# Building extensions

Every tool in Seneschal is an extension, including the ones that ship with it. An extension is a small class library built against the [SDK](../src/Seneschal.Sdk/README.md), which is all it needs from the app.

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

You need Seneschal and the .NET SDK. You do not need a copy of the Seneschal source.

1. **Create the project:** `seneschal extension new JsonDiff` in the folder you want it in. Or ask the assistant: *Help me create a new extension*. It asks what the tool should do and creates the project for you.
2. **Build:** `dotnet build` in the new folder. The project uses the [`Seneschal.Sdk`](https://www.nuget.org/packages/Seneschal.Sdk) package, whose build rules stage the extension and pack it as `artifacts/packages/<id>.snx`.
3. **Try it live:** set `SENESCHAL_DEV_EXTENSIONS` to the full path of `artifacts/extensions` and run `seneschal restart`. Rebuild after each change: an extension that registers no services reloads by itself, and one with its own services needs `seneschal restart`.
4. **Share it:** send the `.snx`. People import it in the Extension Manager.

The in-app **tutorial** (Extension Manager, then Tutorial) walks through the same with code you can copy.

**Working on Seneschal itself?** Run it from source (see [Developing Seneschal](development.md)). The **Developer** page then has **New Extension**, which creates the project inside `extensions/` of the repository, adds it to the solution and can build and load it for you.

## Making it available to the assistant

Everything the page lets a person do should be a tool the assistant can use too: list, create, delete, run. Implement `IToolMcp` next to `IToolModule`, and the app offers those tools while yours is turned on, with no restart. Anything that removes or overwrites is marked `Destructive`, which makes the assistant ask the user first. Add one to three requests to `ToolMetadata.Prompts` too: they show as starter cards when the assistant's chat is empty. The template includes an example of both.

## Where Seneschal finds extensions

- Next to the app, in `extensions/`: the ones that ship with it.
- In the folders named by `SENESCHAL_DEV_EXTENSIONS`: dev builds, which win over a shipped copy of the same id.
- In `%AppData%\Seneschal\extensions`: the ones a user imported in the Extension Manager.

## Keep the look

Extensions use the same theme variables, wording rules and class names as the app, so a new tool feels like it was always there. Read the [UI guidelines](ui-guidelines.md) before touching a page.

## More

- [Extension SDK reference](../src/Seneschal.Sdk/README.md): every type an extension can use
- [Dev Environment](../extensions/DevEnvironment/README.md): the bundled extension, a larger real example
