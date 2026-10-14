using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Seneschal.Core;

namespace Seneschal.Dev;

public enum BuildState
{
    NotBuilt,

    /// <summary>The build is newer than every source file.</summary>
    UpToDate,

    /// <summary>A source file changed after the last build.</summary>
    Changed,
}

/// <summary>An extension project under extensions/. <see cref="Shipped"/> ones are built into the app and cannot be deleted from here.</summary>
public sealed record WorkspaceExtension(
    string Name,
    string Folder,
    string Id,
    string Title,
    string Version,
    bool Shipped,
    string? Problem,
    BuildState Build,
    DateTime? BuiltAt,
    string? PackagePath);

public sealed record BuildResult(bool Ok, string Message);

/// <summary>What the Developer page works on: the extension projects in this repo's extensions/ folder.</summary>
public sealed partial class Workspace(DevMode dev, ShellLauncher shell, DllExtensions extensions)
{
    private static readonly SemaphoreSlim BuildLock = new(1, 1);
    private static readonly string[] SourceExtensions = [".cs", ".razor", ".csproj", ".json", ".css", ".props", ".targets", ".js"];

    public IReadOnlyList<WorkspaceExtension> List()
    {
        dev.Require();
        var shipped = ShippedFolders();
        return Directory.GetDirectories(dev.ExtensionsRoot!)
            .Select(f => (Folder: f, Project: Directory.GetFiles(f, "*.csproj").FirstOrDefault()))
            .Where(p => p.Project is not null)
            .OrderBy(p => p.Folder, StringComparer.OrdinalIgnoreCase)
            .Select(p => Describe(p.Folder, p.Project!, shipped))
            .ToList();
    }

    /// <summary>Builds the project (which stages the extension and packages it), then loads the result into the running app when that can be done live.</summary>
    public async Task<BuildResult> BuildAsync(string name, CancellationToken ct = default)
    {
        var (folder, project) = Resolve(name);
        ExtensionManifest manifest;
        try
        {
            manifest = ExtensionManifest.Parse(await File.ReadAllBytesAsync(Path.Combine(folder, ExtensionPackage.ManifestFile), ct));
        }
        catch (Exception ex) when (ex is ExtensionException or IOException)
        {
            return new BuildResult(false, $"extension.json: {DllExtensions.Describe(ex)}");
        }

        await BuildLock.WaitAsync(ct);
        try
        {
            var started = Stopwatch.StartNew();
            var psi = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in new[] { "build", project, "-nologo", "-v:q" })
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEndAsync(ct);
            var error = process.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return new BuildResult(false, "The build took too long and was stopped.");
            }

            if (process.ExitCode != 0)
                return new BuildResult(false, "Build failed:\n" + Errors(await output + await error));

            var staged = Path.Combine(dev.DevBuildsDir!, manifest.Id);
            if (!Directory.Exists(staged))
                return new BuildResult(false, $"The build finished but produced nothing in {staged}.");

