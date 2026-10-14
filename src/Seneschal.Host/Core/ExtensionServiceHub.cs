using System.Collections;
using Microsoft.Extensions.DependencyInjection;

namespace Seneschal.Core;

/// <summary>
/// Where the services of the loaded extensions are found. Each extension has its own <see cref="ExtensionContainer"/>, put here when it is
/// loaded and taken out (and disposed) when it is replaced or removed, so no extension needs the app to be restarted. The app's own container
/// is wrapped by <see cref="HostServiceProvider"/>, which asks this when the app's container does not have a type: that is how a page of an
/// extension gets the services it asks for with <c>@inject</c>.
/// </summary>
public sealed class ExtensionServiceHub
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ExtensionContainer> _containers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The app's service provider including the extensions' services. What is handed to an extension as "the app's container".</summary>
    public IServiceProvider Host { get; internal set; } = null!;

    /// <summary>
    /// Puts the extension's container in and returns the one it replaces, which stays alive until <see cref="Release"/> is called: the old
    /// version keeps serving until the new one is fully in place.
    /// </summary>
    internal ExtensionContainer? Swap(string id, ExtensionContainer container)
    {
        ExtensionContainer? old;
        lock (_gate)
        {
            _containers.TryGetValue(id, out old);
            _containers[id] = container;
        }
        return old;
    }

    /// <summary>Undoes <see cref="Swap"/> when the new version could not be activated: the old container is back and the new one is disposed.</summary>
    internal void Restore(string id, ExtensionContainer? previous, ExtensionContainer container)
    {
        lock (_gate)
        {
            if (previous is null) _containers.Remove(id);
            else _containers[id] = previous;
        }
        Release(container);
    }

    /// <summary>Takes the extension's container out and disposes it.</summary>
    internal void Remove(string id)
    {
        ExtensionContainer? old;
        lock (_gate) _containers.Remove(id, out old);
        Release(old);
    }

    /// <summary>Disposed without waiting: the page that asked for the change should not wait for an old service to stop.</summary>
    internal void Release(ExtensionContainer? container)
    {
        if (container is null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await container.DisposeAsync();
                Host?.GetService<ILoggerFactory>()?.CreateLogger("Extensions").LogInformation("The services of {Id} that were replaced or removed are disposed", container.Id);
            }
            catch (Exception)
            {
                // The old version is gone either way
            }
        });
    }

    private ExtensionContainer[] Snapshot()
    {
        lock (_gate) return [.. _containers.Values];
    }

    /// <summary>The service from the first extension that has it, or null.</summary>
    internal object? Resolve(Type type, ScopeSet? scopes)
    {
        foreach (var container in Snapshot())
            if (container.Has(type))
                return container.Resolve(type, scopes?.For(container));
        return null;
    }

    /// <summary>What every extension has registered for an element type, to add to the app's own <c>IEnumerable</c> of it.</summary>
    internal IReadOnlyList<object> ResolveAll(Type element, ScopeSet? scopes) =>
        Snapshot().SelectMany(c => c.ResolveAll(element, scopes?.For(c))).ToList();

    /// <summary>What the app's container answered, with what the extensions add: the same answer, or the extension's when the app has none.</summary>
    internal object? Merge(Type type, object? fromApp, ScopeSet? scopes)
    {
        if (fromApp is null) return Resolve(type, scopes);
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>) && fromApp is IEnumerable own)
        {
            var element = type.GetGenericArguments()[0];
            var extra = ResolveAll(element, scopes);
            if (extra.Count == 0) return fromApp;
            var items = own.Cast<object>().Concat(extra).ToList();
            var array = Array.CreateInstance(element, items.Count);
            for (var i = 0; i < items.Count; i++) array.SetValue(items[i], i);
            return array;
        }
        return fromApp;
    }
}

/// <summary>The scoped services of the extensions for one browser tab: one <see cref="ContainerScope"/> per extension, made when it is first needed.</summary>
internal sealed class ScopeSet(IServiceProvider host) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<ExtensionContainer, ContainerScope> _scopes = [];
    private bool _disposed;

    public ContainerScope For(ExtensionContainer container)
    {
        lock (_gate)
        {
            if (!_scopes.TryGetValue(container, out var scope)) _scopes[container] = scope = container.CreateScope(host);
            return scope;
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<ContainerScope> scopes;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            scopes = [.. _scopes.Values];
        }
        foreach (var scope in scopes) await scope.DisposeAsync();
    }
}

