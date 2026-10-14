using System.Text.Json;
using Seneschal.Core;

namespace Seneschal.Shell;

/// <summary>UI preferences persisted to %AppData%/Seneschal/ui.json so they survive refreshes and restarts.</summary>
public sealed class UiSettings
{
    private static readonly string FilePath = AppPaths.File("ui.json");

    private sealed record State(bool SidebarExpanded = true, bool DarkMode = true);

    private State _state = new();

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
    }
}
