using Seneschal.Core;
using Seneschal.Sdk;

namespace Seneschal.Mcp;

/// <summary>An MCP tool and where it came from, for the assistant page and for the approval card.</summary>
public sealed record McpEntry(string Source, McpTool Tool);

/// <summary>
/// Collects the MCP tools of everything that is on: the app itself, the tools compiled into it and the tools of every loaded extension.
/// It asks again on every call, so an extension that is imported, updated, turned off or removed shows up or goes away at once.
/// </summary>
public sealed class McpRegistry(DllExtensions extensions, IToolRegistry tools, IEnumerable<IToolModule> hostModules, HostMcpTools hostTools, IServiceProvider services, ILogger<McpRegistry> log)
{
    public IReadOnlyList<McpEntry> All()
    {
        var entries = new List<McpEntry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string source, IToolMcp provider)
        {
            try
            {
                foreach (var tool in provider.McpTools(services))
                {
                    if (names.Add(tool.Name)) entries.Add(new McpEntry(source, tool));
                    else log.LogWarning("MCP tool {Tool} from {Source} was skipped because the name is taken", tool.Name, source);
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "The MCP tools of {Source} could not be listed", source);
            }
        }

        Add("Seneschal", hostTools);
        foreach (var module in hostModules.Where(m => tools.Get(m.Metadata.Id) is not null).OfType<IToolMcp>())
            Add("Seneschal", module);
        foreach (var (package, module) in extensions.Modules().Where(m => tools.Get(m.Module.Metadata.Id) is not null))
            if (module is IToolMcp provider) Add(package, provider);

        return entries;
    }

    public McpEntry? Find(string name) => All().FirstOrDefault(e => e.Tool.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
