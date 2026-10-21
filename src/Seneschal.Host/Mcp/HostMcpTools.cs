using Seneschal.Core;
using Seneschal.Dev;
using Seneschal.Sdk;

namespace Seneschal.Mcp;

/// <summary>What the AI assistant can do in the app itself: see which tools and extensions there are, and turn tools on or off.</summary>
public sealed class HostMcpTools(IToolRegistry tools, ExtensionSettings settings, DllExtensions extensions, Authoring authoring, Seneschal.Shell.GroupIcons icons, DevWatcher watcher, InstructionStore instructions, McpAddress address, AssistantSettings assistant, McpQuestions questions, SettingsReset reset) : IToolMcp
{
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
                Loaded = extensions.Loaded.Select(e => new { e.Name, e.Title, e.Version, Source = e.Origin.ToString(), e.ToolIds }),
                Failed = extensions.Failed.Select(f => new { f.Name, f.Error }),
                extensions.RestartPending,
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

        yield return new McpTool("seneschal_ask_user", "Ask the user to choose, shown as a card in the assistant panel with the options as rows. Use it instead of a question in plain text whenever there are a few clear options (which one, which approach, yes or no with a reason). Waits for the answer and returns the chosen labels, or what the user typed. Mark the option you recommend with recommended: true, because the user may let Seneschal pick it for them, and the answer then says Automatic. Returns no answer when the user skips",
            McpEffect.Read,
            McpSchema.Object()
                .Text("question", "The question, short", required: true)
                .Raw("options", new System.Text.Json.Nodes.JsonObject
                {
                    ["type"] = "array",
                    ["description"] = "The choices, two to six",
                    ["items"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new System.Text.Json.Nodes.JsonObject
                        {
                            ["label"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "string", ["description"] = "Short, one to five words" },
                            ["description"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "string", ["description"] = "One line that says what it means" },
                            ["recommended"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "boolean", ["description"] = "True for the option you recommend (one, unless more than one can be chosen). The user may let Seneschal pick it for them, so always mark one" },
                        },
                        ["required"] = new System.Text.Json.Nodes.JsonArray("label"),
                    },
                }, required: true)
                .Flag("multiple", "True when more than one can be chosen")
                .Flag("allowOther", "True (the default) to let the user type their own answer"),
            async (args, cancel) =>
            {
                var options = (args.Values["options"] as System.Text.Json.Nodes.JsonArray ?? [])
                    .OfType<System.Text.Json.Nodes.JsonObject>()
                    .Select(o => new QuestionOption(o["label"]?.GetValue<string>()?.Trim() ?? "", o["description"]?.GetValue<string>()?.Trim() ?? "", o["recommended"]?.GetValue<bool>() ?? false))
                    .Where(o => o.Label.Length > 0).ToList();
                if (options.Count < 2) throw new McpException("Give at least two options");
                var answer = await questions.AskAsync(new Question(args.Text("question"), options, args.Flag("multiple"), args.Flag("allowOther", true)), cancel);
                if (answer.Labels is null) return "The user did not answer. Carry on with a sensible default and say which";
                return answer.Automatic
                    ? new { Answer = answer.Labels, Automatic = true, Note = "The user did not choose: Seneschal picked the recommended option for them. Say so, and carry on" }
                    : (object)new { Answer = answer.Labels };
            });

        yield return new McpTool("seneschal_instructions_list", "List the texts that shape the assistant (character, lessons, extension guide, UI guidelines, SDK reference) and whether the user changed them",
            McpEffect.Read, McpSchema.None, _ => InstructionStore.All.Select(i => new { i.Id, i.Title, i.Description, Changed = instructions.IsCustom(i.Id) }));

        yield return new McpTool("seneschal_instruction_read", "Read one of the texts that shape the assistant",
            McpEffect.Read, McpSchema.Object().Choice("id", "Which text", InstructionStore.All.Select(i => i.Id), required: true),
            args => instructions.Get(args.Text("id")));

        yield return new McpTool("seneschal_instruction_update", "Replace one of the texts that shape the assistant, such as its character, with a new whole text. The user is asked first. Read it first and keep what is still right. To record one thing you learned, use seneschal_lesson_add instead",
            McpEffect.Destructive,
            McpSchema.Object().Choice("id", "Which text", InstructionStore.All.Select(i => i.Id), required: true).Text("content", "The whole new text", required: true),
            args =>
            {
                instructions.Set(args.Text("id"), args.Text("content"));
                return $"{args.Text("id")} is saved and used from the next message";
            });

        yield return new McpTool("seneschal_settings_reset", "Put Seneschal's own settings back to the defaults (theme, sidebar, icons, favorites, assistant settings, which extensions are off) at once. Extensions, their data, projects, chats and the MCP secret stay. The user is asked first",
            McpEffect.Destructive, McpSchema.None,
            _ =>
            {
                reset.Run();
                return "Settings are back to the defaults";
            });

