# Building extensions

Every tool in Seneschal is an extension, and Seneschal ships with none, so what you have is what you chose. An extension is a small class library built against the [SDK](../src/Seneschal.Sdk/README.md), which is all it needs from the app.

## What one looks like

A folder (or a `.snx` zip of it):

```
extension.json      id, name, version, SDK version, entry assembly, stylesheets
<Name>.dll          the extension, plus any libraries the app does not already provide
wwwroot/...         static files, served by the app under /_ext/<id>/
```

In code it is a class that describes the tool and a Blazor component that is its page:

```csharp
public sealed class RegexTesterTool : IToolModule
{
    public ToolMetadata Metadata { get; } = new(
        Id: "regex-tester",
        Title: "Regex Tester",
        Description: "Try regular expressions against sample text",
        Group: "Text",
        Icon: Icons.Material.Filled.Spellcheck,
        ComponentType: typeof(RegexTesterView),
        SearchTerms: ["regex", "pattern", "match"]);
}
```

## Start here

You need Seneschal and the .NET SDK, which the installer adds unless you chose a lite install (`seneschal dev` tells you, `seneschal dev install` adds it). You do not need a copy of the Seneschal source, and you do not need Visual Studio.

1. **Let the assistant do it:** ask *Help me create a new extension*. It asks what the tool should do, whether you want it to be usable by Claude (MCP), what it must remember and what it may touch, then it creates the project, writes the code by the [UI guidelines](ui-guidelines.md), builds it (it asks you first, because that runs the code) and loads it. Fix what you do not like by asking again. Projects are in `source\seneschal-extensions` in your home folder (`SENESCHAL_EXTENSIONS_DIR` changes it).
   Or do it by hand: `seneschal extension new JsonDiff` in the folder you want it in.
2. **Build:** `dotnet build` in the new folder. The project uses the [`Seneschal.Sdk`](https://www.nuget.org/packages/Seneschal.Sdk) package, whose build rules stage the extension and pack it as `artifacts/packages/<id>.snx`.
3. **Try it live:** open **Options, Extension Projects** and use **Add Project** to add your project's folder (the project folder itself is enough: it uses its `artifacts/extensions`). The folder may not exist yet. Setting `SENESCHAL_DEV_EXTENSIONS` to that path does the same, for a script. From then on every `dotnet build` is loaded by itself, as soon as the build ends, whether you build in a terminal, Visual Studio or the assistant: the page you have open updates without a refresh. This is true for an extension with its own services too: each extension has a container of its own, replaced when it is rebuilt, and the old one is disposed, so there is never a restart. The **Live Reload** switch at the top of **Options, Extension Projects** turns this off. A dev build has **Remove Build** in its row menu, which deletes the staged folder (building again brings it back). The first build into a new folder is picked up too.
4. **Share it:** send the `.snx`. People import it in Options, Extensions.

The in-app **tutorial** (Options, Extensions, then Create an extension) walks through the same with code you can copy.

**Working on Seneschal itself?** Run it from source (see [Developing Seneschal](development.md)). **Options, Extension Projects** has **New Project**, which creates the project in your projects folder (`Documents\Seneschal\Projects`, never in the repository) and can build and load it for you. It builds against the Seneschal SDK that the running app uses.

## Testing your extension

Whoever makes it, a person or the assistant, the same steps tell you it works. The assistant follows them by itself (see [the assistant](assistant.md)):

1. `dotnet build` with no errors and no warnings.
2. The extension appears in Options, Extensions with no failure.
3. Use each of its tools: a normal call, then a bad one (an empty or missing argument, an id that does not exist), which should give a clear message and not a crash. A tool that changes something is tried on made-up data and undone.
4. If it saves anything, change it, rebuild (the build reloads the extension) and read it back.
5. Keep the work in the engine class, so it can be tried with edge cases (empty, very long, not English) without the page.
6. Look at the page in dark and light. `seneschal_extension_preview` does it in a headless browser and reports console errors and content that overflows. `seneschal_extension_check` finds colors, sizes, wording and case that break the [UI guidelines](ui-guidelines.md).
7. Go through the checklist at the end of the guidelines: an empty, loading, busy and error state for every list and action, and a question before anything is deleted.

## Making it available to the assistant

Everything the page lets a person do should be a tool the assistant can use too: list, create, delete, run. Implement `IToolMcp` next to `IToolModule`, and the app offers those tools while yours is turned on, with no restart. Anything that removes or overwrites is marked `Destructive`, which makes the assistant ask the user first. The `Icon` you give the tool is its default: people can pick another for it in Options, Appearance, and reset it. Add one to three requests to `ToolMetadata.Prompts` too: they show as starter cards when the assistant's chat is empty. The template includes an example of both.

## Where Seneschal finds extensions

- Next to the app, in `extensions/`: the ones a build of the app bundles (none by default).
- In the folders named by `SENESCHAL_DEV_EXTENSIONS`: dev builds, which win over a shipped copy of the same id.
- In `%AppData%\Seneschal\extensions`: the ones a user imported in Options, Extensions.

## Keep the look

Extensions use the same theme variables, wording rules and class names as the app, so a new tool feels like it was always there. Read the [UI guidelines](ui-guidelines.md) before touching a page.

## More

- [Extension SDK reference](../src/Seneschal.Sdk/README.md): every type an extension can use
