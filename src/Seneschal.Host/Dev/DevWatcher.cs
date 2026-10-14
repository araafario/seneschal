using Seneschal.Core;
using Seneschal.Shell;

namespace Seneschal.Dev;

/// <summary>
/// Live reload for developing an extension. It watches the dev folders (<c>SENESCHAL_DEV_EXTENSIONS</c>, and the repository's
/// <c>artifacts/extensions</c> when running from source) and, when a build writes an extension there, loads the new build at once:
/// the page, the stylesheets and the assistant tools follow, with no import and no restart. The services of an extension that has services of its
/// own are swapped too: each extension has a container of its own that is replaced. A build is many file writes, so it waits until they stop
/// before it loads.
/// </summary>
public sealed class DevWatcher(ExtensionStartup startup, DllExtensions extensions, Restarter restarter, UiSettings ui, DevMode dev, ILogger<DevWatcher> log) : BackgroundService
{
    /// <summary>How long the folder must be quiet before a build counts as finished.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(1200);

    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _waiting = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _one = new(1, 1);
    private bool _restarting;

    /// <summary>Where a dev folder comes from, which decides whether it can be removed here.</summary>
    public sealed record DevFolder(string Path, string Source, bool Exists, bool Removable);

    /// <summary>The folders that hold dev builds: the environment variable, the repository's own when running from source, and the ones added in Options.</summary>
    public IReadOnlyList<string> Folders =>
        [.. startup.DevFolderPaths.Concat(ui.DevFolders).Distinct(StringComparer.OrdinalIgnoreCase)];

    public IReadOnlyList<DevFolder> Entries
    {
        get
        {
            var added = ui.DevFolders;
            return [.. Folders.Select(f => new DevFolder(f,
                added.Contains(f, StringComparer.OrdinalIgnoreCase) ? "Added here"
                    : dev.DevBuildsDir is { } repo && string.Equals(Path.GetFullPath(repo), Path.GetFullPath(f), StringComparison.OrdinalIgnoreCase) ? "This repository"
                    : "Environment variable",
                Directory.Exists(f),
                added.Contains(f, StringComparer.OrdinalIgnoreCase)))];
        }
    }

    /// <summary>
    /// Adds a folder to watch, and returns the folder as it was saved. A project folder (one with a .csproj) means the folder its build stages into,
    /// artifacts/extensions inside it. The folder need not exist: the first build makes it.
    /// </summary>
    public string AddFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Give the folder");
        var full = System.IO.Path.GetFullPath(path.Trim().Trim('"'));
        if (Directory.Exists(full) && Directory.GetFiles(full, "*.csproj").Length > 0)
            full = System.IO.Path.Combine(full, "artifacts", "extensions");
        ui.AddDevFolder(full);
        return full;
    }

    public void RemoveFolder(string path) => ui.RemoveDevFolder(path);

    // What was there when the app started is loaded by the start. Only a folder that comes later is read for what it holds
    private HashSet<string>? _atStart;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        // A folder is picked up when it is added, and one can be made by the first build long after the app started, so they are looked for all along
        ui.Changed += OnSettingsChanged;
        _atStart = new HashSet<string>(Folders.Where(Directory.Exists), StringComparer.OrdinalIgnoreCase);
        while (!stop.IsCancellationRequested)
        {
            Attach();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), stop);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void OnSettingsChanged() => _ = Task.Run(Attach);

    /// <summary>Starts watching the folders that are there and not watched yet, and stops watching the ones that are no longer in the list.</summary>
    private void Attach()
    {
        lock (_watchers)
        {
            var wanted = Folders;
            foreach (var gone in _watchers.Keys.Where(k => !wanted.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
            {
                _watchers[gone].Dispose();
                _watchers.Remove(gone);
            }
            foreach (var folder in wanted.Where(f => !_watchers.ContainsKey(f) && Directory.Exists(f)))
                Watch(folder, catchUp: _atStart?.Contains(folder) != true);
        }
    }

    public override void Dispose()
    {
        ui.Changed -= OnSettingsChanged;
        foreach (var watcher in _watchers.Values) watcher.Dispose();
        foreach (var wait in _waiting.Values) wait.Cancel();
        base.Dispose();
    }

    private void Watch(string root, bool catchUp)
    {
        try
        {
            var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, EnableRaisingEvents = false, InternalBufferSize = 64 * 1024 };
            void Touched(string path) => Schedule(root, path);
            watcher.Changed += (_, e) => Touched(e.FullPath);
            watcher.Created += (_, e) => Touched(e.FullPath);
            watcher.Renamed += (_, e) => Touched(e.FullPath);
            watcher.Deleted += (_, e) => Touched(e.FullPath);
            watcher.Error += (_, e) => log.LogWarning(e.GetException(), "The dev folder {Folder} could not be watched for a moment", root);
            watcher.EnableRaisingEvents = true;
            _watchers[root] = watcher;

            // The build that made the folder has already written its files, and no change will come for them
            if (catchUp)
                foreach (var extension in Directory.GetDirectories(root))
                    Schedule(root, Path.Combine(extension, ExtensionPackage.ManifestFile));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "The dev folder {Folder} cannot be watched", root);
        }
    }

    /// <summary>One extension's folder changed. Start (or restart) its quiet period.</summary>
    private void Schedule(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var id = relative.Split(Path.DirectorySeparatorChar, 2)[0];
        // A file straight in the root is not an extension
        if (id.Length == 0 || id == "." || id == ".." || !relative.Contains(Path.DirectorySeparatorChar)) return;

        CancellationTokenSource wait;
        lock (_waiting)
        {
            if (_waiting.Remove(id, out var previous)) previous.Cancel();
            wait = _waiting[id] = new CancellationTokenSource();
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Settle, wait.Token);
                await ReloadAsync(Path.Combine(root, id));
            }
            catch (OperationCanceledException)
            {
                // Another write came in: that one starts the wait again
            }
        });
    }

    private async Task ReloadAsync(string folder)
    {
        if (!ui.DevLiveReload || !File.Exists(Path.Combine(folder, ExtensionPackage.ManifestFile))) return;

        await _one.WaitAsync();
        try
        {
            ImportResult result = new(false, "");
            // The build may still hold a file for a moment, so a failure is tried once more before it is reported
            for (var attempt = 0; attempt < 2; attempt++)
            {
                result = extensions.LoadDev(folder);
                if (result.Ok) break;
                await Task.Delay(1500);
            }

            var id = Path.GetFileName(folder);
            if (!result.Ok)
            {
                extensions.ReportDevFailure(id, folder, result.Message);
                return;
            }

            // Only a build that replaces a copy that ships with the app still needs a new start
            if (extensions.IsDevPending(id) && restarter.Available && !_restarting)
            {
                _restarting = true;
                log.LogInformation("{Extension} replaces the copy that ships with the app, so the app restarts to use the new build", id);
                restarter.Restart();
            }
        }
        finally
        {
            _one.Release();
        }
    }
}