        yield return new McpTool("seneschal_lesson_add", "Write down what you learned from a correction or a mistake, so you do not repeat it. One short line that says what to do or not do and why. Never write a secret, a token or a password. Lessons are sent with every message and the user can edit them in Options, Instructions",
            McpEffect.Change, McpSchema.Object().Text("lesson", "One line", required: true),
            args =>
            {
                instructions.AddLesson(args.Text("lesson"));
                return "Written down";
            });

        yield return new McpTool("seneschal_about", "Which version of Seneschal this is, where it is listening, and where its data folder and log file are",
            McpEffect.Read, McpSchema.None, _ => new
            {
                Version = Seneschal.Cli.EngineMode.Version,
                Address = address.Url,
                DataFolder = Seneschal.Core.AppPaths.DataDir,
                LogFile = System.IO.Path.Combine(Seneschal.Core.AppPaths.DataDir, "logs", "seneschal.log"),
            });

        yield return new McpTool("seneschal_find_icons", "Search the icons that can be used for a tool or a group (the names to give seneschal_set_icon)",
            McpEffect.Read, McpSchema.Object().Text("query", "Part of the name, such as folder or play", required: true),
            args => Seneschal.Shell.GroupIconCatalog.Names.Where(n => n.Contains(args.Text("query"), StringComparison.OrdinalIgnoreCase)).Take(40).ToList());

