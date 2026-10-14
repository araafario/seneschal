using System.Diagnostics;
using Seneschal.Core;

namespace Seneschal.Dev;

/// <summary>
/// What the assistant needs to make an extension without an editor: create the project, read and write its files, build it and load it.
/// Run from source it works in this repository's <c>extensions/</c> folder, where a build loads live. An installed Seneschal works in
/// a folder of the user's own (<c>SENESCHAL_EXTENSIONS_DIR</c>, by default <c>source/seneschal-extensions</c> in the home folder). A project made
/// there is added to the folders Seneschal watches, so every build of it, from here or from a terminal, is loaded as a dev build like any other
/// project. Every file operation is confined to one project folder.
/// </summary>
public sealed class Authoring(DevMode dev, Scaffolder scaffolder, Workspace workspace, DotnetSdk sdk, DllExtensions extensions, DevWatcher watcher)
{
    private const int MaxFileBytes = 256 * 1024;
    private static readonly SemaphoreSlim BuildLock = new(1, 1);
    private static readonly string[] TextTypes = [".cs", ".razor", ".css", ".json", ".md", ".csproj", ".js", ".props", ".targets", ".txt", ".html", ".svg", ".config"];
    private static readonly string[] Hidden = ["bin", "obj", "artifacts", ".vs", ".git", ".deleted"];

    public bool InRepo => dev.Enabled;

    public bool Available => sdk.Available;

    /// <summary>The folder that holds the extension projects.</summary>
    public string Root => dev.Enabled
        ? dev.ExtensionsRoot!
        : Environment.GetEnvironmentVariable("SENESCHAL_EXTENSIONS_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "seneschal-extensions");

    public ScaffoldResult Create(NewExtension request)
    {
        var result = InRepo ? scaffolder.Create(request) : Scaffolder.CreateStandalone(Root, request);
        // A standalone project builds into its own artifacts/extensions: watching it is what makes every build load
        if (result.Ok && !InRepo && result.Folder is not null) watcher.AddFolder(result.Folder);
        return result;
    }

    public IReadOnlyList<string> Projects() =>
        !Directory.Exists(Root)
            ? []
            : Directory.GetDirectories(Root).Where(d => Directory.GetFiles(d, "*.csproj").Length > 0).Select(d => Path.GetFileName(d)!).Order(StringComparer.OrdinalIgnoreCase).ToList();

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
        if (InRepo) return await workspace.BuildAsync(name, cancel);

        watcher.AddFolder(folder);
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

    private string Folder(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', ':']) >= 0 || name.Contains(".."))
            throw new InvalidOperationException("Give the project name only, such as JsonDiff");
        var root = Path.GetFullPath(Root);
        var folder = Path.GetFullPath(Path.Combine(root, name.Trim()));
        if (!Directory.Exists(folder) || Directory.GetFiles(folder, "*.csproj").Length == 0)
            throw new InvalidOperationException($"There is no extension project called {name} in {root}");
        return folder;
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
