using System.Text.Json;

namespace Seneschal.Core;

/// <summary>
/// Which extensions the user switched off, persisted to %AppData%/Seneschal/extensions.json.
/// A disabled extension is hidden from the sidebar, grid and search; everything is enabled by default.
/// </summary>
public sealed class ExtensionSettings
{
    private static readonly string FilePath = AppPaths.File("extensions.json");

    private readonly object _gate = new();
    private readonly HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised after an extension was switched on or off so open pages can refresh.</summary>
    public event Action? Changed;

    public ExtensionSettings()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var saved = JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath));
                foreach (var id in saved?.Disabled ?? [])
                    _disabled.Add(id);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken file must never stop the app; everything simply starts enabled.
        }
    }

    public bool IsEnabled(string id)
    {
        lock (_gate) return !_disabled.Contains(id);
    }

    public void SetEnabled(string id, bool enabled)
    {
        bool changed;
        lock (_gate) changed = enabled ? _disabled.Remove(id) : _disabled.Add(id);
        if (!changed) return;

        try
        {
            string[] snapshot;
            lock (_gate) snapshot = [.. _disabled];
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new State(snapshot)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The switch still applies until the app restarts.
        }
        Changed?.Invoke();
    }

    /// <summary>Switches every extension back on and removes the saved file.</summary>
    public void ResetToDefaults()
    {
        lock (_gate) _disabled.Clear();
        try { File.Delete(FilePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Changed?.Invoke();
    }

    private sealed record State(string[] Disabled);
}
