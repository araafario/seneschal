# Seneschal.Sdk

What an extension compiles against, and nothing else from the app. Everything in here is a promise to every extension author, so it is kept small.
The host loads this assembly once and every extension shares it, so they all see the same types.

## Get it

```powershell
dotnet add package Seneschal.Sdk
```

The package brings these types and the [build rules](#the-build-rules): after every build of your extension project it stages the extension and packs it as a `.snx`. You also need `extension.json` next to the project file and a reference to MudBlazor (compiled against, not shipped). The Seneschal that imports your extension must have an SDK at least as new as the one you built against. Start from `seneschal extension new` or the in-app tutorial.

## What is in it

| File | Purpose |
| --- | --- |
| `Tool.cs` | `IToolModule` (one class per tool) and `ToolMetadata`: id, title, description, group, icon, the Blazor component that is the page, search terms, `Persistent` and `Prompts` (requests the assistant suggests for the tool) |
| `IToolLifecycle.cs` | Optional hooks next to `IToolModule`: `ConfigureServices` (register the tool's own services) and `Started` (runs once after the app has started) |
| `IExtensionContext.cs` | What an extension may know about itself: `Id`, `Name`, `Version`, `Directory` and a private `DataDirectory` |
| `Mcp.cs` | `IToolMcp`, `McpTool`, `McpSchema`, `McpArguments`, `McpEffect`: what the AI assistant can do in a tool |
| `SdkInfo.cs` | The SDK version the app was built with |
| `build/Seneschal.Extension.targets` | MSBuild rules that stage and package an extension after every build |

## A tool in a few lines

```csharp
public sealed class RegexTesterTool : IToolModule
{
    public ToolMetadata Metadata { get; } = new(
        Id: "regex-tester",
        Title: "Regex Tester",
        Description: "Try regular expressions against sample text",
        Group: "Text",                        // optional: empty means "Other"
        Icon: Icons.Material.Filled.Spellcheck,
        ComponentType: typeof(RegexTesterView),
        SearchTerms: ["regex", "pattern", "match"])
    {
        Persistent = true,                    // optional: keep the view alive while the user visits other pages
        Prompts = ["Try a regex on my text"], // optional: what the assistant suggests when its chat is empty
    };
}
```

The app finds every `IToolModule` in the extension by itself, so nothing needs registering.

## Services and startup work

Implement `IToolLifecycle` on the same class when the tool needs its own services or background work.
Both methods are optional.

```csharp
public void ConfigureServices(IServiceCollection services, IExtensionContext context)
    => services.AddSingleton(new RegexTesterStore(context.DataDirectory));

public void Started(IServiceProvider services, IExtensionContext context) { }
```

Services live in a container of the extension's own, made when the extension is loaded and disposed when it is rebuilt, replaced or removed, so no extension ever needs the app to be restarted. Singletons that implement `IDisposable` are disposed with it: stop timers and background work there. Constructor injection works, `IEnumerable<T>` gives every registration, and what the extension does not have (the logger, the snackbar) comes from the app. Keyed services are not supported.

`context.DataDirectory` is a folder only this extension writes to. It survives restarts and updates and is not deleted when the extension is removed.

## Letting the AI use a tool

Implement `IToolMcp` on the tool class and list what the AI assistant can do. The app offers these tools to Claude through MCP while the tool is turned on, and no restart is needed.
Everything the page lets the user do should be a tool here: list, create, delete, run, reorder.

```csharp
public sealed class RegexTesterTool : IToolModule, IToolMcp
{
    public IEnumerable<McpTool> McpTools(IServiceProvider services)
    {
        yield return new McpTool("regex_tester_test", "Test a pattern against some text",
            McpEffect.Read,
            McpSchema.Object().Text("pattern", "The regular expression", required: true).Text("text", "The text", required: true),
            args => Regex.IsMatch(args.Text("text"), args.Text("pattern")));
    }
}
```

- `McpEffect.Read` only looks, `Change` is easy to undo, `Destructive` removes something and always asks the user first in the assistant panel
- `McpSchema` builds the arguments (`Text`, `Number`, `Flag`, `List`, `Choice`, and `Raw` for anything else); `McpArguments` reads them and throws an `McpException` the AI can act on when one is missing
- A tool returns text or any value (it becomes JSON). Throw `McpException("No project is called Gateway")` for a failure the AI can fix
- Names are lowercase words with underscores, start with the tool's own name and are unique across extensions (`projects_start`, `docker_add`)
- `services` is the app's container, so the tool reaches the services it registered with `GetRequiredService`
## Versioning

`SdkInfo.Version` follows this rule, and an extension states the version it was built against in `extension.json` (`"sdk": "1.0"`):

- **Major** changes when something breaks. The host rejects an extension built for another major version.
- **Minor** changes when something is added. An extension built against 1.2 needs a host that has 1.2.

## The build rules

`build/Seneschal.Extension.targets` runs after every build of an extension project:

1. Checks that `extension.json` sits next to the project file.
2. Stages the extension in `<ExtensionArtifacts>/extensions/<ExtensionId>/`: the manifest, the DLL and PDB, the libraries it needs and `wwwroot/`.
3. Zips that folder to `<ExtensionArtifacts>/packages/<ExtensionId>.snx`, the file a user imports in Options, Extensions.

| Property | Default | Meaning |
| --- | --- | --- |
| `ExtensionId` | the project name | Folder and package name, and should match the manifest id |
| `ExtensionArtifacts` | `<project>/artifacts/` | Where the staged folder and the package go |
| `ExtensionProvidedAssemblies` | `Seneschal.Sdk;MudBlazor` | Libraries the app already has, which are left out of the package |

An outside author gets the targets file from the SDK package, which ships it as `build/Seneschal.Sdk.targets`, the name NuGet imports on its own. The projects in this repository import it from `extensions/Directory.Build.targets`, with the shared settings in `extensions/Directory.Build.props`, so they are built the same way an outside author builds one.

## Rules for extensions

- Depend on this SDK, MudBlazor and your own packages only. Types that live inside the app (the tool card, the page frame) are not available.
- Keep a package's types out of `ToolMetadata`.
- Tool ids must be unique across all extensions.
