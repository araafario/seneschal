using Seneschal.Sdk;

namespace Seneschal.Core;

/// <summary>Where a tool comes from. Only <see cref="Installed"/> extensions can be removed or replaced by the user.</summary>
public enum ExtensionSource
{
    /// <summary>Compiled into the app itself (the legacy Tools/ folder).</summary>
    Host,

    /// <summary>An extension that ships with the app, in its extensions folder.</summary>
    Bundled,

    /// <summary>A dev build, loaded from a folder named by SENESCHAL_DEV_EXTENSIONS.</summary>
    Dev,

    /// <summary>Imported by the user into %AppData%/Seneschal/extensions.</summary>
    Installed,
}

/// <summary>A tool plus where it came from. <see cref="Package"/> is the id of the extension that provides it (null for tools compiled into the app).</summary>
public sealed record ExtensionInfo(ToolMetadata Tool, ExtensionSource Source, string? Package = null, string? Version = null);

public interface IToolRegistry
{
    /// <summary>Enabled tools only: what the sidebar, grid and search show.</summary>
    IReadOnlyList<ToolMetadata> All { get; }
    IReadOnlyList<IGrouping<string, ToolMetadata>> Groups { get; }

    /// <summary>Every installed tool including the ones the user switched off (for Options, Extensions).</summary>
    IReadOnlyList<ToolMetadata> Installed { get; }
    IReadOnlyList<IGrouping<string, ToolMetadata>> InstalledGroups { get; }

    ToolMetadata? Get(string id);
    ExtensionInfo? Info(string id);
    IReadOnlyList<ToolMetadata> Search(string query);

    /// <summary>Adds the tools of an extension, or replaces the tools of the same extension (an update). Never takes a tool id that another extension or the app uses.</summary>
    void Register(IEnumerable<ExtensionInfo> extensions);

    /// <summary>Removes tools of installed extensions and dev builds. Tools that ship with the app can't be removed.</summary>
    void Unregister(IEnumerable<string> ids);

    /// <summary>Raised when tools were added, removed, switched on or off.</summary>
    event Action? Changed;
}

public sealed class ToolRegistry : IToolRegistry
{
    public const string OtherGroup = "Other";

    private sealed record Snapshot(
        Dictionary<string, ExtensionInfo> ById,
        IReadOnlyList<ToolMetadata> Installed,
        IReadOnlyList<IGrouping<string, ToolMetadata>> InstalledGroups);

    private readonly object _gate = new();
    private readonly ExtensionSettings _settings;
    private Snapshot _snapshot;

    public event Action? Changed;

    private static ToolMetadata WithGroup(ToolMetadata tool) =>
        string.IsNullOrWhiteSpace(tool.Group) ? tool with { Group = OtherGroup } : tool;

    public ToolRegistry(IEnumerable<IToolModule> modules, ExtensionSettings settings)
    {
        _settings = settings;
        var all = modules.Select(m => new ExtensionInfo(WithGroup(m.Metadata), ExtensionSource.Host)).ToList();

        var duplicate = all.GroupBy(t => t.Tool.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Duplicate tool id '{duplicate.Key}'. Tool ids must be unique.");

        _snapshot = Build(all);
        settings.Changed += () => Changed?.Invoke();
    }

    private static Snapshot Build(IEnumerable<ExtensionInfo> infos)
    {
        var byId = infos.ToDictionary(i => i.Tool.Id, StringComparer.OrdinalIgnoreCase);
        // "Other" (tools without a group) is always listed last.
        var installed = byId.Values.Select(i => i.Tool)
            .OrderBy(t => t.Group == OtherGroup).ThenBy(t => t.Group).ThenBy(t => t.Title).ToList();
        return new Snapshot(byId, installed, installed.GroupBy(t => t.Group).ToList());
    }

    public IReadOnlyList<ToolMetadata> Installed => _snapshot.Installed;

    public IReadOnlyList<IGrouping<string, ToolMetadata>> InstalledGroups => _snapshot.InstalledGroups;

    public IReadOnlyList<ToolMetadata> All => Installed.Where(t => _settings.IsEnabled(t.Id)).ToList();

    public IReadOnlyList<IGrouping<string, ToolMetadata>> Groups => All.GroupBy(t => t.Group).ToList();

    public ToolMetadata? Get(string id) =>
        _snapshot.ById.TryGetValue(id, out var info) && _settings.IsEnabled(info.Tool.Id) ? info.Tool : null;

    public ExtensionInfo? Info(string id) => _snapshot.ById.GetValueOrDefault(id);

    public IReadOnlyList<ToolMetadata> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return All;

        var q = query.Trim();
        return All
            .Where(t => t.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || t.SearchTerms.Any(s => s.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public void Register(IEnumerable<ExtensionInfo> extensions)
    {
        var incoming = extensions.Select(e => e with { Tool = WithGroup(e.Tool) }).ToList();
        lock (_gate)
        {
            var map = new Dictionary<string, ExtensionInfo>(_snapshot.ById, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in incoming)
            {
                if (!seen.Add(item.Tool.Id))
                    throw new InvalidOperationException($"The extension contains the tool id '{item.Tool.Id}' twice.");
                if (map.TryGetValue(item.Tool.Id, out var existing)
                    && (existing.Source == ExtensionSource.Host || existing.Source != item.Source
                        || !string.Equals(existing.Package, item.Package, StringComparison.OrdinalIgnoreCase)))
                {
                    var owner = existing.Source is ExtensionSource.Host or ExtensionSource.Bundled ? "a built-in tool" : $"the extension '{existing.Package}'";
                    throw new InvalidOperationException($"Tool id '{item.Tool.Id}' is already used by {owner}.");
                }
                map[item.Tool.Id] = item;
            }
            _snapshot = Build(map.Values);
        }
        Changed?.Invoke();
    }

    public void Unregister(IEnumerable<string> ids)
    {
        var changed = false;
        lock (_gate)
        {
            var map = new Dictionary<string, ExtensionInfo>(_snapshot.ById, StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids)
            {
                if (map.TryGetValue(id, out var info) && info.Source is ExtensionSource.Installed or ExtensionSource.Dev)
                    changed |= map.Remove(id);
            }
            if (changed) _snapshot = Build(map.Values);
        }
        if (changed) Changed?.Invoke();
    }
}
