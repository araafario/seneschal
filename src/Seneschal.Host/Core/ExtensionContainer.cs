using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Seneschal.Core;

/// <summary>
/// The services of one extension: what its <c>ConfigureServices</c> registered, in a container of its own, so the extension can be loaded,
/// replaced and removed while the app runs (the app's container cannot take or drop a registration once it is built). It resolves like the
/// usual container: singletons live as long as the extension version, scoped services live as long as the browser tab's scope, transient
/// services are made each time, constructors are filled in, and <c>IEnumerable&lt;T&gt;</c> gives every registration. Whatever it does not
/// have comes from the app (the logger, the snackbar). Disposing it disposes what it made and stops its hosted services.
/// </summary>
internal sealed class ExtensionContainer : IAsyncDisposable
{
    private readonly Dictionary<Type, List<ServiceDescriptor>> _byType = [];
    private readonly Dictionary<Type, ServiceDescriptor> _open = [];
    private readonly Dictionary<(ServiceDescriptor, Type), object> _singletons = [];
    private readonly List<object> _owned = [];
    private readonly Func<IServiceProvider> _host;
    private readonly object _gate = new();
    private ContainerScope? _rootScope;
    private List<IHostedService> _hosted = [];
    private bool _disposed;

    public string Id { get; }

    public ExtensionContainer(string id, IEnumerable<ServiceDescriptor> descriptors, Func<IServiceProvider> host)
    {
        Id = id;
        _host = host;
        foreach (var descriptor in descriptors)
        {
            if (descriptor.IsKeyedService)
                throw new ExtensionException($"The extension registers a keyed service ({descriptor.ServiceType.Name}), which extensions cannot use yet.");
            if (descriptor.ServiceType.IsGenericTypeDefinition)
            {
                _open[descriptor.ServiceType] = descriptor;
                continue;
            }
            if (!_byType.TryGetValue(descriptor.ServiceType, out var list)) _byType[descriptor.ServiceType] = list = [];
            list.Add(descriptor);
        }
    }