            var loaded = extensions.LoadDev(staged);
            return new BuildResult(loaded.Ok, loaded.Ok
                ? $"Built in {started.Elapsed.TotalSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}s" + (loaded.Message.Length > 0 ? "\n" + loaded.Message : "")
                : $"Built but not loaded\n{loaded.Message}");
        }
        finally
        {
            BuildLock.Release();
        }
    }

    /// <summary>
    /// Moves the whole project out of the repo (to .deleted/extensions at the repo root, so it can be recovered), removes it from the
    /// solution and deletes its build output, and unloads it from the running app.
    /// </summary>
    public ScaffoldResult Delete(string name, bool includeBuiltIn = false)
    {
        var (folder, _) = Resolve(name);
        var info = Describe(folder, Directory.GetFiles(folder, "*.csproj").First(), ShippedFolders());
        if (info.Shipped && !includeBuiltIn)
            return new ScaffoldResult(false, $"'{info.Name}' is built into the app, so it needs the extra confirmation to be deleted.");

        var trash = Path.Combine(dev.RepoRoot!, ".deleted", "extensions", $"{info.Name}-{DateTime.Now:yyyyMMdd-HHmmss}");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(trash)!);
            Directory.Move(folder, trash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ScaffoldResult(false, $"Could not move the project (is it open in an editor or being built?): {ex.Message}");
        }

        // The project is gone; the rest is tidying up and must not fail the delete
        var notes = new List<string>();
        if (info.Problem is null) extensions.UnloadDev(info.Id);
        foreach (var leftover in new[] { Path.Combine(dev.DevBuildsDir!, info.Id), Path.Combine(dev.PackagesDir!, $"{info.Id}.snx") })
        {
            try
            {
                if (Directory.Exists(leftover)) Directory.Delete(leftover, recursive: true);
                else if (File.Exists(leftover)) File.Delete(leftover);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                notes.Add($"Could not delete {leftover}: {ex.Message}");
            }
        }
        try
        {
            SolutionFile.RemoveExtension(dev.SolutionFile!, info.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidDataException)
        {
            notes.Add($"Could not remove it from {DevMode.SolutionName}: {ex.Message}");
        }

        // A built-in extension is also part of the app build: its entry in the host project goes (or the next build would look for a
        // project that is not there) and so does the copy that was built next to the app (or it would keep loading as built-in)
        if (info.Shipped)
        {
            try
            {
                foreach (var id in RemoveBundled(dev.HostProject!, folder))
                {
                    var copy = Path.Combine(AppContext.BaseDirectory, "extensions", id);
                    if (Directory.Exists(copy)) Directory.Delete(copy, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                notes.Add($"Could not remove it from the app build: {ex.Message}");
            }
            notes.Add("Restart Seneschal to finish");
        }

        return new ScaffoldResult(true, $"Deleted {info.Name}", trash, notes.Count > 0 ? notes : null);
    }

    public void OpenFolder(string name) => shell.OpenInEditor(Resolve(name).Folder);

    public void OpenSolution()
    {
        dev.Require();
        shell.Open(dev.SolutionFile!);
    }

    public void RevealPackage(string name)
    {
        var info = List().FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (info?.PackagePath is { } path) shell.Reveal(path);
    }

    private WorkspaceExtension Describe(string folder, string project, HashSet<string> shipped)
    {
        var name = Path.GetFileName(folder);
        var shippedFlag = shipped.Contains(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar));
        try
        {
            var manifest = ExtensionManifest.Parse(File.ReadAllBytes(Path.Combine(folder, ExtensionPackage.ManifestFile)));
            var entry = Path.Combine(dev.DevBuildsDir!, manifest.Id, manifest.Entry ?? $"{name}.dll");
            DateTime? builtAt = File.Exists(entry) ? File.GetLastWriteTime(entry) : null;
            var state = builtAt is null ? BuildState.NotBuilt : NewestSource(folder) > builtAt ? BuildState.Changed : BuildState.UpToDate;
            var package = Path.Combine(dev.PackagesDir!, $"{manifest.Id}.snx");
            return new WorkspaceExtension(name, folder, manifest.Id, manifest.Name, manifest.Version, shippedFlag, null, state, builtAt, File.Exists(package) ? package : null);
        }
        catch (Exception ex) when (ex is ExtensionException or IOException)
        {
            return new WorkspaceExtension(name, folder, name.ToLowerInvariant(), name, "", shippedFlag, $"extension.json: {DllExtensions.Describe(ex)}", BuildState.NotBuilt, null, null);
        }
    }

    private static DateTime NewestSource(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(folder, f) && SourceExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Select(File.GetLastWriteTime)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();

    private static bool IsBuildOutput(string root, string file)
    {
        var first = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar)[0];
        return first.Equals("bin", StringComparison.OrdinalIgnoreCase) || first.Equals("obj", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Takes the extension out of the BundledExtension items of the host project (one line each) and returns the ids it had there.</summary>
    private static List<string> RemoveBundled(string hostProject, string extensionFolder)
    {
        var hostDir = Path.GetDirectoryName(hostProject)!;
        var wanted = Path.GetFullPath(extensionFolder).TrimEnd(Path.DirectorySeparatorChar);
        var ids = new List<string>();
        foreach (var item in XDocument.Load(hostProject).Descendants("BundledExtension"))
        {
            if ((string?)item.Attribute("SourceProject") is not { } source) continue;
            var folder = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(hostDir, source)))!.TrimEnd(Path.DirectorySeparatorChar);
            if (folder.Equals(wanted, StringComparison.OrdinalIgnoreCase) && (string?)item.Attribute("Include") is { } id) ids.Add(id);
        }

        // Edited as text, so the comments and the layout of the project file stay as they are
        var lines = File.ReadAllLines(hostProject).ToList();
        lines.RemoveAll(line => ids.Any(id => line.Contains($"<BundledExtension Include=\"{id}\"", StringComparison.Ordinal)));
        File.WriteAllLines(hostProject, lines);
        return ids;
    }

    /// <summary>The extension folders the app project builds in (the BundledExtension items of its project file).</summary>
    private HashSet<string> ShippedFolders()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var hostProject = dev.HostProject!;
            var hostDir = Path.GetDirectoryName(hostProject)!;
            foreach (var item in XDocument.Load(hostProject).Descendants("BundledExtension"))
            {
                if ((string?)item.Attribute("SourceProject") is { } source)
                    result.Add(Path.GetDirectoryName(Path.GetFullPath(Path.Combine(hostDir, source)))!.TrimEnd(Path.DirectorySeparatorChar));
            }
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
        {
            // Without the list nothing is protected from Delete by this check; the move itself still only touches extensions/<Name>
        }
        return result;
    }

    /// <summary>Only a direct child folder of extensions/ with a project in it is ever touched, whatever name the caller passes.</summary>
    private (string Folder, string Project) Resolve(string name)
    {
        dev.Require();
        var folder = Directory.GetDirectories(dev.ExtensionsRoot!)
            .FirstOrDefault(d => Path.GetFileName(d).Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No extension folder named '{name}'.");
        var project = Directory.GetFiles(folder, "*.csproj").FirstOrDefault()
            ?? throw new InvalidOperationException($"'{name}' has no project file.");
        return (folder, project);
    }

    /// <summary>The compiler errors only, with the repo path and the project suffix removed.</summary>
    private string Errors(string text)
    {
        var root = dev.RepoRoot! + Path.DirectorySeparatorChar;
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => ProjectSuffix().Replace(l, ""))
            .Select(l => l.Replace(root, ""))
            .ToList();
        var errors = lines.Where(l => l.Contains(": error ", StringComparison.OrdinalIgnoreCase) || l.Contains("error :", StringComparison.OrdinalIgnoreCase))
            .Distinct().Take(8).ToList();
        return string.Join('\n', errors.Count > 0 ? errors : lines.TakeLast(8));
    }

    [GeneratedRegex(@"\s*\[[^\]]*\.csproj\]$")]
    private static partial Regex ProjectSuffix();
}
