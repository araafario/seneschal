using System.Reflection;
using MudBlazor;

namespace Seneschal.Shell;

/// <summary>
/// Icons available for tool groups and new tools: every Material icon MudBlazor ships (Outlined style), by its MudBlazor name,
/// plus the built-in default per group name. Groups only exist as a text field on each tool, so their icon is a display decision
/// made here (not in tool folders). Unknown groups, including "Other" and groups that arrive from DLLs, fall back to
/// <see cref="Fallback"/> until the user picks one.
/// </summary>
public static class GroupIconCatalog
{
    public const string Fallback = "Folder";

    /// <summary>Keys saved by earlier versions, when only a short list of icons was offered.</summary>
    private static readonly Dictionary<string, string> LegacyKeys = new()
    {
        ["Transform"] = "SwapHoriz",
        ["Format"] = "FormatAlignLeft",
        ["Text"] = "TextFields",
        ["Calendar"] = "CalendarMonth",
        ["Rocket"] = "RocketLaunch",
        ["More"] = "MoreHoriz",
    };

    private static readonly Dictionary<string, string> ByName = typeof(Icons.Material.Outlined)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

    /// <summary>The icon names to offer in a picker, alphabetical.</summary>
    public static IReadOnlyList<string> Names { get; } = ByName.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Icon key (stable, saved to disk) to MudBlazor SVG. Keys from earlier versions still resolve.</summary>
    public static IReadOnlyDictionary<string, string> All { get; } = ByName
        .Concat(LegacyKeys.Where(l => ByName.ContainsKey(l.Value) && !ByName.ContainsKey(l.Key)).Select(l => new KeyValuePair<string, string>(l.Key, ByName[l.Value])))
        .ToDictionary(p => p.Key, p => p.Value);

    /// <summary>The current name for a key; an old short key such as "Transform" becomes "SwapHoriz".</summary>
    public static string Canonical(string key) => ByName.ContainsKey(key) ? key : LegacyKeys.GetValueOrDefault(key, key);

    /// <summary>Built-in icon key per group name. To give a new group an icon, add one line here.</summary>
    public static IReadOnlyDictionary<string, string> Defaults { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Encoders / Decoders"] = "Code",
        ["Converters"] = "SwapHoriz",
        ["Formatters"] = "FormatAlignLeft",
        ["Generators"] = "Bolt",
        ["Text"] = "TextFields",
        ["Graphic"] = "Image",
        ["Testers"] = "Rule",
        ["Productivity"] = "RocketLaunch",
        ["Other"] = "MoreHoriz",
    };
}
