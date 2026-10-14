using System.Text.Json;
using Seneschal.Core;

namespace Seneschal.Shell;

/// <summary>
/// Resolves the icon of a tool group: the user's choice (persisted to %AppData%/Seneschal/groups.json) wins,
/// then the built-in default for that group name, then the generic folder.
/// </summary>
public sealed class GroupIcons
{
    private static readonly string FilePath = AppPaths.File("groups.json");

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _overrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised after an icon was changed or reset so open pages can refresh.</summary>
    public event Action? Changed;

    public GroupIcons()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath));
                foreach (var (group, key) in saved ?? [])
                    _overrides[group] = key;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken preferences file must never stop the app; fall back to the built-in icons.
        }
    }

    public string Key(string group)
    {
        lock (_gate)
        {
            if (_overrides.TryGetValue(group, out var key) && GroupIconCatalog.All.ContainsKey(key))
                return GroupIconCatalog.Canonical(key);
        }
        return GroupIconCatalog.Defaults.GetValueOrDefault(group, GroupIconCatalog.Fallback);
    }

    public string Get(string group) => GroupIconCatalog.All[Key(group)];

    public bool IsCustom(string group)
    {
        lock (_gate) return _overrides.ContainsKey(group);
    }

    public void Set(string group, string key)
    {
        if (!GroupIconCatalog.All.ContainsKey(key)) return;
        lock (_gate) _overrides[group] = GroupIconCatalog.Canonical(key);
        SaveAndNotify();
    }

    public void Reset(string group)
    {
        bool removed;
        lock (_gate) removed = _overrides.Remove(group);
        if (removed) SaveAndNotify();
    }

    private void SaveAndNotify()
    {
        try
        {
            Dictionary<string, string> snapshot;
            lock (_gate) snapshot = new(_overrides);
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
