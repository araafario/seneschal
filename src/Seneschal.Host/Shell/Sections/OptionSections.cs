using MudBlazor;

namespace Seneschal.Shell.Sections;

/// <summary>One part of the Options page: its own component, shown at the right when it is picked in the list at the left. <see cref="Group"/> is the heading it is listed under.</summary>
public sealed record OptionSection(string Id, string Title, string Group, string Icon, Type Component, bool Developer = false);

public static class OptionSections
{
    /// <summary>In the order they are listed. To add a section, write a component and add a line here. A developer section is shown only where someone develops.</summary>
    public static IReadOnlyList<OptionSection> All { get; } =
    [
        new("extensions", "Extensions", "General", Icons.Material.Outlined.Extension, typeof(ExtensionsSection)),
        new("appearance", "Appearance", "General", Icons.Material.Outlined.Palette, typeof(AppearanceSection)),
        new("about", "About", "General", Icons.Material.Outlined.Info, typeof(AboutSection)),
        new("assistant", "Assistant", "AI", Icons.Material.Outlined.AutoAwesome, typeof(AssistantSection)),
        new("instructions", "Instructions", "AI", Icons.Material.Outlined.EditNote, typeof(InstructionsSection)),
        new("mcp", "MCP", "AI", Icons.Material.Outlined.Hub, typeof(McpSection)),
        new("mcp-tools", "MCP Tools", "AI", Icons.Material.Outlined.Handyman, typeof(McpToolsSection)),
        new("projects", "Projects", "Developer", Icons.Material.Outlined.Code, typeof(ProjectsSection), Developer: true),
    ];

    public static IReadOnlyList<OptionSection> Visible(bool developer) => [.. All.Where(s => developer || !s.Developer)];

    /// <summary>The section with this id, or the first visible one when there is none (the address /options).</summary>
    public static OptionSection Find(string? id, bool developer)
    {
        var visible = Visible(developer);
        return visible.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? visible[0];
    }
}
