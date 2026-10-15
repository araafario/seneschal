using Seneschal.Sdk;

namespace Seneschal.Core;

public sealed record FailedExtension(string Name, string Folder, string Error, ExtensionSource Origin = ExtensionSource.Installed);

/// <summary>
/// Loads the extensions that are present when the app starts, before the service container is built, so an extension can
/// register its own services. It runs once, from <see cref="ExtensionServiceCollectionExtensions.AddExtensions"/>; the result is
/// then activated by <see cref="DllExtensions.LoadAll"/> once the app is built.
/// </summary>
public sealed class ExtensionStartup
{
    /// <summary>
    /// Extra folders (separated by ';') with extensions that are loaded from a dev build and win over a shipped copy of the same id.
    /// In the Development environment the repository's artifacts/extensions is used without this.
    /// </summary>
    public const string DevFoldersVariable = "SENESCHAL_DEV_EXTENSIONS";

    private sealed record Candidate(string Folder, ExtensionSource Origin, string Id);

    internal List<LoadedExtension> Loaded { get; } = [];

    internal List<FailedExtension> Failed { get; } = [];

    private IReadOnlyList<string> _devFolders = [];
    private IReadOnlyList<string> _fixedDevFolders = [];

    /// <summary>
    /// The folders that are set outside the app (the environment variable, the repository's own). The ones added in Options are kept in the
    /// settings, which can change while the app runs, so DevWatcher reads those itself.
    /// </summary>
    internal IReadOnlyList<string> DevFolderPaths => _fixedDevFolders;

    /// <summary>Every dev folder right now: the fixed ones and the ones added in Options.</summary>
    internal IReadOnlyList<string> CurrentDevFolders() =>
        [.. _fixedDevFolders.Concat(Seneschal.Shell.UiSettings.SavedDevFolders()).Concat(Seneschal.Dev.ProjectsFolder.BuildFolders(Seneschal.Dev.ProjectsFolder.RootFromSaved())).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The folders that hold dev builds: the ones in the environment variable, plus the repository's own when developing.</summary>
    internal static List<string> DevFolders(IHostEnvironment env)
    {
        var folders = (Environment.GetEnvironmentVariable(DevFoldersVariable) ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (env.IsDevelopment() && Seneschal.Dev.DevMode.FindRepoRoot(env.ContentRootPath) is { } root)
            folders.Add(Path.Combine(root, "artifacts", "extensions"));
        return folders;
    }

    internal void LoadAll(IServiceCollection services, IReadOnlyList<string> devFolders)
    {
        _fixedDevFolders = devFolders;
        // What the folders added in Options hold is loaded at the start too, like the others
        _devFolders = [.. devFolders.Concat(Seneschal.Shell.UiSettings.SavedDevFolders()).Concat(Seneschal.Dev.ProjectsFolder.BuildFolders(Seneschal.Dev.ProjectsFolder.RootFromSaved())).Distinct(StringComparer.OrdinalIgnoreCase)];
        var chosen = new List<Candidate>();
        foreach (var candidate in Discover())
        {
            // Dev wins over Bundled wins over Installed; the first one of an id is the one that is used
            if (chosen.Any(c => c.Id.Equals(candidate.Id, StringComparison.OrdinalIgnoreCase)))
            {
                if (candidate.Origin == ExtensionSource.Installed)
                    Failed.Add(new FailedExtension(candidate.Id, candidate.Folder, $"'{candidate.Id}' is already provided by the app or a dev build. Remove this copy.", candidate.Origin));
                continue;
            }
            chosen.Add(candidate);
        }

        foreach (var candidate in chosen)
        {
            try
            {
                var package = ExtensionPackage.FromFolder(candidate.Folder);
                var loaded = ExtensionLoader.Load(package, candidate.Folder, candidate.Origin, candidate.Id);
                try
                {
                    var collected = loaded.CollectServices();
                    RejectAppServices(collected);
                    loaded.HasServices = collected.Count > 0;
                    // Kept with the extension: its own container is made when it is activated, so it can be replaced later without a restart
                    loaded.Services = collected;
                }
                catch
                {
                    loaded.Context.Unload();
                    throw;
                }
                Loaded.Add(loaded);
            }
            catch (Exception ex)
            {
                Failed.Add(new FailedExtension(candidate.Id, candidate.Folder, DllExtensions.Describe(ex), candidate.Origin));
            }
        }
    }

    /// <summary>The folders to look in, in priority order, and where each extension in them comes from.</summary>
    private IEnumerable<Candidate> Discover()
    {
        var roots = new List<(string Path, ExtensionSource Origin)>();
        foreach (var dev in _devFolders)
            roots.Add((dev, ExtensionSource.Dev));
        roots.Add((Path.Combine(AppContext.BaseDirectory, "extensions"), ExtensionSource.Bundled));
        roots.Add((AppPaths.ExtensionsDir, ExtensionSource.Installed));

        foreach (var (root, origin) in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var folder in Directory.GetDirectories(root).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string id;
                try
                {
                    id = IdOf(folder);
                }
                catch (Exception ex)
                {
                    Failed.Add(new FailedExtension(Path.GetFileName(folder), folder, DllExtensions.Describe(ex), origin));
                    continue;
                }
                yield return new Candidate(folder, origin, id);
            }
        }
    }

    /// <summary>The id in the folder's manifest, or the folder's name for an extension that has none.</summary>
    private static string IdOf(string folder)
    {
        var manifest = Path.Combine(folder, ExtensionPackage.ManifestFile);
        return File.Exists(manifest) ? ExtensionManifest.Parse(File.ReadAllBytes(manifest)).Id : Path.GetFileName(folder);
    }

    /// <summary>An extension adds services of its own; it may not replace the app's.</summary>
    internal static void RejectAppServices(IEnumerable<ServiceDescriptor> descriptors)
    {
        var app = typeof(ExtensionStartup).Assembly;
        var sdk = typeof(IToolModule).Assembly;
        var own = descriptors.FirstOrDefault(d => d.ServiceType.Assembly == app || d.ServiceType.Assembly == sdk);
        if (own is not null)
            throw new ExtensionException($"The extension registers {own.ServiceType.Name}, which belongs to the app.");
    }
}
