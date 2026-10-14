using System.Text.Json;
using Seneschal.Core;

namespace Seneschal.Shell;

/// <summary>UI preferences persisted to %AppData%/Seneschal/ui.json so they survive refreshes and restarts.</summary>
public sealed class UiSettings
{
    private static readonly string FilePath = AppPaths.File("ui.json");

    private sealed record State(bool SidebarExpanded = true, bool DarkMode = true, bool DevLiveReload = true, string[]? DevFolders = null);

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
