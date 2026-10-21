using System.Diagnostics;
using Seneschal.Core;

namespace Seneschal.Dev;

/// <summary>An extension project that can be worked on, and what it builds.</summary>
public sealed record ProjectInfo(string Name, string Folder, string Source, IReadOnlyList<ProjectBuild> Builds);

/// <summary>An extension a project builds: its id, its title, and the ids of the tools it adds.</summary>
public sealed record ProjectBuild(string Id, string Title, IReadOnlyList<string> Tools);
/// <summary>
/// What the assistant needs to make an extension without an editor: create the project, read and write its files, build it and load it.
/// Projects live in a folder of the user's own, the same from source and installed: Options (Projects folder), or <c>SENESCHAL_EXTENSIONS_DIR</c>,
/// or <c>Documents/Seneschal/Projects</c>. Never in the Seneschal repository. A project made there is added to the folders Seneschal watches, so every
/// build of it, from here or from a terminal, is loaded as a dev build like any other project. Every file operation is confined to one project folder.
/// </summary>
public sealed class Authoring(DotnetSdk sdk, DllExtensions extensions, DevWatcher watcher, Seneschal.Shell.UiSettings settings)
{
    private const int MaxFileBytes = 256 * 1024;
    private static readonly SemaphoreSlim BuildLock = new(1, 1);
    private static readonly string[] TextTypes = [".cs", ".razor", ".css", ".json", ".md", ".csproj", ".js", ".props", ".targets", ".txt", ".html", ".svg", ".config"];
    private static readonly string[] Hidden = ["bin", "obj", "artifacts", ".vs", ".git", ".deleted"];

    public bool Available => sdk.Available;

    /// <summary>The folder that holds the extension projects: the environment variable, then the one set in Options, then Documents/Seneschal/Projects.</summary>
    public string Root => ProjectsFolder.Root(settings);

    public static string DefaultRoot => ProjectsFolder.DefaultRoot;

    public ScaffoldResult Create(NewExtension request)
    {
        // A project of that name that already exists (here or one the user added) is worked in, not made again
        if (ProjectList().FirstOrDefault(p => p.Name.Equals(request.Name.Trim(), StringComparison.OrdinalIgnoreCase)) is { } existing)
            return new ScaffoldResult(false, $"A project called {existing.Name} already exists ({existing.Folder}). Work in it, or choose another name", null);
        // The new project is inside the projects folder, whose builds are all watched, so every build of it loads by itself
        return Scaffolder.CreateLocal(Root, request);
    }

    public IReadOnlyList<string> Projects() => [.. ProjectList().Select(p => p.Name)];

