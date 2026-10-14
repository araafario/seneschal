namespace Seneschal.Core;

/// <summary>
/// The static files of the extensions (stylesheets for now). They are served by the app under /_ext/&lt;id&gt;/..., and the
/// stylesheets an extension lists in its manifest are linked into every page.
/// </summary>
public sealed class ExtensionAssets
{
    private sealed record Entry(string Root, string Version, string[] Styles);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised when an extension's files were added or removed, so open pages can load the new stylesheets.</summary>
    public event Action? Changed;

    internal void Set(string id, string webRoot, string version, IEnumerable<string> styles)
    {
        lock (_gate) _entries[id] = new Entry(webRoot, version, [.. styles]);
        Changed?.Invoke();
    }

    internal void Remove(string id)
    {
        bool removed;
        lock (_gate) removed = _entries.Remove(id);
        if (removed) Changed?.Invoke();
    }

    /// <summary>The URLs of the stylesheets to link, one per style an extension lists.</summary>
    public IReadOnlyList<string> StyleUrls
    {
        get
        {
            lock (_gate)
                return _entries.SelectMany(e => e.Value.Styles.Select(s => $"/_ext/{Uri.EscapeDataString(e.Key)}/{Url(s)}?v={Uri.EscapeDataString(e.Value.Version)}")).ToList();
        }
    }

    /// <summary>The file for /_ext/&lt;id&gt;/&lt;path&gt;, or null when there is none. Nothing outside the extension's wwwroot is ever returned.</summary>
    public string? Resolve(string id, string path)
    {
        Entry? entry;
        lock (_gate) _entries.TryGetValue(id, out entry);
        if (entry is null || string.IsNullOrWhiteSpace(path)) return null;

        var root = Path.GetFullPath(entry.Root) + Path.DirectorySeparatorChar;
        var file = Path.GetFullPath(Path.Combine(entry.Root, path.Replace('/', Path.DirectorySeparatorChar)));
        return file.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(file) ? file : null;
    }

    private static string Url(string path) => string.Join('/', path.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
}
