using System.Text.RegularExpressions;
using Seneschal.Sdk;

namespace Seneschal.Core;

/// <summary>An extension that is loaded. <see cref="Name"/> is its id; <see cref="Title"/> what the manifest calls it.</summary>
public sealed record DllExtension(string Name, string Title, string Folder, string Version, string[] ToolIds, ExtensionSource Origin, bool HasServices);

public sealed record ImportResult(bool Ok, string Message);

/// <summary>
/// The extensions of the running app. Those present at startup were loaded before the container was built (see
/// <see cref="ExtensionStartup"/>) and are activated here. The user can also import a package while the app runs:
/// an extension goes live at once. Its services are in a container of its own (see <see cref="ExtensionServiceHub"/>), so no extension needs the app
/// to be restarted: a new version replaces the container, and the old one is disposed.
/// Imported packages live in %AppData%/Seneschal/extensions/&lt;id&gt;/ and are loaded from memory, so their files are never locked.
/// </summary>
public sealed partial class DllExtensions
{
    public const long MaxSizeBytes = 50 * 1024 * 1024;
    private const string FilePrefix = "Seneschal.Extension.";

    private readonly object _gate = new();
    private readonly List<DllExtension> _loaded = [];
    private readonly List<FailedExtension> _failed = [];
    private readonly HashSet<string> _pendingRestart = new(StringComparer.OrdinalIgnoreCase);
    // Pending extensions that are a dev build (nothing was imported, so there is nothing to remove in Options)
    private readonly HashSet<string> _pendingDev = new(StringComparer.OrdinalIgnoreCase);
    // The runtime unloads a collectible context once nothing references it, so each one is held here until its extension goes away.
    private readonly Dictionary<string, LoadedExtension> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly IToolRegistry _registry;
    private readonly ExtensionSettings _settings;
    private readonly ExtensionStartup _startup;
    private readonly ExtensionAssets _assets;
    private readonly ExtensionServiceHub _hub;

    public event Action? Changed;

    public DllExtensions(IToolRegistry registry, ExtensionSettings settings, ExtensionStartup startup, ExtensionAssets assets, ExtensionServiceHub hub)
    {
        _registry = registry;
        _settings = settings;
        _startup = startup;
        _assets = assets;
        _hub = hub;
    }

    public IReadOnlyList<DllExtension> Loaded
    {
        get { lock (_gate) return [.. _loaded]; }
    }

    public IReadOnlyList<FailedExtension> Failed
    {
        get { lock (_gate) return [.. _failed]; }
    }

    /// <summary>True once a change needs the app to be restarted to take full effect.</summary>
    /// <summary>The tool classes of every active extension with the id of the extension, for features that ask the tools themselves (the AI assistant).</summary>
    internal IReadOnlyList<(string Package, IToolModule Module)> Modules()
    {
        lock (_gate) return _active.Values.SelectMany(a => a.Modules.Select(m => (a.Id, m))).ToList();
    }

    public bool RestartPending
    {
        get { lock (_gate) return _pendingRestart.Count > 0; }
    }

    /// <summary>The extensions that wait for a restart.</summary>
    public IReadOnlyList<string> PendingRestart
    {
        get { lock (_gate) return [.. _pendingRestart.Order()]; }
    }

    /// <summary>True when a pending extension is a dev build rather than an imported package.</summary>
    public bool IsDevPending(string name)
    {
        lock (_gate) return _pendingDev.Contains(name);
    }

    /// <summary>Activates what was loaded at startup: registers the tools and stylesheets and tells the extensions the app has started.</summary>
    public void LoadAll()
    {
        lock (_gate) _failed.AddRange(_startup.Failed);

        foreach (var loaded in _startup.Loaded)
        {
            try
            {
                Activate(loaded);
            }
            catch (Exception ex)
            {
                loaded.Context.Unload();
                lock (_gate) _failed.Add(new FailedExtension(loaded.Id, loaded.Folder, Describe(ex), loaded.Origin));
            }
        }
        Changed?.Invoke();
    }