/// <summary>
/// The app's service provider. It is the app's own container, which answers first, with the extensions behind it: a type the app does not
/// know is looked for in the loaded extensions. The scopes it makes (one per browser tab) do the same, with the tab's scoped services.
/// </summary>
internal sealed class HostServiceProvider(ServiceProvider app, ExtensionServiceHub hub) : IServiceProvider, IServiceScopeFactory, IKeyedServiceProvider, IDisposable, IAsyncDisposable
{
    public object? GetService(Type type)
    {
        if (type == typeof(IServiceProvider) || type == typeof(IServiceScopeFactory)) return this;
        return hub.Merge(type, app.GetService(type), null);
    }

    public object? GetKeyedService(Type type, object? key) => ((IKeyedServiceProvider)app).GetKeyedService(type, key);

    public object GetRequiredKeyedService(Type type, object? key) => ((IKeyedServiceProvider)app).GetRequiredKeyedService(type, key);

    public IServiceScope CreateScope() => new HostScope(app.CreateScope(), hub, this);

    public void Dispose() => app.Dispose();

    public ValueTask DisposeAsync() => app.DisposeAsync();
}

/// <summary>One browser tab's scope: the app's scope, plus the extensions' scoped services for it.</summary>
internal sealed class HostScope : IServiceScope, IServiceProvider, IKeyedServiceProvider, IAsyncDisposable
{
    private readonly IServiceScope _app;
    private readonly ExtensionServiceHub _hub;
    private readonly HostServiceProvider _root;
    private readonly ScopeSet _scopes;

    public HostScope(IServiceScope app, ExtensionServiceHub hub, HostServiceProvider root)
    {
        _app = app;
        _hub = hub;
        _root = root;
        _scopes = new ScopeSet(this);
    }

    public IServiceProvider ServiceProvider => this;

    public object? GetService(Type type)
    {
        if (type == typeof(IServiceProvider)) return this;
        if (type == typeof(IServiceScopeFactory)) return _root;
        return _hub.Merge(type, _app.ServiceProvider.GetService(type), _scopes);
    }

    public object? GetKeyedService(Type type, object? key) => ((IKeyedServiceProvider)_app.ServiceProvider).GetKeyedService(type, key);

    public object GetRequiredKeyedService(Type type, object? key) => ((IKeyedServiceProvider)_app.ServiceProvider).GetRequiredKeyedService(type, key);

    public void Dispose()
    {
        _scopes.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _app.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _scopes.DisposeAsync();
        if (_app is IAsyncDisposable async) await async.DisposeAsync();
        else _app.Dispose();
    }
}

/// <summary>Builds the app's container as usual and wraps it, so the extensions' services can be added and dropped while the app runs.</summary>
internal sealed class SeneschalServiceProviderFactory(ExtensionServiceHub hub, bool validate) : IServiceProviderFactory<IServiceCollection>
{
    public IServiceCollection CreateBuilder(IServiceCollection services) => services;

    public IServiceProvider CreateServiceProvider(IServiceCollection services)
    {
        UseHostScopesForCircuits(services);

        // The same checks the app has in Development by default
        var app = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = validate, ValidateOnBuild = validate });
        var host = new HostServiceProvider(app, hub);
        hub.Host = host;
        return host;
    }

    /// <summary>
    /// A page of an extension is made inside the scope of its browser tab (a Blazor circuit), and that scope is made by the factory the
    /// framework gives itself: the app's own container, which knows nothing of the extensions. So the circuit factory is made again with
    /// our scope factory in its place, which is what lets a page get the services of its extension with <c>@inject</c>. The framework's
    /// type is internal, so it is found by name; when it is not found (a framework that changed) the app still runs, and extensions
    /// with services cannot be injected until this is updated.
    /// </summary>
    private void UseHostScopesForCircuits(IServiceCollection services)
    {
        var circuitFactory = System.Reflection.Assembly.Load("Microsoft.AspNetCore.Components.Server")
            .GetType("Microsoft.AspNetCore.Components.Server.Circuits.ICircuitFactory");
        var index = circuitFactory is null ? -1 : services.ToList().FindLastIndex(d => d.ServiceType == circuitFactory);
        if (circuitFactory is null || index < 0 || services[index].ImplementationType is not { } implementation)
        {
            Console.Error.WriteLine("The circuit factory of Blazor was not found, so the services of extensions cannot be injected into their pages.");
            return;
        }

        var original = services[index];
        services[index] = new ServiceDescriptor(circuitFactory,
            provider => ActivatorUtilities.CreateInstance(provider, implementation, (IServiceScopeFactory)hub.Host),
            original.Lifetime);
    }
}