    /// <summary>True when this container can answer for the type (a registration, or an enumeration of one).</summary>
    public bool Has(Type type)
    {
        if (_byType.ContainsKey(type)) return true;
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (_open.ContainsKey(definition)) return true;
            if (definition == typeof(IEnumerable<>) && _byType.ContainsKey(type.GetGenericArguments()[0])) return true;
        }
        return false;
    }

    /// <summary>The service, or null when this container does not have it. <paramref name="scope"/> holds the scoped services of one browser tab.</summary>
    public object? Resolve(Type type, ContainerScope? scope)
    {
        if (_disposed) return null;

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            var element = type.GetGenericArguments()[0];
            return _byType.TryGetValue(element, out var all) ? ToArray(element, all.Select(d => Instance(d, type: element, scope))) : null;
        }

        if (_byType.TryGetValue(type, out var found)) return Instance(found[^1], type, scope);
        if (type.IsGenericType && _open.TryGetValue(type.GetGenericTypeDefinition(), out var open)) return Instance(open, type, scope);
        return null;
    }

    /// <summary>Every registration of the element type, for the app's <c>IEnumerable</c> to add to its own.</summary>
    public IReadOnlyList<object> ResolveAll(Type element, ContainerScope? scope) =>
        !_disposed && _byType.TryGetValue(element, out var all) ? all.Select(d => Instance(d, element, scope)).ToList() : [];

    public ContainerScope CreateScope(IServiceProvider? host) => new(this, host);

    /// <summary>Starts the hosted services the extension registered (the same <c>IHostedService</c> the app runs for its own).</summary>
    public async Task StartHostedAsync(CancellationToken cancel = default)
    {
        _hosted = [.. ResolveAll(typeof(IHostedService), null).Cast<IHostedService>()];
        foreach (var service in _hosted) await service.StartAsync(cancel);
    }

    public async ValueTask DisposeAsync()
    {
        List<object> owned;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            owned = [.. _owned];
            _owned.Clear();
            _singletons.Clear();
        }

        foreach (var service in _hosted.AsEnumerable().Reverse())
        {
            try { await service.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token); }
            catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException) { }
        }

        if (_rootScope is not null) await _rootScope.DisposeAsync();
        await Dispose(owned);
    }

    internal static async ValueTask Dispose(IEnumerable<object> owned)
    {
        foreach (var item in owned.Reverse())
        {
            try
            {
                if (item is IAsyncDisposable async) await async.DisposeAsync();
                else if (item is IDisposable sync) sync.Dispose();
            }
            catch (Exception)
            {
                // One service that fails to dispose must not leave the others running
            }
        }
    }

    private object Instance(ServiceDescriptor descriptor, Type type, ContainerScope? scope)
    {
        if (descriptor.ImplementationInstance is { } instance) return instance;

        switch (descriptor.Lifetime)
        {
            case ServiceLifetime.Singleton:
                lock (_gate)
                {
                    if (_singletons.TryGetValue((descriptor, type), out var existing)) return existing;
                    var made = Create(descriptor, type, new Resolver(this, null, _host));
                    _singletons[(descriptor, type)] = made;
                    _owned.Add(made);
                    return made;
                }

            case ServiceLifetime.Scoped:
                scope ??= RootScope();
                return scope.GetOrCreate((descriptor, type), () => Create(descriptor, type, new Resolver(this, scope, _host)));

            default:
                var transient = Create(descriptor, type, new Resolver(this, scope, _host));
                // Kept with the scope that asked (or the container), as the usual container does, so it is disposed with it
                if (scope is not null) scope.Own(transient);
                else lock (_gate) _owned.Add(transient);
                return transient;
        }
    }

    private ContainerScope RootScope()
    {
        lock (_gate) return _rootScope ??= new ContainerScope(this, null);
    }

    private static object Create(ServiceDescriptor descriptor, Type type, IServiceProvider provider)
    {
        if (descriptor.ImplementationFactory is { } factory) return factory(provider);
        var implementation = descriptor.ImplementationType ?? throw new InvalidOperationException($"{descriptor.ServiceType.Name} has no implementation");
        if (implementation.IsGenericTypeDefinition) implementation = implementation.MakeGenericType(type.GetGenericArguments());
        return ActivatorUtilities.CreateInstance(provider, implementation);
    }

    private static Array ToArray(Type element, IEnumerable<object> items)
    {
        var list = items.ToList();
        var array = Array.CreateInstance(element, list.Count);
        for (var i = 0; i < list.Count; i++) array.SetValue(list[i], i);
        return array;
    }

    /// <summary>How a service of this extension finds what its constructor asks for: this extension first, then the app.</summary>
    private sealed class Resolver(ExtensionContainer container, ContainerScope? scope, Func<IServiceProvider> host) : IServiceProvider
    {
        public object? GetService(Type type)
        {
            if (type == typeof(IServiceProvider)) return this;
            return container.Resolve(type, scope) ?? (scope?.Host ?? host()).GetService(type);
        }
    }
}

/// <summary>The scoped services of one extension for one browser tab. Lives as long as the tab's scope and disposes what it made with it.</summary>
internal sealed class ContainerScope(ExtensionContainer container, IServiceProvider? host) : IAsyncDisposable
{
    private readonly Dictionary<(Microsoft.Extensions.DependencyInjection.ServiceDescriptor, Type), object> _made = [];
    private readonly List<object> _owned = [];
    private readonly object _gate = new();
    private bool _disposed;

    public ExtensionContainer Container { get; } = container;

    /// <summary>The app's services for this tab, for a scoped service of the extension that needs one of them.</summary>
    public IServiceProvider? Host { get; } = host;

    public object GetOrCreate((Microsoft.Extensions.DependencyInjection.ServiceDescriptor, Type) key, Func<object> create)
    {
        lock (_gate)
        {
            if (_made.TryGetValue(key, out var existing)) return existing;
            var made = create();
            _made[key] = made;
            if (!_disposed) _owned.Add(made);
            return made;
        }
    }

    public void Own(object item)
    {
        lock (_gate) _owned.Add(item);
    }

    public async ValueTask DisposeAsync()
    {
        List<object> owned;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            owned = [.. _owned];
            _owned.Clear();
        }
        await ExtensionContainer.Dispose(owned);
    }
}