    /// <summary>Validates and installs a package. Importing an extension whose id is already installed updates it.</summary>
    public ImportResult Import(string fileName, byte[] bytes)
    {
        try
        {
            if (bytes.Length == 0) throw new ExtensionException("The file is empty.");
            if (bytes.Length > MaxSizeBytes) throw new ExtensionException($"The file is larger than {MaxSizeBytes / 1024 / 1024} MB.");

            var fallback = NameFrom(fileName);
            var package = IsArchive(fileName) ? ExtensionPackage.FromZip(bytes) : ExtensionPackage.FromDll(fileName, bytes);
            var id = package.Manifest?.Id ?? fallback;
            var folder = Path.Combine(AppPaths.ExtensionsDir, id);

            DllExtension? existing;
            lock (_gate) existing = _loaded.FirstOrDefault(e => e.Name.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (existing is { Origin: not ExtensionSource.Installed })
                throw new ExtensionException($"'{id}' is {(existing.Origin == ExtensionSource.Dev ? "loaded from a dev build" : "part of the app")} and cannot be replaced by an import.");

            var loaded = ExtensionLoader.Load(package, folder, ExtensionSource.Installed, id);
            try
            {
                var collected = loaded.CollectServices();
                ExtensionStartup.RejectAppServices(collected);
                loaded.HasServices = collected.Count > 0;
                loaded.Services = collected;

                var note = BundledNote(package);

                // Register the tools first (that validates the extension), then put the files in place, and only then publish
                // the stylesheets and tell the extension it has started: both need the files to exist.
                Activate(loaded, replacing: existing is null ? null : _active.GetValueOrDefault(id), finish: false);
                try
                {
                    package.WriteTo(folder);
                }
                catch
                {
                    Deactivate(loaded);
                    throw;
                }
                Finish(loaded);

                var tools = loaded.Tools.Count == 1 ? "1 tool" : $"{loaded.Tools.Count} tools";
                Changed?.Invoke();
                return new ImportResult(true, (existing is null
                    ? $"Imported '{loaded.Name}' ({tools})."
                    : $"Updated '{loaded.Name}' to v{loaded.Version.ToString(3)} ({tools}).") + note);
            }
            catch
            {
                loaded.Context.Unload();
                throw;
            }
        }
        catch (Exception ex)
        {
            return new ImportResult(false, Describe(ex));
        }
    }

    /// <summary>Deletes an installed extension's files and hides its tools right away. Also used to clear a failed extension.</summary>
    public ImportResult Remove(string name)
    {
        DllExtension? loaded;
        FailedExtension? failed;
        lock (_gate)
        {
            loaded = _loaded.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            failed = _failed.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        // An extension that was imported and is waiting for a restart is on disk but not loaded
        bool waiting;
        lock (_gate) waiting = loaded is null && failed is null && _pendingRestart.Contains(name) && !_pendingDev.Contains(name);
        var folder = loaded?.Folder ?? failed?.Folder ?? (waiting ? Path.Combine(AppPaths.ExtensionsDir, name) : null);
        if (folder is null) return new ImportResult(false, $"No extension named '{name}'.");
        if (!waiting && (loaded?.Origin ?? failed!.Origin) != ExtensionSource.Installed)
            return new ImportResult(false, $"'{name}' is part of the app or a dev build. Turn it off instead of removing it.");

        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ImportResult(false, $"Could not delete the extension's files: {ex.Message}");
        }

        lock (_gate) _failed.RemoveAll(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (loaded is not null)
        {
            LoadedExtension? active;
            lock (_gate)
            {
                _loaded.RemoveAll(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                _active.Remove(loaded.Name, out active);
            }
            _hub.Remove(loaded.Name);
            _registry.Unregister(loaded.ToolIds);
            _assets.Remove(loaded.Name);
            foreach (var id in loaded.ToolIds) _settings.SetEnabled(id, true); // forget the old on/off choice
            active?.Context.Unload();
        }
        if (waiting) lock (_gate) _pendingRestart.Remove(name);
        Changed?.Invoke();
        return new ImportResult(true, $"Removed '{name}'.");
    }

    /// <summary>
    /// Loads, or reloads, an extension from a folder the Developer page just built, as a dev extension. One without services of its
    /// own goes live at once, with or without services. Only one that replaces a copy that ships with the app waits for a restart.
    /// </summary>
    public ImportResult LoadDev(string folder)
    {
        try
        {
            var package = ExtensionPackage.FromFolder(folder);
            var manifest = package.Manifest ?? throw new ExtensionException("The build has no extension.json.");
            var id = manifest.Id;

            DllExtension? existing;
            lock (_gate) existing = _loaded.FirstOrDefault(e => e.Name.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (existing is { Origin: ExtensionSource.Installed })
                throw new ExtensionException($"'{id}' is also installed from a package. Remove the installed copy in Options, Extensions first.");

            var loaded = ExtensionLoader.Load(package, folder, ExtensionSource.Dev, id);
            try
            {
                var collected = loaded.CollectServices();
                ExtensionStartup.RejectAppServices(collected);
                loaded.HasServices = collected.Count > 0;
                loaded.Services = collected;

                string? why = null;
                if (existing is { Origin: not ExtensionSource.Dev }) why = "The running app uses the copy that ships with it";
                if (why is not null)
                {
                    loaded.Context.Unload();
                    lock (_gate)
                    {
                        _pendingRestart.Add(id);
                        _pendingDev.Add(id);
                        _failed.RemoveAll(e => e.Name.Equals(id, StringComparison.OrdinalIgnoreCase));
                    }
                    Changed?.Invoke();
                    return new ImportResult(true, "");
                }

                Activate(loaded, replacing: existing is null ? null : _active.GetValueOrDefault(id));
                Changed?.Invoke();
                var tools = loaded.Tools.Count == 1 ? "1 tool" : $"{loaded.Tools.Count} tools";
                return new ImportResult(true, existing is null
                    ? $"Loaded {loaded.Name} with {tools}"
                    : $"Reloaded {loaded.Name} with {tools}");
            }
            catch
            {
                loaded.Context.Unload();
                throw;
            }
        }
        catch (Exception ex)
        {
            return new ImportResult(false, Describe(ex));
        }
    }

    /// <summary>
    /// Deletes the staged folder of a dev build (or of one that failed to load) and unloads it. A dev build is output, so building again brings it
    /// back. Only a folder inside a dev folder is ever deleted.
    /// </summary>
    public ImportResult RemoveDevBuild(string name)
    {
        DllExtension? loaded;
        FailedExtension? failed;
        lock (_gate)
        {
            loaded = _loaded.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            failed = _failed.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        // The dev build is the loaded one, or the one that failed to load. Not the installed copy of the same id, which can be loaded beside a dev
        // build that failed because of it (and whose folder must never be deleted here)
        var folder = loaded is { Origin: ExtensionSource.Dev } ? loaded.Folder
            : failed is { Origin: ExtensionSource.Dev } ? failed.Folder
            : null;
        if (folder is null) return new ImportResult(false, $"'{name}' is not a dev build.");

        var full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        var inside = _startup.CurrentDevFolders().Any(root =>
            full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        if (!inside) return new ImportResult(false, $"'{name}' is not in a dev folder, so it is not deleted.");

        try
        {
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ImportResult(false, $"Could not delete the build: {ex.Message}");
        }

        lock (_gate) _failed.RemoveAll(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        UnloadDev(name);
        Changed?.Invoke();
        return new ImportResult(true, $"Removed the build of '{name}'.");
    }

    /// <summary>A dev build that could not be loaded is shown in Options like one that failed at startup, until a later build loads.</summary>
    public void ReportDevFailure(string id, string folder, string message)
    {
        lock (_gate)
        {
            _failed.RemoveAll(e => e.Name.Equals(id, StringComparison.OrdinalIgnoreCase));
            _failed.Add(new FailedExtension(id, folder, message, ExtensionSource.Dev));
        }
        Changed?.Invoke();
    }

    /// <summary>Hides a dev extension's tools and releases it (the project was deleted). Its services are disposed.</summary>
    public void UnloadDev(string id)
    {
        DllExtension? loaded;
        LoadedExtension? active = null;
        lock (_gate)
        {
            _pendingRestart.Remove(id);
            _pendingDev.Remove(id);
            loaded = _loaded.FirstOrDefault(e => e.Name.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (loaded is { Origin: ExtensionSource.Dev })
            {
                _loaded.Remove(loaded);
                _active.Remove(id, out active);
            }
        }
        if (loaded is not { Origin: ExtensionSource.Dev }) return;
        _hub.Remove(id);

        _registry.Unregister(loaded.ToolIds);
        _assets.Remove(id);
        foreach (var tool in loaded.ToolIds) _settings.SetEnabled(tool, true);
        active?.Context.Unload();
        Changed?.Invoke();
    }

    internal static string Describe(Exception ex) => ex is ExtensionException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";

    /// <summary>
    /// Registers an extension's tools and, when <paramref name="finish"/> is true, publishes its stylesheets and tells it the app has
    /// started. Throws when a tool id is taken.
    /// </summary>
    private void Activate(LoadedExtension loaded, LoadedExtension? replacing = null, bool finish = true)
    {
        var version = loaded.Version.ToString(3);
        var infos = loaded.Tools.Select(t => new ExtensionInfo(t, loaded.Origin, loaded.Id, version)).ToList();

        // The services go in before the tools are shown: a page that is drawn the moment the tools change must already find them. The
        // version they replace keeps living until the tools are registered (a conflict with another extension undoes all of it)
        var container = new ExtensionContainer(loaded.Id, loaded.Services, () => _hub.Host);
        var previous = _hub.Swap(loaded.Id, container);

        // Conflicts surface as ExtensionException with a readable message
        try { _registry.Register(infos); }
        catch (InvalidOperationException ex)
        {
            _hub.Restore(loaded.Id, previous, container);
            throw new ExtensionException(ex.Message);
        }

        // The services of the version it replaces are disposed here, which stops what they were doing
        loaded.Container = container;
        _hub.Release(previous);
        StartHosted(container, loaded.Id);

        var entry = new DllExtension(loaded.Id, loaded.Name, loaded.Folder, version, loaded.Tools.Select(t => t.Id).ToArray(), loaded.Origin, loaded.HasServices);
        lock (_gate)
        {
            _loaded.RemoveAll(e => e.Name.Equals(loaded.Id, StringComparison.OrdinalIgnoreCase));
            _failed.RemoveAll(e => e.Name.Equals(loaded.Id, StringComparison.OrdinalIgnoreCase));
            _loaded.Add(entry);
            _active[loaded.Id] = loaded;
        }

        if (replacing is not null)
        {
            // Tools that the previous version had and the new one dropped
            _registry.Unregister(replacing.Tools.Select(t => t.Id).Except(loaded.Tools.Select(t => t.Id), StringComparer.OrdinalIgnoreCase));
            replacing.Context.Unload();
        }

        if (finish) Finish(loaded);
    }

    /// <summary>Makes the extension's stylesheets available and tells it the app has started. Its files must be on disk by now.</summary>
    private void Finish(LoadedExtension loaded)
    {
        _assets.Set(loaded.Id, loaded.WebRoot, loaded.Version.ToString(3), loaded.Styles);

        foreach (var lifecycle in loaded.Lifecycles)
        {
            try
            {
                lifecycle.Started(_hub.Host, loaded.Info);
            }
            catch (Exception ex)
            {
                _hub.Host.GetService<ILoggerFactory>()?.CreateLogger("Extensions").LogError(ex, "Extension {Id} failed to start", loaded.Id);
            }
        }
    }

    /// <summary>Starts the hosted services an extension registered. A failure is logged: the extension is loaded either way.</summary>
    private void StartHosted(ExtensionContainer container, string id)
    {
        _ = Task.Run(async () =>
        {
            try { await container.StartHostedAsync(); }
            catch (Exception ex) { _hub.Host.GetService<ILoggerFactory>()?.CreateLogger("Extensions").LogError(ex, "A hosted service of {Id} failed to start", id); }
        });
    }

    /// <summary>Undoes <see cref="Activate"/> for an extension that was just imported.</summary>
    private void Deactivate(LoadedExtension loaded)
    {
        _hub.Remove(loaded.Id);
        _registry.Unregister(loaded.Tools.Select(t => t.Id));
        _assets.Remove(loaded.Id);
        lock (_gate)
        {
            _loaded.RemoveAll(e => e.Name.Equals(loaded.Id, StringComparison.OrdinalIgnoreCase));
            _active.Remove(loaded.Id);
        }
    }

    private static string BundledNote(ExtensionPackage package)
    {
        var shared = package.Libraries.Values.Count(ExtensionLoadContext.HostProvides);
        return shared == 0 ? "" : $"\n{shared} bundled {(shared == 1 ? "library was" : "libraries were")} not used because the app already provides {(shared == 1 ? "it" : "them")}";
    }

    private static bool IsArchive(string fileName) =>
        fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".snx", StringComparison.OrdinalIgnoreCase);

    private static string NameFrom(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !IsArchive(fileName))
            throw new ExtensionException("Choose a .snx, .zip or .dll file.");
        if (stem.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase))
            stem = stem[FilePrefix.Length..];
        if (!SafeName().IsMatch(stem))
            throw new ExtensionException($"'{stem}' is not a valid extension name (letters, digits, '.', '-' and '_' only).");
        return stem;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex SafeName();
}
