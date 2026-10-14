namespace Seneschal.Sdk;

/// <summary>What the app shows for a tool: its place in the sidebar, its icon, and the Blazor component that is its page.</summary>
public sealed record ToolMetadata(
    string Id,
    string Title,
    string Description,
    string Group,
    string Icon,
    Type ComponentType,
    string[] SearchTerms)
{
    /// <summary>
    /// True keeps the tool's view alive when the user navigates away: it is hidden instead of disposed, so its fields,
    /// inputs and timers carry on until the app restarts or the tool is turned off or removed. Default false: the tool
    /// starts fresh every time it is opened.
    /// </summary>
    public bool Persistent { get; init; }

    /// <summary>
    /// Things to ask the AI assistant about this tool, shown as suggestions when its chat is empty. Short requests in the user's own words,
    /// such as "Stop everything that is running", that the tool's MCP tools (see <see cref="IToolMcp"/>) can answer. The first one or two of each tool are used.
    /// </summary>
    public IReadOnlyList<string> Prompts { get; init; } = [];
}

/// <summary>Implemented by a class in the extension, once per tool. The app finds these by itself; nothing needs registering.</summary>
public interface IToolModule
{
    ToolMetadata Metadata { get; }
}
