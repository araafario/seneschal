using System.Text.Json;
using Seneschal.Core;

namespace Seneschal.Shell;

/// <summary>UI preferences persisted to %AppData%/Seneschal/ui.json so they survive refreshes and restarts.</summary>
public sealed class UiSettings
{
    private static readonly string FilePath = AppPaths.File("ui.json");

    private sealed record State(bool SidebarExpanded = true, bool DarkMode = true, bool DevLiveReload = true, string[]? DevFolders = null, string? ProjectsFolder = null, string? FontFamily = null);

    private State _state = new();

    /// <summary>Raised when a setting changes, for what shows it (the Live Reload card) and for what acts on it (the dev folder watcher).</summary>
    public event Action? Changed;

    public UiSettings()
    {
        try
        {
            if (File.Exists(FilePath))
                _state = JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath)) ?? new State();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken preferences file must never stop the app; fall back to defaults.
        }
    }

    public bool SidebarExpanded
    {
        get => _state.SidebarExpanded;
        set => Update(_state with { SidebarExpanded = value });
    }

    public bool DarkMode
    {
        get => _state.DarkMode;
        set => Update(_state with { DarkMode = value });
    }

    /// <summary>Whether a build that lands in a dev folder is loaded by itself (whatever it registers). On unless the developer turns it off.</summary>
    public bool DevLiveReload
    {
        get => _state.DevLiveReload;
        set => Update(_state with { DevLiveReload = value });
    }

    /// <summary>The fonts the app falls back to, and uses when none is chosen.</summary>
    public static readonly string[] DefaultFontStack = ["Segoe UI Variable", "Segoe UI", "system-ui", "sans-serif"];

    /// <summary>The font the person chose for the whole app (a font installed on this computer), or null for the default.</summary>
    public string? FontFamily
    {
        get => _state.FontFamily;
        set => Update(_state with { FontFamily = Clean(value) });
    }

    /// <summary>The chosen font first, then the default ones, so a font that is not installed still shows text.</summary>
    public string[] FontStack => FontFamily is { } font ? [font, .. DefaultFontStack] : DefaultFontStack;

    /// <summary>A font name and nothing that could break out of a CSS value.</summary>
    private static string? Clean(string? name)
    {
        const string Forbidden = "\"'\\;{}<>()";
        var clean = new string((name ?? "").Where(c => !Forbidden.Contains(c) && !char.IsControl(c)).ToArray()).Trim();
        return clean.Length is > 0 and <= 60 ? clean : null;
    }

    /// <summary>Where the extension projects are made. Empty means the default, Documents/Seneschal/Projects.</summary>
    public string? ProjectsFolder
    {
        get => _state.ProjectsFolder;
        set => Update(_state with { ProjectsFolder = string.IsNullOrWhiteSpace(value) ? null : value.Trim() });
    }

    /// <summary>Folders that hold dev builds, added in Options (the environment variable and the repository's own are not in here).</summary>
    public IReadOnlyList<string> DevFolders => _state.DevFolders ?? [];

    public void AddDevFolder(string folder)
    {
        if (DevFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) return;
        Update(_state with { DevFolders = [.. DevFolders, folder] });
    }

    public void RemoveDevFolder(string folder)
    {
        if (!DevFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) return;
        Update(_state with { DevFolders = [.. DevFolders.Where(f => !f.Equals(folder, StringComparison.OrdinalIgnoreCase))] });
    }

    /// <summary>The same list, read from the file before the app is built: the extensions of those folders are loaded at startup.</summary>
    internal static IReadOnlyList<string> SavedDevFolders()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath))?.DevFolders ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Nothing saved is the same as nothing added
        }
        return [];
    }

    /// <summary>Back to the defaults: the saved file is removed and everything that shows or acts on a setting is told.</summary>
    public void ResetToDefaults()
    {
        try { File.Delete(FilePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        _state = new();
        Changed?.Invoke();
    }

    /// <summary>The projects folder as saved, read from the file before the app is built (the builds in it are loaded at startup).</summary>
    internal static string? SavedProjectsFolder()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath))?.ProjectsFolder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Nothing saved means the default
        }
        return null;
    }

    private void Update(State next)
    {
        if (next == _state) return;
        _state = next;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_state));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are cosmetic; losing one is not worth interrupting the user.
        }
        Changed?.Invoke();
    }
}
