namespace Seneschal.Shell;

/// <summary>One line of the What's New list on Home. Newest first, a handful at most: it is a nudge, not a changelog.</summary>
public sealed record WhatsNewItem(string Title, string Detail, string Href);

public static class WhatsNew
{
    public static IReadOnlyList<WhatsNewItem> Items { get; } =
    [
        new("The assistant asks with choices", "When there are a few options it shows them as a card you can click, and it can write down what it learns from your corrections", "/options/instructions"),
        new("Options is grouped", "General, AI and Developer, with every setting as a card and the MCP tools in a section of their own", "/options/extensions"),
        new("Projects has Live Reload", "Add a project, build it, and the tool appears without an import or a restart", "/options/projects"),
    ];
}
