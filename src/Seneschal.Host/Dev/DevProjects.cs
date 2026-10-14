using Seneschal.Core;

namespace Seneschal.Dev;

/// <summary>
/// One extension project as the Projects section of Options shows it: where it is, where its builds go, and what has been loaded from it.
/// <see cref="Source"/> says where Seneschal learned of it: this repository, a folder added in Options, or the environment variable.
/// </summary>
public sealed record DevProject(
    string Key,
    string Name,
    string Source,
    string? ProjectFolder,
    string BuildFolder,
    bool BuildFolderExists,
    bool Removable,
    WorkspaceExtension? Repo,
    IReadOnlyList<DllExtension> Loaded);

/// <summary>The projects being developed: the extensions of this repository when running from source, and every folder Seneschal watches for builds.</summary>
public sealed class DevProjects(DevMode dev, Workspace work, DevWatcher watcher, DllExtensions extensions, Authoring authoring, ShellLauncher shell)
{
    public IReadOnlyList<DevProject> List()
    {
        var loaded = extensions.Loaded;
        var result = new List<DevProject>();

        if (dev.Enabled)
        {
            foreach (var w in work.List())
            {
                var staged = Path.Combine(dev.DevBuildsDir!, w.Id);
                result.Add(new DevProject($"repo:{w.Name}", w.Name, "This repository", w.Folder, staged, Directory.Exists(staged), false, w,
                    loaded.Where(e => e.Name.Equals(w.Id, StringComparison.OrdinalIgnoreCase)).ToList()));
            }
        }

        foreach (var entry in watcher.Entries)
        {
            // This repository's own folder is already shown as its projects
            if (dev.Enabled && entry.Source == "This repository") continue;

            var project = ProjectOf(entry.Path);
            var inside = loaded.Where(e => e.Origin == ExtensionSource.Dev && IsInside(entry.Path, e.Folder)).ToList();
            result.Add(new DevProject($"folder:{entry.Path}", project is not null ? Path.GetFileName(project)! : Path.GetFileName(entry.Path.TrimEnd(Path.DirectorySeparatorChar)),
                entry.Source, project, entry.Path, entry.Exists, entry.Removable, null, inside));
        }
        return result;
    }

    /// <summary>The project folder behind a build folder (.../Project/artifacts/extensions), when it has a project file.</summary>
    private static string? ProjectOf(string buildFolder)
    {
        var full = Path.GetFullPath(buildFolder).TrimEnd(Path.DirectorySeparatorChar);
        if (!Path.GetFileName(full).Equals("extensions", StringComparison.OrdinalIgnoreCase)) return null;
        var artifacts = Path.GetDirectoryName(full);
        if (artifacts is null || !Path.GetFileName(artifacts).Equals("artifacts", StringComparison.OrdinalIgnoreCase)) return null;
        var project = Path.GetDirectoryName(artifacts);
        return project is not null && Directory.Exists(project) && Directory.GetFiles(project, "*.csproj").Length > 0 ? project : null;
    }

    private static bool IsInside(string root, string path) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public bool CanBuild(DevProject project) => project.Repo is not null || project.ProjectFolder is not null;

    public Task<BuildResult> BuildAsync(DevProject project, CancellationToken cancel) =>
        project.Repo is not null
            ? work.BuildAsync(project.Repo.Name, cancel)
            : authoring.BuildFolderAsync(project.ProjectFolder ?? throw new InvalidOperationException("This folder has no project to build"), cancel);

    public void OpenFolder(DevProject project) => shell.OpenInEditor(project.ProjectFolder ?? project.BuildFolder);

    public void Reveal(DevProject project) => shell.Reveal(project.ProjectFolder ?? project.BuildFolder);

    /// <summary>Opens the solution that holds the project in Visual Studio (or whatever .sln and .slnx open with); without one, the project file.</summary>
    public void OpenSolution(DevProject project)
    {
        if (project.Repo is not null)
        {
            work.OpenSolution();
            return;
        }
        var folder = project.ProjectFolder ?? throw new InvalidOperationException("This folder has no project to open");
        for (var dir = new DirectoryInfo(folder); dir is not null && dir.FullName.Length > 3; dir = dir.Parent)
        {
            var solution = dir.GetFiles("*.slnx").Concat(dir.GetFiles("*.sln")).FirstOrDefault();
            if (solution is not null)
            {
                shell.Open(solution.FullName);
                return;
            }
            if (dir.FullName.Equals(Path.GetPathRoot(dir.FullName), StringComparison.OrdinalIgnoreCase)) break;
        }
        shell.Open(Directory.GetFiles(folder, "*.csproj").First());
    }

    /// <summary>Stops watching a folder that was added in Options. What it loaded stays until its build is removed or the app restarts.</summary>
    public void StopWatching(DevProject project)
    {
        if (!project.Removable) throw new InvalidOperationException("This folder is not set in Options, so it is not removed here");
        watcher.RemoveFolder(project.BuildFolder);
    }
}