    /// <summary>
    /// Deletes a project of the projects folder: unloads what it built, then moves it to .deleted in the projects folder (not erased, so it can be moved back).
    /// A project the user added by hand lives somewhere of their own and is never touched here: it is only stopped being watched (Remove Project).
    /// </summary>
    public ScaffoldResult DeleteProject(string name)
    {
        var folder = ProjectsFolder.Projects(Root).FirstOrDefault(d => Path.GetFileName(d).Equals((name ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        if (folder is null)
            return new ScaffoldResult(false, $"There is no project called {name} in the projects folder ({Root}). A project that was added by hand is removed with Remove Project, which only stops watching it");

        // The watcher holds the project's build folder open, and a folder that holds one cannot be moved
        watcher.Release(folder);
        var prefix = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var built in extensions.Loaded.Where(e => Path.GetFullPath(e.Folder).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            extensions.UnloadDev(built.Name);

        var trash = Path.Combine(Path.GetFullPath(Root), ".deleted", $"{Path.GetFileName(folder)}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(Path.GetDirectoryName(trash)!);
        // What was loaded from the project keeps its files open until the unloaded code is collected, so the first tries may need a moment
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Move(folder, trash);
                return new ScaffoldResult(true, $"Moved {Path.GetFileName(folder)} to {trash}", trash);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= 5) return new ScaffoldResult(false, $"Could not move the project (is it open in an editor or being built?): {ex.Message}");
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(300);
            }
        }
    }

    /// <summary>
    /// Every extension project that can be worked on: the ones in the projects folder, and the ones the user added in Options, Extension Projects (a project
    /// anywhere on the machine whose build folder is watched). Each says what it builds, so a request such as "add a tool to LoopUp" can be matched to it.
    /// </summary>
    public IReadOnlyList<ProjectInfo> ProjectList()
    {
        var list = new List<ProjectInfo>();
        foreach (var folder in ProjectsFolder.Projects(Root)) list.Add(Describe(folder, "Projects folder"));
        foreach (var folder in AddedProjectFolders())
            if (!list.Any(p => p.Name.Equals(Path.GetFileName(folder), StringComparison.OrdinalIgnoreCase))) list.Add(Describe(folder, "Added in Options, Extension Projects"));
        return list;
    }

    /// <summary>The folders of the projects added by hand: the folder above <c>artifacts/extensions</c> of a watched build folder, when it holds a project file.</summary>
    private IEnumerable<string> AddedProjectFolders()
    {
        foreach (var watched in settings.DevFolders)
        {
            string? project;
            try
            {
                var full = Path.GetFullPath(watched).TrimEnd(Path.DirectorySeparatorChar);
                project = Path.GetFileName(full).Equals("extensions", StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(Path.GetDirectoryName(full) ?? "").Equals("artifacts", StringComparison.OrdinalIgnoreCase)
                        ? Path.GetDirectoryName(Path.GetDirectoryName(full))
                        : null;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }
            if (project is not null && Directory.Exists(project) && Directory.GetFiles(project, "*.csproj").Length > 0) yield return project;
        }
    }

    private ProjectInfo Describe(string folder, string source)
    {
        var builds = extensions.Loaded
            .Where(e => Path.GetFullPath(e.Folder).StartsWith(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Select(e => new ProjectBuild(e.Name, e.Title, e.ToolIds)).ToList();
        return new ProjectInfo(Path.GetFileName(folder), folder, source, builds);
    }

    public IReadOnlyList<string> Files(string name)
    {
        var folder = Folder(name);
        var result = new List<string>();
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
            if (relative.Split('/').Any(IsHidden)) continue;
            result.Add(relative);
            if (result.Count >= 500) break;
        }
        return result.Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>Checks the project against the design rules and the SDK, without changing anything.</summary>
    public CheckResult Check(string name) => ExtensionCheck.Run(Folder(name));

    public string Read(string name, string file)
    {
        var path = FilePath(Folder(name), file);
        if (!File.Exists(path)) throw new InvalidOperationException($"There is no file {file} in {name}");
        if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidOperationException($"{file} is larger than {MaxFileBytes / 1024} KB");
        return File.ReadAllText(path);
    }

    public string Write(string name, string file, string content)
    {
        var path = FilePath(Folder(name), file);
        if (System.Text.Encoding.UTF8.GetByteCount(content) > MaxFileBytes) throw new InvalidOperationException($"A file can be at most {MaxFileBytes / 1024} KB");
        var existed = File.Exists(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
        return $"{(existed ? "Replaced" : "Created")} {file}";
    }

    /// <summary>Builds the project and loads the result as a dev build, live, whether it is in the repository or in the user's own folder.</summary>
    public async Task<BuildResult> BuildAsync(string name, CancellationToken cancel)
    {
        var folder = Folder(name);

        var built = await BuildFolderAsync(folder, cancel);
        if (!built.Ok) return built;

        // The watcher would load it in a moment, but the caller wants the answer now
        var manifestPath = Path.Combine(folder, ExtensionPackage.ManifestFile);
        if (!File.Exists(manifestPath)) return new BuildResult(false, "The project has no extension.json");
        var id = ExtensionManifest.Parse(await File.ReadAllBytesAsync(manifestPath, cancel)).Id;
        var staged = Path.Combine(folder, "artifacts", "extensions", id);
        if (!Directory.Exists(staged)) return new BuildResult(false, "The build finished but made no extension. Does the project use the Seneschal.Sdk package?");

        var loaded = extensions.LoadDev(staged);
        return new BuildResult(loaded.Ok, loaded.Ok ? $"{built.Message}\n{loaded.Message}".Trim() : $"Built but not loaded\n{loaded.Message}");
    }

    /// <summary>
    /// Runs <c>dotnet build</c> on a project folder (the one with the .csproj) and returns the compiler errors when it fails. It loads nothing:
    /// the build stages the extension in the project's artifacts/extensions, which Seneschal loads when that folder is watched.
    /// </summary>
    public async Task<BuildResult> BuildFolderAsync(string projectFolder, CancellationToken cancel)
    {
        var info = sdk.Current ?? throw new InvalidOperationException("The .NET SDK is not installed. Run seneschal dev install");
        var project = Directory.GetFiles(projectFolder, "*.csproj").FirstOrDefault()
            ?? throw new InvalidOperationException($"There is no project (.csproj) in {projectFolder}");

        await BuildLock.WaitAsync(cancel);
        try
        {
            // The SDK copy and the build rules next to the projects follow the app, so an update reaches a project at its next build
            if (IsUnder(Root, projectFolder)) LocalSdk.Ensure(Root);

            var started = Stopwatch.StartNew();
            var psi = new ProcessStartInfo(info.Dotnet)
            {
                WorkingDirectory = projectFolder,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in new[] { "build", project, "-nologo", "-v:q" }) psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEndAsync(cancel);
            var error = process.StandardError.ReadToEndAsync(cancel);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return new BuildResult(false, "The build took too long and was stopped");
            }

            var seconds = started.Elapsed.TotalSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            return process.ExitCode != 0
                ? new BuildResult(false, "Build failed\n" + Errors(await output + await error))
                : new BuildResult(true, $"Built in {seconds}s");
        }
        finally
        {
            BuildLock.Release();
        }
    }

    private static bool IsUnder(string root, string folder) =>
        Path.GetFullPath(folder).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private string Folder(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', ':']) >= 0 || name.Contains(".."))
            throw new InvalidOperationException("Give the project name only, such as JsonDiff");
        var root = Path.GetFullPath(Root);
        var folder = Path.GetFullPath(Path.Combine(root, name.Trim()));
        if (Directory.Exists(folder) && Directory.GetFiles(folder, "*.csproj").Length > 0) return folder;
        // A project the user added by hand lives wherever it was added from. Only that project's own folder is reached, as for the others
        if (AddedProjectFolders().FirstOrDefault(d => Path.GetFileName(d).Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)) is { } added) return Path.GetFullPath(added);
        throw new InvalidOperationException($"There is no extension project called {name}. The projects are: {string.Join(", ", Projects())}");
    }

    /// <summary>A file inside the project folder, and a kind of file an extension is made of. Nothing else is ever reached.</summary>
    private static string FilePath(string folder, string file)
    {
        if (string.IsNullOrWhiteSpace(file)) throw new InvalidOperationException("Give the path of the file inside the project");
        var full = Path.GetFullPath(Path.Combine(folder, file.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The file must be inside the project folder");
        if (Path.GetRelativePath(folder, full).Split(Path.DirectorySeparatorChar).Any(IsHidden))
            throw new InvalidOperationException("That folder is build output and is not part of the project");
        if (!TextTypes.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Only text files of an extension can be used here ({string.Join(" ", TextTypes)})");
        return full;
    }

    private static bool IsHidden(string segment) => Hidden.Contains(segment, StringComparer.OrdinalIgnoreCase);

    /// <summary>Only the lines that say what is wrong, each once, so the answer is short enough to act on.</summary>
    private static string Errors(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Contains(" error ", StringComparison.OrdinalIgnoreCase) || l.Contains("error NU", StringComparison.OrdinalIgnoreCase) || l.Contains("warning NU", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .Take(30)
            .ToList();
        return lines.Count > 0 ? string.Join('\n', lines) : output.Trim();
    }
}
