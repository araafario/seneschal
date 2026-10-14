using System.Reflection;
using Seneschal.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Seneschal.Core;

/// <summary>The extension's view of itself, handed to its lifecycle hooks.</summary>
internal sealed record ExtensionContext(string Id, string Name, Version Version, string DataDirectory, string Directory) : IExtensionContext;

/// <summary>An extension that has been read and loaded into its own load context, but not necessarily activated yet.</summary>
internal sealed class LoadedExtension
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required Version Version { get; init; }

    public required ExtensionSource Origin { get; init; }

    public required string Folder { get; init; }

    public required ExtensionManifest? Manifest { get; init; }

    public required ExtensionLoadContext Context { get; init; }

    public required IReadOnlyList<IToolModule> Modules { get; init; }

    public required ExtensionContext Info { get; init; }

    /// <summary>True when the extension registers its own services.</summary>
    public bool HasServices { get; set; }

    /// <summary>What the extension registered. Turned into its <see cref="Container"/> when it is activated.</summary>
    public ServiceCollection Services { get; set; } = [];

    /// <summary>The extension's own services, made when it is activated and disposed when it is replaced or removed.</summary>
    internal ExtensionContainer? Container { get; set; }

    public IReadOnlyList<ToolMetadata> Tools => Modules.Select(m => m.Metadata).ToList();

    public IEnumerable<IToolLifecycle> Lifecycles => Modules.OfType<IToolLifecycle>();

    public IReadOnlyList<string> Styles => Manifest?.Styles ?? [];

    /// <summary>The folder with the extension's static files (stylesheets), served by the app.</summary>
    public string WebRoot => Path.Combine(Folder, "wwwroot");

    /// <summary>
    /// Asks every lifecycle hook for its services, into a collection of its own first, so a hook that fails halfway leaves nothing
    /// behind. Returns what it registered.
    /// </summary>
    public ServiceCollection CollectServices()
    {
        var collected = new ServiceCollection();
        foreach (var lifecycle in Lifecycles)
            lifecycle.ConfigureServices(collected, Info);
        return collected;
    }
}

/// <summary>Turns a package into a <see cref="LoadedExtension"/>: loads the assembly, finds its tools and checks that it can work here.</summary>
internal static class ExtensionLoader
{
    // Extensions built when the app and its SDK had an earlier name reference these assemblies
    private static readonly string[] EarlierSdkNames = ["DevToolbelt.Contracts", "DevToolbelt.Sdk"];

    /// <summary>
    /// Loads the package into a new load context. The caller owns the result and must unload its context if it does not keep it.
    /// <paramref name="fallbackId"/> names an extension that has no manifest (a plain DLL, or an older package).
    /// </summary>
    public static LoadedExtension Load(ExtensionPackage package, string folder, ExtensionSource origin, string fallbackId)
    {
        var manifest = package.Manifest;
        manifest?.RequireCompatibleSdk();

        var id = manifest?.Id ?? fallbackId;
        var context = new ExtensionLoadContext(id, folder, package.Libraries);
        try
        {
            Assembly assembly;
            try
            {
                // A dev build is loaded with its symbols, so a debugger attached to the app can step into it
                var symbols = origin == ExtensionSource.Dev && package.MainSymbols is { } pdb ? new MemoryStream(pdb) : null;
                assembly = context.LoadFromStream(new MemoryStream(package.Main.Bytes), symbols);
            }
            catch (BadImageFormatException)
            {
                throw new ExtensionException("This is not a .NET assembly (or it targets another platform).");
            }

            RequireReferences(package);
            var modules = FindModules(assembly);
            var version = manifest?.ParsedVersion ?? assembly.GetName().Version ?? new Version(1, 0, 0);
            var name = manifest?.Name ?? id;
            var data = Path.Combine(AppPaths.ExtensionDataDir, id);

            return new LoadedExtension
            {
                Id = id,
                Name = name,
                Version = version,
                Origin = origin,
                Folder = folder,
                Manifest = manifest,
                Context = context,
                Modules = modules,
                Info = new ExtensionContext(id, name, version, data, folder),
            };
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    private static List<IToolModule> FindModules(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var inner = ex.LoaderExceptions.FirstOrDefault();
            throw new ExtensionException(MissingLibrary(inner) ?? $"A type in the DLL could not be loaded: {inner?.Message ?? ex.Message}");
        }

        var modules = new List<IToolModule>();
        foreach (var type in types.Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IToolModule).IsAssignableFrom(t)))
        {
            IToolModule module;
            try
            {
                module = (IToolModule)Activator.CreateInstance(type)!;
            }
            catch (Exception ex)
            {
                var cause = ex.InnerException ?? ex;
                throw new ExtensionException(MissingLibrary(cause) ?? $"Tool '{type.Name}' could not be created: {cause.Message}");
            }

            var meta = module.Metadata;
            if (string.IsNullOrWhiteSpace(meta.Id) || string.IsNullOrWhiteSpace(meta.Title) || meta.ComponentType is null)
                throw new ExtensionException($"Tool '{type.Name}' needs an Id, a Title and a ComponentType.");
            modules.Add(module);
        }

        if (modules.Count == 0)
            throw new ExtensionException("No tool found. The extension must contain a class that implements IToolModule.");
        return modules;
    }

    /// <summary>Every library the extension DLL uses directly must be in the package or provided by the app.</summary>
    private static void RequireReferences(ExtensionPackage package)
    {
        var missing = package.MainReferences
            .Where(r => !package.Libraries.ContainsKey(r) && !ExtensionLoadContext.HostHas(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (missing.Count == 0) return;

        if (missing.Any(m => EarlierSdkNames.Contains(m, StringComparer.OrdinalIgnoreCase)))
            throw new ExtensionException("This extension was built for DevToolbelt, the earlier name of this app: it uses DevToolbelt.Sdk, which is now Seneschal.Sdk. Rebuild it against the current SDK.");

        var list = string.Join(", ", missing.Select(m => $"'{m}'"));
        throw new ExtensionException(missing.Count == 1
            ? $"The extension needs the library {list}, which is not in the package and not provided by the app. Package it again so the library is included."
            : $"The extension needs the libraries {list}, which are not in the package and not provided by the app. Package it again so they are included.");
    }

    /// <summary>A readable message when the cause is a library that is neither in the package nor provided by the app.</summary>
    private static string? MissingLibrary(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is not FileNotFoundException { FileName: { Length: > 0 } file }) continue;
            string library;
            try { library = new AssemblyName(file).Name ?? file; }
            catch (Exception parse) when (parse is ArgumentException or FileLoadException) { library = file; }
            return $"The extension needs the library '{library}', which is not in the package and not provided by the app. Package it again so the library is included.";
        }
        return null;
    }
}
