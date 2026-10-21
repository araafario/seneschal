using Seneschal.Shell;

namespace Seneschal.Dev;

/// <summary>
/// The folder that holds the extension projects, and what is in it. Every project in it is a project of Seneschal with nothing to add: it is listed in
/// Options, Extension Projects and its builds are watched, so a build loads by itself. Projects from anywhere else are added by hand (Add Project).
/// </summary>
public static class ProjectsFolder
{
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Seneschal", "Projects");

    /// <summary>The environment variable, then the folder set in Options, then Documents/Seneschal/Projects.</summary>
    public static string Root(UiSettings ui) =>
        Environment.GetEnvironmentVariable("SENESCHAL_EXTENSIONS_DIR") is { Length: > 0 } custom ? custom
        : ui.ProjectsFolder is { Length: > 0 } chosen ? chosen
        : DefaultRoot;

    /// <summary>The same folder as <see cref="Root"/>, before the settings exist: read from the saved file. Used while the app is starting.</summary>
    public static string RootFromSaved() =>
        Environment.GetEnvironmentVariable("SENESCHAL_EXTENSIONS_DIR") is { Length: > 0 } custom ? custom
        : UiSettings.SavedProjectsFolder() is { Length: > 0 } chosen ? chosen
        : DefaultRoot;

    /// <summary>The project folders directly inside <paramref name="root"/>: the ones with a project file.</summary>
    public static IReadOnlyList<string> Projects(string root)
    {
        try
        {
            return !Directory.Exists(root)
                ? []
                : Directory.GetDirectories(root).Where(d => Directory.GetFiles(d, "*.csproj").Length > 0).Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Where each project's build stages its extension. The folder may not exist yet: the first build makes it.</summary>
    public static IReadOnlyList<string> BuildFolders(string root) =>
        [.. Projects(root).Select(p => Path.Combine(p, "artifacts", "extensions"))];
}
