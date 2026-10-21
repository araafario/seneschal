using System.Globalization;
using System.Text;
using Seneschal.Core;
using Seneschal.Dev;

namespace Seneschal;

/// <summary>
/// What the assistant is told before the first word: who it is, what Seneschal is, how its tools work, and what is installed right now.
/// Built again for every message, so a tool that was turned off or an extension that was just imported is already in it.
/// </summary>
public sealed class AssistantPrompt(IToolRegistry tools, ExtensionSettings settings, DotnetSdk sdk, DevMode dev, Mcp.McpAddress address, InstructionStore instructions)
{
    /// <summary>The command line of a program is limited (about 32,000 characters on Windows), so the list of tools is cut well before that.</summary>
    private const int MaxTools = 60;

    public string Build()
    {
        var text = new StringBuilder(instructions.Get("character").TrimEnd());
        text.AppendLine().AppendLine();
        if (instructions.Lessons() is { Length: > 0 } lessons) text.AppendLine(lessons).AppendLine();
        text.AppendLine("# Right now");
        text.AppendLine($"- Date: {DateTime.Now.ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture)}");
        text.AppendLine($"- MCP address: {address.Url ?? "not listening yet"} (setup for other clients: Options, MCP)");
        text.AppendLine($"- Seneschal {Cli.EngineMode.Version}{(dev.Enabled ? ", run from source (Options, Extension Projects and dev builds of extensions are available)" : "")}");
        text.AppendLine(sdk.Available
            ? "- Making extensions: possible, the .NET SDK is installed (seneschal_extension_guide, _create_from_template, _check, _preview, and _files, _read, _write, _build for an assistant without file access)"
            : "- Making extensions: not yet, the .NET SDK is missing. If asked, call seneschal_extension_guide: it tells the person to run `seneschal dev install`");

        var installed = tools.Installed;
        var on = installed.Where(t => settings.IsEnabled(t.Id)).ToList();
        var off = installed.Where(t => !settings.IsEnabled(t.Id)).ToList();
        if (installed.Count == 0)
        {
            text.AppendLine("- Tools: none are installed. This Seneschal is empty, which is normal at the start. The first useful thing you can offer is to make or import one");
            return text.ToString();
        }

        text.AppendLine($"- Tools turned on ({on.Count}), by group. Each is a page with its own MCP tools; seneschal_list_tools has the live list:");
        foreach (var group in on.Take(MaxTools).GroupBy(t => t.Group).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            text.AppendLine($"  - {group.Key}");
            foreach (var tool in group)
                text.AppendLine($"    - {tool.Title} (id {tool.Id}, page /tools/{tool.Id}): {tool.Description}");
        }
        if (on.Count > MaxTools) text.AppendLine($"  - and {on.Count - MaxTools} more: use seneschal_list_tools");
        if (off.Count > 0)
            text.AppendLine($"- Turned off: {string.Join(", ", off.Take(20).Select(t => t.Title))}. The person can turn them on in Options, Extensions, and you can with seneschal_set_tool_enabled");

        return text.ToString();
    }
}
