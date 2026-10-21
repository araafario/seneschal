# Making a Seneschal extension with the user

An extension is one or more tools: a page in the sidebar, and the same abilities as tools for you (MCP). You make it for the user step by step.

## What is Seneschal's and what is ordinary development
- Seneschal's own tools: seneschal_extension_create_from_template (the starting project, the only step that needs Seneschal), seneschal_extension_guide (this), seneschal_ui_guidelines (the design rules), seneschal_sdk_reference (the types), seneschal_extension_check (checks the project against the rules), seneschal_extension_preview (shows the page in a headless browser, in dark and light). Always use these.
- After the template, development is ordinary .NET: edit files, run dotnet build, run tests. If you can edit files and run commands yourself (a Claude Code with a terminal), do that and ignore seneschal_extension_files, _read, _write and _build: they only exist for an assistant that has no file access, such as the one built into Seneschal.

## 0. A new project, or one that already exists?
Making a tool does not always mean making a project. A project can hold several tools, and the user may already have one, in the projects folder or added by hand in Options, Extension Projects (it can be anywhere on the machine). Decide this before anything else:
1. Call seneschal_extension_list. It shows every project that can be worked on, where it is, and what each builds (its extensions and tools).
2. The user names a project and it is the same as one in the list (ignore case, spaces and a leading "Seneschal.") and the request is to add or change something in it ("under LoopUp", "in Seneschal.LoopupDevEnv", "add to the Bondio project"): work in that project. Do not create a project. Add the tool to it (a new tool class and page beside the existing ones, then the files the tool needs) and leave what is already there alone.
3. The name is only similar to one in the list (LoopupDevEnv and Seneschal.LoopupDevEnv, a sidebar group name such as LoopUp, a plural, a different spelling): do not guess. Ask with seneschal_ask_user whether it means the existing project or a new one, name the existing one with its folder, and mark the existing one as recommended.
4. The user gives no project and it is not clear: if one existing project fits what they want (the same group, the same service, the same data), ask in the same way. If nothing in the list fits, it is a new project.
5. The user says new ("create a new project", "a separate project") and a project of that name exists: ask whether to use the existing one or to pick another name. A name that exists is refused by the template anyway.
6. When you work in an existing project, say so in your first line ("Adding it to Seneschal.LoopupDevEnv, which already has Logs and Projects"), and do not run seneschal_extension_create_from_template.
## 1. Ask first
Ask short questions, one or two at a time, and suggest an answer to each. Do not start writing until these are clear:
- What should it do, and when does the user reach for it? Suggest a short name in PascalCase (JsonDiff) and a one-line description.
- What must it remember? Anything it keeps goes in its own data folder (IExtensionContext.DataDirectory), as small JSON files. Never a password.
- MCP: should you be able to use it? The default is yes, and it is the point of Seneschal. List the verbs you would offer (list, create, run, delete) and let the user cut them.
- Does it do work that outlives a page visit, or keep state in services of its own? Services are fine: they live in a container of the extension's own and are disposed when it is rebuilt, so timers and background work belong in an IDisposable singleton.
- Does it delete or overwrite anything? Those tools are Destructive and ask the user first.
- Which sidebar group and icon? A group groups related tools. Suggest one.
- What does it touch (network, files, processes)? An extension runs with the user's full access, so tell them what yours will do.

## 2. What every extension has (the baseline)
- extension.json: id (kebab-case, unique), name, version, description, entry (the dll), sdk (major.minor, never newer than this Seneschal), styles.
- A tool class implementing IToolModule with ToolMetadata: Id, Title, Description, Group, Icon, ComponentType (the page), SearchTerms and 1 to 3 Prompts (short requests that show as starter cards).
- IToolLifecycle when it needs ConfigureServices (register services, read the data folder) or Started.
- IToolMcp on the same class: every feature of the page is also a tool for you. Name them <tool>_<verb>. McpEffect.Read only looks, Change is easy to undo, Destructive removes or overwrites and asks the user first. Schemas are built with McpSchema.Object().Text(...).Flag(...).
- A page (.razor) built from MudBlazor components and the shared CSS classes. The app's own components (PageShell, Banner) are not available: use MudAlert.
- An engine class (<Name>Engine.cs) for the work, with no UI, so the page and the MCP tools share it and it can be checked without the page.
- A stylesheet (wwwroot/<id>.css) whose class names have a prefix of your own, using only the theme variables.

## 3. Design rules
Read seneschal_ui_guidelines before you write any page, and follow it. The points that fail a review most: short text with no trailing . , ; ! or ..., Title Case for buttons and titles, buttons are verbs, sizes only from the density scale (--fs-*, --h-ctl, --gap-*), colors only from theme variables (never a written color), an empty, loading, busy and error state for every list and action, and it must look right in dark and light mode. Also: reuse the shared classes before writing a style, make one thing the main thing on the page, let content of any length work (wrap, clamp or scroll inside its own box, no fixed widths), give every control hover, focus and disabled states and keep it reachable by keyboard. seneschal_extension_check finds what a script can see. Finding nothing does not mean the design matches: you still compare your page with the guidelines.

## 4. How to work
1. A new project: seneschal_extension_create_from_template makes it. An existing one: read its files first (seneschal_extension_files and _read, or the files themselves). Either way, build on what is there and do not rewrite it.
2. Write the files, one concern per file. Keep the work in the engine and the page thin.
3. Build. {{build}}
4. Run seneschal_extension_check, fix every error and warning, and judge every review item.
5. Test it (next section), fix what you find, and check again.
6. Tell the user where to find it in the sidebar and what you made, in two or three short lines, and what you tested.

## 5. Test it before you say it is done
Never say it works because the code looks right. Do these in order, and report what you did and what you could not do:
1. Build clean: no errors and no warnings.
2. seneschal_extension_check: no errors and no warnings.
3. It loaded: seneschal_list_extensions shows the extension and its tools and no failure. A failure says why.
4. Use every MCP tool of the extension the way the user would ask for it:
   - a normal call with realistic input, and check the answer is what the page would show
   - a bad call (a missing argument, empty text, an id that does not exist), and check the answer is a clear message from McpException and not a crash
   - a tool that changes something: do it on made-up data and undo it. Ask the user before any Destructive one.
   The extension's tools reach you on your next message, because the tool list is read when a message starts. Say so and carry on then.
5. State: if it saves anything, change something with a tool, build again (a build reloads the extension) and read it back. It must still be there, and it must live only in IExtensionContext.DataDirectory.
6. The engine: try examples with the output you expect, including empty input, one very long input and text that is not English.
7. Look at the page: seneschal_extension_preview opens it in a headless browser, in dark and light, and gives you the screenshots and what it found (errors in the console, failed requests, content wider than the window, the text of the page). Look for overlapping or cut text, empty areas, unreadable colors and anything that looks unlike the rest of Seneschal. Fix and look again.
8. Design: go through the checklist at the end of seneschal_ui_guidelines against the files you wrote. The check covers colors, sizes, wording and case; the screenshots show the layout. You cover what neither can: that every list and action has its empty, loading, busy and error state, and that anything which deletes asks first.
9. Say what you tested and what only the user can: clicking through the main action with real data. Never claim a test you did not run.

New projects are made in {{root}}. Projects the user added by hand are where they were added from, and seneschal_extension_list shows where.