        yield return new McpTool("seneschal_set_icon", "Change the icon of a tool or of a group in the sidebar, or put the default back. The icon an extension gives is only the default",
            McpEffect.Change,
            McpSchema.Object()
                .Choice("kind", "Whether it is a tool or a group", ["tool", "group"], required: true)
                .Text("name", "The tool id (from seneschal_list_tools) or the group name", required: true)
                .Text("icon", "An icon name from seneschal_find_icons. Leave it out to put the default back"),
            args =>
            {
                var kind = args.Text("kind");
                var name = args.Text("name");
                var icon = args.Optional("icon");
                if (icon is not null && !Seneschal.Shell.GroupIconCatalog.All.ContainsKey(icon))
                    throw new McpException($"There is no icon '{icon}'. Search with seneschal_find_icons");
                if (kind == "tool")
                {
                    if (tools.Info(name) is null) throw new McpException($"There is no tool '{name}'");
                    if (icon is null) icons.ResetTool(name); else icons.SetTool(name, icon);
                }
                else
                {
                    if (!tools.Groups.Any(g => g.Key.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new McpException($"There is no group '{name}'");
                    if (icon is null) icons.Reset(name); else icons.Set(name, icon);
                }
                return icon is null ? $"{name} uses its default icon" : $"{name} now uses {icon}";
            });

        // Making an extension needs the .NET SDK. The "Help me create a new extension" card is always there, so without the SDK the
        // assistant gets one tool that says what is missing and how to add it, and none of the others. The SDK is looked for again
        // every minute, so installing it turns the rest on without a restart.
        if (!authoring.Available)
        {
            yield return new McpTool("seneschal_extension_guide", "How to make a Seneschal extension. Call it first when the user wants to make one",
                McpEffect.Read, McpSchema.None, _ => "The .NET SDK is not installed on this machine, and building an extension needs it. Tell the user to run `seneschal dev install` in a terminal (it installs the SDK for them alone and needs no administrator rights), then ask again. Seneschal notices the SDK by itself, so no restart is needed");
            yield break;
        }

        yield return new McpTool("seneschal_extension_guide", "How to make a Seneschal extension (a new tool) with the user: what to ask first, what every extension must have, the design rules in short, and the steps. Read it before you help anyone make one, then follow it",
            McpEffect.Read, McpSchema.None, _ => AuthoringGuide.Text(instructions, authoring.Root));

        yield return new McpTool("seneschal_ui_guidelines", "The full UI design rules of Seneschal: wording, spacing, color, components and the checklist. Read them before you write or change any page of an extension",
            McpEffect.Read, McpSchema.None, _ => instructions.Get("ui-guidelines"));

        yield return new McpTool("seneschal_sdk_reference", "The reference of the Seneschal SDK: every type an extension can use (the tool, its metadata, lifecycle, MCP tools and schemas, the context) with examples",
            McpEffect.Read, McpSchema.None, _ => instructions.Get("sdk-reference"));

        yield return new McpTool("seneschal_dev_folders", "List the folders Seneschal watches for dev builds of extensions (Live Reload): where each comes from and whether it exists yet",
            McpEffect.Read, McpSchema.None, _ => watcher.Entries.Select(f => new { f.Path, f.Source, f.Exists, CanRemove = f.Removable }));

        yield return new McpTool("seneschal_dev_folder_add", "Watch a folder for dev builds: every extension built into it is loaded by itself and loaded again on each build. Give a project folder to use its artifacts/extensions. This loads code from that folder, so the user is asked first",
            McpEffect.Destructive, McpSchema.Object().Text("path", "The full path of the folder, or of an extension project", required: true),
            args => Guard(() => new { Folder = watcher.AddFolder(args.Text("path")) }));

        yield return new McpTool("seneschal_dev_folder_remove", "Stop watching a folder that was added for dev builds. What was loaded from it stays loaded until it is removed or the app restarts",
            McpEffect.Change, McpSchema.Object().Text("path", "The folder, as seneschal_dev_folders shows it", required: true),
            args =>
            {
                var path = args.Text("path");
                if (!watcher.Entries.Any(f => f.Removable && f.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                    throw new McpException("That folder was not added here, so it cannot be removed here");
                watcher.RemoveFolder(path);
                return $"No longer watching {path}";
            });

        yield return new McpTool("seneschal_extension_list", "List every extension project that can be worked on: the ones in the projects folder and the ones the user added in Options, Extension Projects (anywhere on the machine). Each has its name, its folder, where it comes from, and what it builds (the extensions and the tool ids). Call it before you create or change anything: a request to make or add a tool may mean an existing project, so decide from this list whether the work belongs in one of them (see seneschal_extension_guide, step 0)",
            McpEffect.Read, McpSchema.None, _ => new { Folder = authoring.Root, Projects = authoring.ProjectList() });

        yield return new McpTool("seneschal_extension_delete_project", "Delete an extension project of the projects folder: its tools are unloaded and the project moves to .deleted in the projects folder, so the user can move it back. It never touches a project the user added by hand (use seneschal_dev_folder_remove to stop watching one). The user is asked first",
            McpEffect.Destructive, McpSchema.Object().Text("name", "The project name from seneschal_extension_list", required: true),
            args =>
            {
                var result = authoring.DeleteProject(args.Text("name"));
                if (!result.Ok) throw new McpException(result.Message);
                return result.Message;
            });

        yield return new McpTool("seneschal_extension_create_from_template", "Create a NEW extension project from Seneschal's template (only when the work really is a new project: never when the user means an existing one, which seneschal_extension_list shows, and ask with seneschal_ask_user when it is not clear; a name that already exists is refused): a tool class, a page, an engine, a manifest and a stylesheet, ready to build. This is the only step of making an extension that needs Seneschal. After it, development is ordinary: edit the files and run dotnet build yourself when you can (the file and build tools below are for assistants that have no file access, such as the built-in one). Read seneschal_extension_guide and ask the user what the tool should do first",
            McpEffect.Change,
            McpSchema.Object()
                .Text("name", "The project name: capital letter first, letters and digits only, such as JsonDiff", required: true)
                .Text("title", "What the tool is called in the app, such as Json Diff (default: the name split into words)")
                .Text("description", "One short phrase that says what the tool does")
                .Text("group", "The sidebar group it is listed under (default: Other)")
                .Flag("services", "True when the extension needs its own services (the template adds an example)")
                .Flag("git", "True to make a git repository of the project, with a .gitignore, a .gitattributes and a first commit"),
            args =>
            {
                var result = authoring.Create(new NewExtension(args.Text("name"), args.Optional("title") ?? "", args.Optional("group") ?? "", args.Optional("description") ?? "", Scaffolder.DefaultIcon, args.Flag("services"), args.Flag("git")));
                if (!result.Ok) throw new McpException(result.Message);
                return new
                {
                    result.Message,
                    result.Folder,
                    result.Notes,
                    Next = "Develop it like any .NET project, following seneschal_extension_guide and seneschal_ui_guidelines. If you cannot edit files yourself, read the files with seneschal_extension_files and seneschal_extension_read, change them with seneschal_extension_write and build with seneschal_extension_build. Then run seneschal_extension_check and test it as the guide says",
                };
            });

        yield return new McpTool("seneschal_extension_check", "Check an extension project against Seneschal's rules without changing it: the design guidelines (colors from theme variables, sizes from the scale, Title Case and no trailing punctuation, states), the tool class (every feature also an MCP tool, names, Destructive for deletes, starter prompts) and the manifest. Returns findings with file and line. Run it before telling the user the extension is done and fix every error and warning. Items marked review are for you to judge",
            McpEffect.Read, McpSchema.Object().Text("name", "The project name", required: true),
            args =>
            {
                var result = Guard(() => authoring.Check(args.Text("name")));
                return new
                {
                    result.Project,
                    result.Clean,
                    result.Errors,
                    result.Warnings,
                    result.Review,
                    Findings = result.Findings.Select(f => new { f.Severity, f.File, f.Line, f.Rule, f.Message }),
                };
            });

        yield return new McpTool("seneschal_extension_preview", "Look at a page of Seneschal the way a person would: opens it in a headless browser, in dark and light, and returns a screenshot of each plus what it found (errors in the console, requests that failed, content wider than the window, the text of the page). Use it on the page of an extension you made, after it is built and loaded, and look for overlapping or cut text, empty areas, unreadable colors and anything unlike the rest of Seneschal",
            McpEffect.Read,
            McpSchema.Object()
                .Text("toolId", "The tool whose page to look at (the id from seneschal_list_tools)")
                .Text("path", "Or any page of the app, such as /options/extensions. Without either, the home page")
                .Choice("theme", "Which theme (default both)", ["both", "dark", "light"])
                .Number("width", "Window width in pixels (default 1440)")
                .Number("height", "Window height in pixels (default 900)"),
            async (args, cancel) =>
            {
                if (!HeadlessBrowser.Available) throw new McpException("Neither Edge nor Chrome is installed here, so there is no browser to look at the page with");
                if (address.Url is not { } mcpUrl) throw new McpException("Seneschal is not listening yet");

                var path = args.Optional("path") ?? "/";
                if (args.Optional("toolId") is { } toolId)
                {
                    if (!tools.Installed.Any(t => t.Id.Equals(toolId, StringComparison.OrdinalIgnoreCase)))
                        throw new McpException($"There is no tool '{toolId}'. Use an id from seneschal_list_tools");
                    if (!settings.IsEnabled(toolId)) throw new McpException($"The tool '{toolId}' is turned off, so it has no page. Turn it on with seneschal_set_tool_enabled");
                    path = $"/tools/{toolId}";
                }
                if (!path.StartsWith('/')) throw new McpException("The path starts with /, such as /options/extensions");

                var theme = args.Optional("theme") ?? "both";
                IReadOnlyList<string> themes = theme == "both" ? ["dark", "light"] : [theme];
                var width = Math.Clamp(args.Number("width", 1440), 360, 2560);
                var height = Math.Clamp(args.Number("height", 900), 400, 2000);
                var baseUrl = new Uri(mcpUrl).GetLeftPart(UriPartial.Authority);

                try
                {
                    var previews = await HeadlessBrowser.CaptureAsync(baseUrl + path, themes, width, height, assistant.ShowTestBrowser, cancel);
                    var summary = previews.Select(p => new
                    {
                        p.Theme,
                        p.Title,
                        Drew = p.Loaded,
                        p.ScrollsSideways,
                        p.WiderThanWindow,
                        p.ConsoleErrors,
                        p.FailedRequests,
                        p.Text,
                    });
                    return (object?)new McpContent(new { Page = path, Window = $"{width}x{height}", Browser = assistant.ShowTestBrowser ? "with a window" : "headless", Themes = summary }, previews.Select(p => new McpImage(p.Png, "image/png")).ToList());
                }
                catch (InvalidOperationException ex)
                {
                    throw new McpException(ex.Message);
                }
            });

        yield return new McpTool("seneschal_extension_files", "For an assistant without file access: list the files of an extension project (build output is left out). With file access, just look in the folder",
            McpEffect.Read, McpSchema.Object().Text("name", "The project name from seneschal_extension_list", required: true),
            args => Guard(() => authoring.Files(args.Text("name"))));

        yield return new McpTool("seneschal_extension_read", "For an assistant without file access: read one file of an extension project",
            McpEffect.Read, McpSchema.Object().Text("name", "The project name", required: true).Text("file", "The path inside the project, such as JsonDiffView.razor or wwwroot/json-diff.css", required: true),
            args => Guard(() => authoring.Read(args.Text("name"), args.Text("file"))));

        yield return new McpTool("seneschal_extension_write", "For an assistant without file access: write one file of an extension project, replacing it whole when it exists. Only text files inside the project folder can be written",
            McpEffect.Change,
            McpSchema.Object().Text("name", "The project name", required: true).Text("file", "The path inside the project", required: true).Text("content", "The whole new content of the file", required: true),
            args => Guard(() => authoring.Write(args.Text("name"), args.Text("file"), args.Text("content"))));

        yield return new McpTool("seneschal_extension_build", "For an assistant without a terminal: build an extension project and load it into Seneschal. This runs the code that was written, so the user is asked first. Returns the compiler errors when it fails: fix the files and build again",
            McpEffect.Destructive, McpSchema.Object().Text("name", "The project name", required: true),
            async (args, cancel) =>
            {
                var result = await authoring.BuildAsync(args.Text("name"), cancel).ConfigureAwait(false);
                if (!result.Ok) throw new McpException(result.Message);
                return (object?)result.Message;
            });
    }

    /// <summary>A refused path or a missing project is an answer for the assistant to act on, not a crash.</summary>
    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            throw new McpException(ex.Message);
        }
    }
}
