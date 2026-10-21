using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Seneschal.Core;
using Seneschal.Sdk;
using Seneschal.Shell;
using MudBlazor;

namespace Seneschal.Dev;

/// <param name="Services">Include an example of an extension with its own service (kept in the extension's own container).</param>
public sealed record NewExtension(string Name, string Title, string Group, string Description, string IconKey, bool Services = false, bool Git = false);

/// <summary><see cref="Notes"/> are warnings the user should see (something that could not be done); the message itself is just what happened.</summary>
public sealed record ScaffoldResult(bool Ok, string Message, string? Folder = null, IReadOnlyList<string>? Notes = null);

/// <summary>What the New extension dialog hands back: the result, and whether to build the new extension right away.</summary>
public sealed record CreatedExtension(ScaffoldResult Result, string Name, bool BuildNow);

/// <summary>Creates extensions/&lt;Name&gt;/ with a working extension project and adds it to the solution.</summary>
public sealed partial class Scaffolder(DevMode dev, IToolRegistry registry, DllExtensions extensions)
{
    public const string DefaultIcon = "Extension";

    public ScaffoldResult Create(NewExtension request)
    {
        dev.Require();

        var name = request.Name.Trim();
        if (!ValidName().IsMatch(name))
            return Fail("Start the name with a capital letter and use only letters and digits");

        var id = Kebab(name);
        var folder = Path.Combine(dev.ExtensionsRoot!, name);
        if (Directory.GetDirectories(dev.ExtensionsRoot!).Any(d => Path.GetFileName(d).Equals(name, StringComparison.OrdinalIgnoreCase)))
            return Fail($"A folder named {name} already exists in extensions");
        if (registry.Info(id) is not null || extensions.Loaded.Any(e => e.Name.Equals(id, StringComparison.OrdinalIgnoreCase)))
            return Fail($"The id {id} is already used by another extension");
        if (!GroupIconCatalog.All.ContainsKey(request.IconKey))
            return Fail("Pick an icon from the list");

        var title = string.IsNullOrWhiteSpace(request.Title) ? DefaultTitle(name) : request.Title.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? $"{title} tool" : request.Description.Trim();
        var group = request.Group.Trim();
        var icon = IconExpression(request.IconKey);
        var sdk = $"{SdkInfo.Version.Major}.{SdkInfo.Version.Minor}";

        var csproj = $$"""
            <Project Sdk="Microsoft.NET.Sdk.Razor">

              <!-- The shared build settings (target framework, SDK reference, packaging) come from extensions/Directory.Build.props. -->
              <PropertyGroup>
                <RootNamespace>{{name}}</RootNamespace>
                <ExtensionId>{{id}}</ExtensionId>
              </PropertyGroup>

            </Project>
            """;

        WriteProject(folder, name, id, title, description, group, icon, sdk, request.Services, csproj);

        var note = "";
        try
        {
            SolutionFile.AddExtension(dev.SolutionFile!, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidDataException)
        {
            note = $"Not added to {DevMode.SolutionName} so add the project by hand ({ex.Message})";
        }

        return new ScaffoldResult(true, $"Created extensions/{name}", folder, note.Length > 0 ? [note] : null);
    }

    /// <summary>Writes the files of an extension project: the same ones wherever it lives. Only the project file differs.</summary>
    private static void WriteProject(string folder, string name, string id, string title, string description, string group, string icon, string sdk, bool services, string csproj)
    {
        Directory.CreateDirectory(Path.Combine(folder, "wwwroot"));

        Write(folder, $"{name}.csproj", csproj);

        Write(folder, "extension.json", $$"""
            {
              "id": "{{id}}",
              "name": "{{Esc(title)}}",
              "version": "1.0.0",
              "description": "{{Esc(description)}}",
              "entry": "{{name}}.dll",
              "sdk": "{{sdk}}",
              "styles": [ "{{id}}.css" ]
            }
            """);

        Write(folder, "_Imports.razor", """
            @using Microsoft.AspNetCore.Components
            @using Microsoft.AspNetCore.Components.Web
            @using MudBlazor
            @using Seneschal.Sdk
            """);

        Write(folder, Path.Combine("wwwroot", $"{id}.css"), $$"""
            /* Styles of this extension; the app links them into every page because extension.json lists this file.
               Use the app's theme variables (--fg, --fg-dim, --border, --hover, --accent, --card, --field) so the tool follows
               light and dark mode, and keep the class names specific so they cannot collide with other extensions. */
            .{{id}}-note { opacity: .7; font-size: var(--fs-small); }
            """);

        Write(folder, $"{name}Tool.cs", services ? ToolWithServices(name, id, title, description, group, icon) : PlainTool(name, id, title, description, group, icon));

        Write(folder, $"{name}View.razor", services
            ? $$"""
                @* The page title, the favorites button and the page container come from the app: build only the tool's content here. *@
                @inject {{name}}State State

                <MudTextField T="string" @bind-Value="_input" Label="Input" Immediate="true" Variant="Variant.Outlined" />
                <MudText Class="mt-4">@{{name}}Engine.Run(_input)</MudText>
                <MudText Class="{{id}}-note mt-6">Opened @State.Opened time@(State.Opened == 1 ? "" : "s") since the app started, counted by this extension's own service</MudText>

                @code {
                    private string _input = "";

                    // OnAfterRender with firstRender runs once per visit; OnInitialized also runs for the render at page load
                    protected override void OnAfterRender(bool firstRender)
                    {
                        if (!firstRender) return;
                        State.Opened++;
                        StateHasChanged();
                    }
                }
                """
            : $$"""
                @* The page title, the favorites button and the page container come from the app: build only the tool's content here. *@
                <MudTextField T="string" @bind-Value="_input" Label="Input" Immediate="true" Variant="Variant.Outlined" />
                <MudText Class="mt-4">@{{name}}Engine.Run(_input)</MudText>
                <MudText Class="{{id}}-note mt-6">This extension's styles are in wwwroot/{{id}}.css</MudText>

                @code {
                    private string _input = "";
                }
                """);

        Write(folder, $"{name}Engine.cs", $$"""
            namespace {{name}};

            // The code that does the tool's actual work. No UI and no framework dependencies, so it is easy to test.
            public static class {{name}}Engine
            {
                public static string Run(string input) => input;
            }
            """);

        if (services)
        {
            Write(folder, $"{name}State.cs", $$"""
                namespace {{name}};

                // A service of this extension: one instance for the whole app, registered in {{name}}Tool.ConfigureServices.
                public sealed class {{name}}State
                {
                    public int Opened { get; set; }
                }
                """);
        }

    }

    /// <summary>
    /// Creates an extension project in the projects folder (by default Documents/Seneschal/Projects), outside any checkout of Seneschal. The project
    /// file is nearly empty: the folder's Directory.Build.props, written by <see cref="LocalSdk"/>, points every project at the copy of the SDK that
    /// the running app uses.
    /// </summary>
    public static ScaffoldResult CreateLocal(string projectsRoot, NewExtension request)
    {
        var name = request.Name.Trim();
        if (!ValidName().IsMatch(name))
            return Fail("Start the name with a capital letter and use only letters and digits");
        if (!GroupIconCatalog.All.ContainsKey(request.IconKey))
            return Fail("Pick an icon from the list");

        var root = Path.GetFullPath(projectsRoot);
        var folder = Path.Combine(root, name);
        if (Directory.Exists(root) && Directory.GetDirectories(root).Any(d => Path.GetFileName(d).Equals(name, StringComparison.OrdinalIgnoreCase)))
            return Fail($"A project named {name} already exists in {root}");

        var id = Kebab(name);
        var title = string.IsNullOrWhiteSpace(request.Title) ? DefaultTitle(name) : request.Title.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? $"{title} tool" : request.Description.Trim();
        var group = request.Group.Trim();
        var icon = IconExpression(request.IconKey);
        var sdk = $"{SdkInfo.Version.Major}.{SdkInfo.Version.Minor}";

        Directory.CreateDirectory(root);
        LocalSdk.Ensure(root);
        Directory.CreateDirectory(folder);
        WriteProject(folder, name, id, title, description, group, icon, sdk, request.Services, $$"""
            <Project Sdk="Microsoft.NET.Sdk.Razor">

              <!-- The target framework, the Seneschal.Sdk reference and the build rules that stage and pack the extension come from the
                   Directory.Build.props and .targets of the folder above, which Seneschal keeps up to date. -->
              <PropertyGroup>
                <RootNamespace>{{name}}</RootNamespace>
                <ExtensionId>{{id}}</ExtensionId>
              </PropertyGroup>

            </Project>
            """);
        Write(folder, ".gitignore", GitInit.GitIgnore);
        var notes = request.Git && GitInit.Init(folder) is { } note ? new[] { note } : null;

        return new ScaffoldResult(true, $"Created {folder}", folder, notes);
    }

    /// <summary>
    /// Creates an extension project that lives outside this repository: it builds from the Seneschal.Sdk package alone, needs no solution and no
    /// checkout of Seneschal, and works from an installed Seneschal. The folder is made inside <paramref name="parentFolder"/>.
    /// </summary>
    public static ScaffoldResult CreateStandalone(string parentFolder, NewExtension request)
    {
        var name = request.Name.Trim();
        if (!ValidName().IsMatch(name))
            return Fail("Start the name with a capital letter and use only letters and digits");
        if (!GroupIconCatalog.All.ContainsKey(request.IconKey))
            return Fail("Pick an icon from the list");

        var folder = Path.Combine(Path.GetFullPath(parentFolder), name);
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            return Fail($"The folder {folder} is not empty");

        var id = Kebab(name);
        var title = string.IsNullOrWhiteSpace(request.Title) ? DefaultTitle(name) : request.Title.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? $"{title} tool" : request.Description.Trim();
        var group = request.Group.Trim();
        var icon = IconExpression(request.IconKey);
        var sdk = $"{SdkInfo.Version.Major}.{SdkInfo.Version.Minor}";
        var mudBlazor = typeof(MudBlazor.MudButton).Assembly.GetName().Version?.ToString(3) ?? "9.11.0";

        Directory.CreateDirectory(folder);
        WriteProject(folder, name, id, title, description, group, icon, sdk, request.Services, $$"""
            <Project Sdk="Microsoft.NET.Sdk.Razor">

              <!-- A Seneschal extension, built from the SDK package alone. The package brings the types and the build rules that stage
                   the extension and pack it as artifacts/packages/{{id}}.snx, the file to import in Options, Extensions. -->
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <RootNamespace>{{name}}</RootNamespace>
                <ExtensionId>{{id}}</ExtensionId>
                <!-- The SDK version must not be newer than the Seneschal that imports the extension -->
                <SeneschalSdkVersion>{{SdkInfo.Version.ToString(3)}}</SeneschalSdkVersion>
                <MudBlazorVersion>{{mudBlazor}}</MudBlazorVersion>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Seneschal.Sdk" Version="$(SeneschalSdkVersion)" />
                <!-- Compiled against, never shipped: the app provides MudBlazor -->
                <PackageReference Include="MudBlazor" Version="$(MudBlazorVersion)" ExcludeAssets="runtime" />
              </ItemGroup>

            </Project>
            """);

        Write(folder, ".gitignore", "bin/\nobj/\nartifacts/\n.vs/\n*.user\n");
        Write(folder, "README.md", $$"""
            # {{title}}

            A Seneschal extension. {{description}}.

            ## Build

            ```
            dotnet build
            ```

            The build stages the extension and packs it as `artifacts/packages/{{id}}.snx`.

            ## Try it

            - **Live:** set `SENESCHAL_DEV_EXTENSIONS` to the full path of `artifacts/extensions`, run `seneschal restart`, and rebuild after each change.
              Every build is loaded by itself, with or without services, and an open page updates.
            - **Installed:** open Seneschal, then Options, Extensions, Import, and choose the `.snx`.

            ## What is in it

            - `{{name}}Tool.cs`: the tool's name, icon and group, and what the AI assistant can do with it (`IToolMcp`)
            - `{{name}}View.razor`: the page
            - `{{name}}Engine.cs`: the work itself, with no UI
            - `extension.json`: the manifest
            - `wwwroot/{{id}}.css`: styles. Use the app's theme variables so the tool follows light and dark mode

            Everything a person can do on the page should also be a tool for the assistant. See the extension guide in the Seneschal repository, docs/extensions.md.

            ## Before you share it

            1. `dotnet build` with no errors and no warnings
            2. It shows in Options, Extensions with no failure
            3. Use each tool: a normal call and a bad one (empty or missing input), which must give a clear message
            4. If it saves anything, change it, rebuild and read it back
            5. Look at the page in dark and light mode, and go through the checklist at the end of docs/ui-guidelines.md
            (The assistant does all of this by itself with `seneschal_extension_check` and `seneschal_extension_preview`.)
            """);

        return new ScaffoldResult(true, $"Created {folder}", folder);
    }

    private static string PlainTool(string name, string id, string title, string description, string group, string icon) => $$"""
        using Seneschal.Sdk;
        using MudBlazor;

        namespace {{name}};

        // Metadata, plus what the AI assistant can do with the tool (IToolMcp). Group is optional: leave it empty and the tool is listed under "Other".
        public sealed class {{name}}Tool : IToolModule, IToolMcp
        {
            public ToolMetadata Metadata { get; } = new(
                Id: "{{id}}",
                Title: "{{Esc(title)}}",
                Description: "{{Esc(description)}}",
                Group: "{{Esc(group)}}",
                Icon: {{icon}},
                ComponentType: typeof({{name}}View),
                SearchTerms: ["{{Esc(title.ToLowerInvariant())}}"])
            {
                // true keeps this tool's inputs and state while the user visits other pages (until the app restarts)
                Persistent = false,
                // What the AI assistant suggests when its chat is empty: requests the tool's MCP tools below can answer
                Prompts = ["Run {{Esc(title)}} on some text"],
            };

            // Everything the page lets the user do should be here too, so the AI assistant can do it for them: list, create, delete, run.
            // Read only looks. Change is easy to undo. Destructive removes something and always asks the user first.
            public IEnumerable<McpTool> McpTools(IServiceProvider services)
            {
                yield return new McpTool("{{McpName(id)}}_run", "Run {{Esc(title)}} on some text and return the result",
                    McpEffect.Read,
                    McpSchema.Object().Text("input", "The text to use", required: true),
                    args => {{name}}Engine.Run(args.Text("input")));
            }
        }
        """;

    private static string ToolWithServices(string name, string id, string title, string description, string group, string icon) => $$"""
        using Seneschal.Sdk;
        using Microsoft.Extensions.DependencyInjection;
        using MudBlazor;

        namespace {{name}};

        // Metadata, plus the extension's own services (IToolLifecycle).
        // The services live in a container of this extension's own, made when it is loaded and disposed when it is rebuilt or removed:
        // a singleton that implements IDisposable should stop its timers and background work there.
        // It also lists what the AI assistant can do with the tool (IToolMcp).
        public sealed class {{name}}Tool : IToolModule, IToolLifecycle, IToolMcp
        {
            public ToolMetadata Metadata { get; } = new(
                Id: "{{id}}",
                Title: "{{Esc(title)}}",
                Description: "{{Esc(description)}}",
                Group: "{{Esc(group)}}",
                Icon: {{icon}},
                ComponentType: typeof({{name}}View),
                SearchTerms: ["{{Esc(title.ToLowerInvariant())}}"])
            {
                // true keeps this tool's inputs and state while the user visits other pages (until the app restarts)
                Persistent = false,
                // What the AI assistant suggests when its chat is empty: requests the tool's MCP tools below can answer
                Prompts = ["Run {{Esc(title)}} on some text"],
            };

            public void ConfigureServices(IServiceCollection services, IExtensionContext context)
            {
                services.AddSingleton<{{name}}State>();
                // context.DataDirectory is a folder only this extension writes to
            }

            public void Started(IServiceProvider services, IExtensionContext context)
            {
                // runs once, after the app has started
            }

            // Everything the page lets the user do should be here too, so the AI assistant can do it for them: list, create, delete, run.
            // Read only looks. Change is easy to undo. Destructive removes something and always asks the user first.
            // The services are the app's container, where this extension's own services live.
            public IEnumerable<McpTool> McpTools(IServiceProvider services)
            {
                yield return new McpTool("{{McpName(id)}}_run", "Run {{Esc(title)}} on some text and return the result",
                    McpEffect.Read,
                    McpSchema.Object().Text("input", "The text to use", required: true),
                    args => {{name}}Engine.Run(args.Text("input")));

                yield return new McpTool("{{McpName(id)}}_opened", "Say how many times {{Esc(title)}} was opened since the app started",
                    McpEffect.Read,
                    McpSchema.None,
                    _ => services.GetRequiredService<{{name}}State>().Opened);
            }
        }
        """;

    /// <summary>The icon as code, e.g. Icons.Material.Filled.Schedule. Keys are MudBlazor icon names; the Filled style is used when it exists.</summary>
    private static string IconExpression(string key)
    {
        var name = GroupIconCatalog.Canonical(key);
        var filled = typeof(Icons.Material.Filled).GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        return filled is not null ? $"Icons.Material.Filled.{name}" : $"Icons.Material.Outlined.{name}";
    }

    private static void Write(string folder, string file, string content) =>
        File.WriteAllText(Path.Combine(folder, file), content.TrimEnd() + Environment.NewLine, new UTF8Encoding(false));

    private static ScaffoldResult Fail(string message) => new(false, message);

    /// <summary>An id such as "json-diff" as the start of an MCP tool name: "json_diff".</summary>
    private static string McpName(string id) => id.Replace('-', '_');

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Kebab(string name) => KebabSplit().Replace(name, "-$1").TrimStart('-').ToLowerInvariant();

    /// <summary>"JsonDiff" becomes "Json Diff".</summary>
    public static string DefaultTitle(string name) => KebabSplit().Replace(name, " $1").Trim();

    public static bool IsValidName(string name) => ValidName().IsMatch(name.Trim());

    [GeneratedRegex("^[A-Z][A-Za-z0-9]{0,39}$")]
    private static partial Regex ValidName();

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex KebabSplit();
}
