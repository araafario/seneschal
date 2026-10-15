using System.Text.Json;
using Seneschal.Core;
using Seneschal.Sdk;

namespace Seneschal.Shell;

/// <summary>
/// Resolves the icon of a tool group and of a tool. What the extension says (or, for a group, the built-in default for its name, then the
/// generic folder) is only the default: the user's choice wins, is kept in %AppData%/Seneschal/icons.json and can be reset to the default.
/// </summary>
public sealed class GroupIcons
{
    private static readonly string FilePath = AppPaths.File("icons.json");

    /// <summary>The file this was kept in before tools could have an icon of their own: only groups, as a flat map. Read once when there is no icons.json.</summary>
    private static readonly string LegacyPath = AppPaths.File("groups.json");

    private sealed record Saved(Dictionary<string, string>? Groups, Dictionary<string, string>? Tools, Dictionary<string, string>? Colors = null);

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _colors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The colors a group can have: the three of the logo, and a neutral one. They tint the icon tiles and the dot of the group on Home.</summary>
    public static readonly string[] ColorKeys = ["blue", "red", "gold", "neutral"];

    /// <summary>Raised after an icon was changed or reset so open pages can refresh.</summary>
    public event Action? Changed;

    public GroupIcons()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath));
                foreach (var (group, key) in saved?.Groups ?? []) _groups[group] = key;
                foreach (var (tool, key) in saved?.Tools ?? []) _tools[tool] = key;
                foreach (var (group, key) in saved?.Colors ?? []) _colors[group] = key;
            }
            else if (File.Exists(LegacyPath))
            {
                foreach (var (group, key) in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(LegacyPath)) ?? [])
                    _groups[group] = key;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken preferences file must never stop the app; fall back to the built-in icons.
        }
    }

    // Groups

    public string Key(string group)
    {
        lock (_gate)
        {
            if (_groups.TryGetValue(group, out var key) && GroupIconCatalog.All.ContainsKey(key))
                return GroupIconCatalog.Canonical(key);
        }
        return GroupIconCatalog.Defaults.GetValueOrDefault(group, GroupIconCatalog.Fallback);
    }

    public string Get(string group) => GroupIconCatalog.All[Key(group)];

    public bool IsCustom(string group)
    {
        lock (_gate) return _groups.ContainsKey(group);
    }

    public void Set(string group, string key) => Change(_groups, group, key);

    // Colors

    /// <summary>
    /// The color of a group: the one the user picked, or, when there is none, one of the three logo colors handed out in turn along the groups in
    /// alphabetical order, so groups next to each other never share a color and the same group keeps its color until the set of groups changes.
    /// </summary>
    public string Color(string group, IEnumerable<string> allGroups)
    {
        lock (_gate)
        {
            if (_colors.TryGetValue(group, out var key) && ColorKeys.Contains(key)) return key;
        }
        var ordered = allGroups.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var index = ordered.FindIndex(g => g.Equals(group, StringComparison.OrdinalIgnoreCase));
        return ColorKeys[Math.Max(index, 0) % 3];
    }

    public bool IsColorCustom(string group)
    {
        lock (_gate) return _colors.ContainsKey(group);
    }

    public void SetColor(string group, string key)
    {
        if (!ColorKeys.Contains(key)) return;
        bool changed;
        lock (_gate)
        {
            changed = !_colors.TryGetValue(group, out var current) || current != key;
            _colors[group] = key;
        }
        if (changed) SaveAndNotify();
    }

    public void ResetColor(string group)
    {
        bool changed;
        lock (_gate) changed = _colors.Remove(group);
        if (changed) SaveAndNotify();
    }

    public void Reset(string group) => Change(_groups, group, null);

    // Tools

    /// <summary>The icon to draw for the tool: the user's choice, or the one the extension gave it.</summary>
    public string Tool(ToolMetadata tool)
    {
        lock (_gate)
        {
            if (_tools.TryGetValue(tool.Id, out var key) && GroupIconCatalog.All.TryGetValue(key, out var icon))
                return icon;
        }
        return tool.Icon;
    }

    /// <summary>The catalog name of the tool's icon (for the picker to mark), or null when the extension's own icon is not one of the catalog.</summary>
    public string? ToolKey(ToolMetadata tool)
    {
        lock (_gate)
        {
            if (_tools.TryGetValue(tool.Id, out var key) && GroupIconCatalog.All.ContainsKey(key)) return GroupIconCatalog.Canonical(key);
        }
        return GroupIconCatalog.All.FirstOrDefault(kv => kv.Value == tool.Icon).Key;
    }

    public bool IsToolCustom(string toolId)
    {
        lock (_gate) return _tools.ContainsKey(toolId);
    }

    public void SetTool(string toolId, string key) => Change(_tools, toolId, key);

    public void ResetTool(string toolId) => Change(_tools, toolId, null);

    /// <summary>Sets the choice for one name, or clears it when <paramref name="key"/> is null. A name that is not in the catalog is ignored.</summary>
    private void Change(Dictionary<string, string> map, string name, string? key)
    {
        bool changed;
        lock (_gate)
        {
            if (key is null) changed = map.Remove(name);
            else if (!GroupIconCatalog.All.ContainsKey(key)) return;
            else
            {
                map[name] = GroupIconCatalog.Canonical(key);
                changed = true;
            }
        }
        if (changed) SaveAndNotify();
    }

    /// <summary>Back to the built-in icons: every choice is cleared and both saved files are removed.</summary>
    public void ResetToDefaults()
    {
        lock (_gate)
        {
            _groups.Clear();
            _tools.Clear();
            _colors.Clear();
        }
        try
        {
            File.Delete(FilePath);
            File.Delete(LegacyPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Changed?.Invoke();
    }

    private void SaveAndNotify()
    {
        try
        {
            Saved snapshot;
            lock (_gate) snapshot = new(new(_groups), new(_tools), new(_colors));
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cosmetic preference; the choice still applies until the app restarts.
        }
        Changed?.Invoke();
    }
}
