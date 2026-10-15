using System.Text.Json;
using Seneschal.Core;

namespace Seneschal.Shell;

/// <summary>The tools the user starred, in the order they were added. Persisted to %AppData%/Seneschal/favorites.json.</summary>
public sealed class Favorites
{
    private static readonly string FilePath = AppPaths.File("favorites.json");

    private readonly object _gate = new();
    private readonly List<string> _ids = [];

    /// <summary>Raised after a tool was starred or unstarred so open pages and the sidebar can refresh.</summary>
    public event Action? Changed;

    public Favorites()
    {
        try
        {
            if (File.Exists(FilePath))
                _ids.AddRange(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken preferences file must never stop the app; start with no favorites.
        }
    }

    public IReadOnlyList<string> Ids
    {
        get { lock (_gate) return [.. _ids]; }
    }

    public bool IsFavorite(string id)
    {
        lock (_gate) return _ids.Contains(id, StringComparer.OrdinalIgnoreCase);
    }

    public void Toggle(string id)
    {
        lock (_gate)
        {
            if (_ids.RemoveAll(i => i.Equals(id, StringComparison.OrdinalIgnoreCase)) == 0)
                _ids.Add(id);
        }
        SaveAndNotify();
    }

    /// <summary>Unstars everything and removes the saved file.</summary>
    public void ResetToDefaults()
    {
        lock (_gate) _ids.Clear();
        try { File.Delete(FilePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Changed?.Invoke();
    }

    private void SaveAndNotify()
    {
        try
        {
            List<string> snapshot;
            lock (_gate) snapshot = [.. _ids];
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(snapshot));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cosmetic preference; the choice still applies until the app restarts.
        }
        Changed?.Invoke();
    }
}
