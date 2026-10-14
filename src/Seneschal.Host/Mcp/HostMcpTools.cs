using Seneschal.Core;
using Seneschal.Dev;
using Seneschal.Sdk;

namespace Seneschal.Mcp;

/// <summary>What the AI assistant can do in the app itself: see which tools and extensions there are, and turn tools on or off.</summary>
public sealed class HostMcpTools(IToolRegistry tools, ExtensionSettings settings, DllExtensions extensions) : IToolMcp
{
    private const string ExtensionGuide = """
        # Creating a Seneschal extension

        An extension is one or more tools (a page in the sidebar). Help the user step by step, and ask before you decide things for them.

        1. Ask what the tool should do, and who uses it. Suggest a short name (PascalCase, such as JsonDiff) and a one-line description.
        2. Create the project with seneschal_extension_new. It needs only the .NET SDK and builds from the Seneschal.Sdk NuGet package.
        3. The project has: <Name>Tool.cs (id, title, icon, group, starter prompts, and the assistant tools), <Name>View.razor (the page), <Name>Engine.cs (the work, with no UI), extension.json (the manifest) and wwwroot/<id>.css (styles).
        4. Build with dotnet build. That stages the extension and packs artifacts/packages/<id>.snx.
        5. Try it: set SENESCHAL_DEV_EXTENSIONS to the full path of artifacts/extensions and run seneschal restart. An extension without services reloads on every build. Or import the .snx in the Extension Manager.
        6. Share the .snx file. People import it in the Extension Manager.

        Rules to follow while writing it:
        - Everything the page lets a person do must also be a tool for the assistant (IToolMcp on the tool class): list, create, delete, run. Name them <tool>_<verb>. Anything that removes or overwrites is McpEffect.Destructive, which asks the user first.
        - Add 1 to 3 short requests to ToolMetadata.Prompts so the assistant can suggest them.
        - UI: short text with no trailing punctuation, Title Case on buttons and titles, the app's theme variables for color (never a written color), a prefix on your own CSS classes, and handle empty, loading and error states. Check dark and light mode.
        - Use MudBlazor components and the Seneschal.Sdk types only. The app's own components (the page frame, the banner) are not available: use MudAlert and the shared CSS classes.
        - The SDK version in the project must not be newer than the Seneschal that imports the extension (see seneschal version).
        """;

    public IEnumerable<McpTool> McpTools(IServiceProvider services)
    {
        yield return new McpTool("seneschal_list_tools", "List the tools in Seneschal (id, title, group, description, whether it is on and which extension it comes from)",
            McpEffect.Read, McpSchema.None, _ => tools.Installed.Select(t => new
            {
                t.Id,
                t.Title,
                t.Group,
                t.Description,
                Enabled = settings.IsEnabled(t.Id),
                Extension = tools.Info(t.Id)?.Package ?? "built-in",
                Path = $"/tools/{t.Id}",
            }));

        yield return new McpTool("seneschal_list_extensions", "List the loaded extensions and the ones that failed to load",
            McpEffect.Read, McpSchema.None, _ => new
            {
                Loaded = extensions.Loaded.Select(e => new { e.Name, e.Title, e.Version, Source = e.Origin.ToString(), e.ToolIds, RestartNeeded = e.HasServices }),
                Failed = extensions.Failed.Select(f => new { f.Name, f.Error }),
                extensions.RestartPending,
            });

        yield return new McpTool("seneschal_extension_guide", "How to create a Seneschal extension (a new tool), step by step. Read it before helping someone make one, then follow it with them",
            McpEffect.Read, McpSchema.None, _ => ExtensionGuide);

        yield return new McpTool("seneschal_extension_new", "Create the project of a new extension in a folder, ready to build. It builds from the Seneschal.Sdk package alone and needs no checkout of Seneschal. Ask the user what the tool should do and what to call it first",
            McpEffect.Change,
            McpSchema.Object()
                .Text("name", "The project name: capital letter first, letters and digits only, such as JsonDiff", required: true)
                .Text("folder", "The folder to make it in (a folder named after the project is created inside it). Default: source/seneschal-extensions in the user's home folder")
                .Text("title", "What the tool is called in the app, such as Json Diff (default: the name split into words)")
                .Text("description", "One short phrase that says what the tool does")
                .Text("group", "The sidebar group it is listed under (default: Other)")
                .Flag("services", "True when the extension needs its own services, which makes it need a restart to load"),
            args =>
            {
                var folder = args.Optional("folder") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "seneschal-extensions");
                var result = Scaffolder.CreateStandalone(folder, new NewExtension(args.Text("name"), args.Optional("title") ?? "", args.Optional("group") ?? "", args.Optional("description") ?? "", Scaffolder.DefaultIcon, args.Flag("services")));
                if (!result.Ok) throw new McpException(result.Message);
                return new
                {
                    result.Message,
                    result.Folder,
                    Next = new[]
                    {
                        "Open the folder and run: dotnet build",
                        "That makes artifacts/packages/<id>.snx, which is imported in the Extension Manager",
                        "To try it live set SENESCHAL_DEV_EXTENSIONS to the full path of artifacts/extensions and run seneschal restart",
                        "Change the tool in <Name>Tool.cs, the page in <Name>View.razor and the work in <Name>Engine.cs",
                    },
                };
            });

        yield return new McpTool("seneschal_set_tool_enabled", "Turn a tool on or off in the sidebar",
            McpEffect.Change, McpSchema.Object().Text("toolId", "The id from seneschal_list_tools", required: true).Flag("enabled", "True to turn it on, false to turn it off", required: true),
            args =>
            {
                var id = args.Text("toolId");
                if (tools.Info(id) is null) throw new McpException($"There is no tool '{id}'");
                var enabled = args.Flag("enabled", true);
                settings.SetEnabled(id, enabled);
                return $"{id} is now {(enabled ? "on" : "off")}";
            });
    }
}
