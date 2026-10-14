# Seneschal.Host

The app: a Blazor Web App (interactive server, MudBlazor, .NET 10) that provides the shell and loads the tools that extensions provide.
The shell is the sidebar, search, favorites, the group pages and the tool pages.
It also contains the Options page (the Extensions section lists, turns on and imports extensions) grouped as General (Extensions, Appearance), AI (Assistant, Instructions, MCP, MCP Tools) and Developer (Projects, for whoever makes extensions). Each section is a component in `Shell/Sections`, built from `OptionHeader`, `SettingCard` and `SettingExpander`.

## Run

```powershell
.\dev.ps1                                   # from the repository root: builds, then starts it through the launcher, hidden (-Attached: runs the https profile in this window, restarts on exit code 42)
dotnet run --project src/Seneschal.Host     # plain run
```

Default addresses (`Properties/launchSettings.json`): `https://localhost:7228` and `http://localhost:5293`.

## Layout

```
Core/    extension loading: manifest, package, load context, registry, settings, paths
Dev/     the engine of Options, Projects: repo detection, scaffolder, solution file edits, build, restart
Shell/   the UI: layout, sidebar, search, tool and group pages, dialogs, theme
  Pages/ Home, GroupPage, ToolHost, ExtensionManager, Options, Tutorial, Error, NotFound
wwwroot/ app.css (theme variables and shared styles), the logo (seneschal.png) and the icons (favicon.ico and favicon.png)
seneschal.ico  the icon of Seneschal.exe (also built into the project as its ApplicationIcon)
```

## Startup

`Program.cs` builds in this order, which matters:

1. `AddExtensionManagement()` and `AddExtensions(env)` load every extension that is present, **before the container is built**, so an extension can register its own services through `IToolLifecycle.ConfigureServices`.
2. The container is built.
3. `DllExtensions.LoadAll()` activates what was loaded: it registers the tools and stylesheets.
4. `StartTools()` calls `IToolLifecycle.Started` on each tool.

An extension can be imported, updated or removed while the app runs, with or without services of its own (see the next section). Nothing waits for a restart.

## Where extensions come from

| Source | Location | Notes |
| --- | --- | --- |
| Bundled | `extensions/<id>/` next to the app | None by default. An extension from a project in this repository can be listed as a `BundledExtension` item in `Seneschal.Host.csproj`, which copies it from `artifacts/extensions` on build and publish |
| Dev | the folders in `SENESCHAL_DEV_EXTENSIONS` (separated by `;`) | In Development the repository's `artifacts/extensions` is added without the variable. A dev build wins over a bundled copy of the same id |
| Installed | `%AppData%/Seneschal/extensions` | Imported by the user. Only these can be removed or replaced in Options, Extensions |

Tools compiled into the app itself (the older `Tools/` folder approach) are still supported and are reported as the `Host` source.

An extension's own `wwwroot` is served under `/_ext/<id>/`, and the stylesheets listed in its manifest are linked into every page.
A request can never reach a file outside that folder.

## Files and settings

| What | Where |
| --- | --- |
| App data | `%AppData%/Seneschal`, or the folder in `SENESCHAL_DATA_DIR` (used to isolate test runs) |
| Favorites | `favorites.json` |
| UI preferences | `ui.json` (theme, sidebar and so on) |
| Extension on/off switches | `extensions.json` |
| A private folder per extension | `extension-data/<id>`, handed to the extension as `DataDirectory` and kept when it is removed |

A data folder from the earlier name of the app (DevToolbelt) is adopted once so nothing a user set up is lost.

## Environment variables

| Variable | Effect |
| --- | --- |
| `SENESCHAL_DEV_EXTENSIONS` | Extra folders with dev builds of extensions |
| `SENESCHAL_DATA_DIR` | Replaces the app data folder |
| `SENESCHAL_RESTARTABLE` | Set by `dev.ps1`. It tells the app that exit code 42 will be followed by a restart, which enables the *Restart app* buttons |
| `ASPNETCORE_ENVIRONMENT` | `Development` (together with a repo checkout) lets Options, Projects create projects inside the repo |

## Options, Projects

Shown when the .NET SDK is installed or `DevMode` is on. `DevProjects` merges the projects of this repo (when the app finds `Seneschal.slnx` above its folder) and the folders being watched (Live Reload). Live Reload is the switch at the top: a project builds into its `artifacts/extensions` and `DevWatcher` loads each build.

- **New Project** (`Authoring.Create`, `Scaffolder`) writes a complete project into `extensions/<Name>` and adds it to the solution.
- **Build** (`Authoring`) runs `dotnet build`, shows the compiler errors and loads the result into the running app, live.
- **Delete Project** (repo projects) moves the project to `.deleted/extensions/` at the repository root, removes it from the solution and deletes its build output.
  The dialog lists what will happen. A **built-in** extension (one the host project bundles) can be deleted too, but only after typing `seneschal` to confirm: besides the steps above it is removed from the `BundledExtension` items of `Seneschal.Host.csproj` (otherwise the next build would look for a project that is gone) and the copy built next to the app is deleted. Restart Seneschal afterwards. Move the project back from `.deleted/extensions` and add the `BundledExtension` line again to restore it.

Only a direct child folder of `extensions/` that contains a project is ever touched, whatever name a caller passes.

## Running hidden (an installed app)

The same `Seneschal.exe` is the app and the command. Given a command (`open`, `start`, `stop`, `restart`, `status`, `logs`, `version`) it does that and ends (`Cli/EngineCli.cs`); with no command from source (`dotnet run`, `dev.ps1`) it serves as before.

- `seneschal start` runs a second copy of itself as the **engine**: `Seneschal.exe --engine=true --urls=http://localhost:5380 --environment=Production` (the next free port when 5380 is taken, `SENESCHAL_PORT` to choose), started through the shell so it inherits nothing from the caller (a script that reads the output of `seneschal start` would otherwise wait for the engine to end), no window, and its output in `%AppData%/Seneschal/logs/seneschal.log` (over 5 MB it moves to `.old`). `Cli/EngineMode.cs`
- The engine writes `engine.json` (process id, port, a secret, version) and removes it when it stops. The commands read it and talk to `GET /_engine/ping` and `POST /_engine/quit`, which only answer with the secret in the `X-Seneschal-Engine` header.
- **Restart** in the app starts a new copy that waits for the old one to end first (`--wait-pid=<id>`), because nothing supervises the engine. From source with `dev.ps1 -Attached` it exits with code 42 instead. A copy started from a checkout (a `Seneschal.slnx` above its folder) is started as Development, so `dev.ps1` through the launcher has the Developer page.
- **Quit** in the sidebar (engine only) and `seneschal stop` end the app. Projects it started keep running, as on any restart, and show up again as running when it starts.
- Building the app first ends a copy that runs from the build's own output folder (`StopRunningCopy` in `Seneschal.Host.csproj`, which runs `stop-running-copy.ps1`), so it no longer locks the files the build replaces. It ends the process directly instead of asking `seneschal stop`, because a build that failed half way leaves a `Seneschal.exe` that cannot start. A copy running from another folder, such as an installed one, is left alone. `seneschal stop --from <folder>` does the same politely by hand.
- The shutdown wait is 5 seconds, so an open browser tab does not hold the app up.

`BrowserLauncher` shows the page in an app window (`--app=<url>` with Edge, else Chrome) and falls back to the default browser. The shortcuts do not start `Seneschal.exe` (a console program, which flashes a black window) but `SeneschalApp.exe` from `src/Seneschal.Launcher`, a program without a console that runs `Seneschal.exe open` hidden and shows a message box when that fails.

`install.ps1` and `install.cmd` in the repo root install a release: download from GitHub, check the SHA-256, copy, add to PATH, add the Start menu shortcut, and stop a running copy first.

## Updates

`Updates/` keeps an installed copy current (`seneschal update`, and a background service while the engine runs).

- `UpdateService` looks at GitHub's latest release every 3 days (`SENESCHAL_UPDATE_DAYS`, `SENESCHAL_UPDATE_URL` to use another address that answers the same JSON). Pre-releases are never offered. Nothing happens in a build from source (version `0.0.0-dev`) or without `SeneschalApp.exe` beside the app.
- Same major number: `UpdateInstaller.StageAsync` downloads the zip for this machine, checks it against `SHA256SUMS.txt` and unpacks it to `%AppData%/Seneschal/updates/<version>/`. `UpdateState` makes the notice (`Shell/UpdateNotice.razor`). A new major number is only announced.
- `ApplyStaged` goes in when nothing runs from the files or at a restart: every file is renamed aside (`name.old-<time>`, which Windows allows for files in use) and the new one copied in, and a failure puts everything back. The old files are deleted at the next start. The in-app **Restart App** and `seneschal start` call it before the new copy starts.
- `update.json` in the data folder keeps the last check and whether updates install themselves (`seneschal update auto on|off`). The updater catches everything: a background service that throws would stop the whole app.

## The services of an extension

An extension's `ConfigureServices` does not touch the app's container, which cannot change once it is built. Each extension gets its own container instead (`Core/ExtensionContainer.cs`: singletons, scoped and transient services, constructor injection, `IEnumerable<T>`, hosted services, open generics; anything it does not have comes from the app), kept by `Core/ExtensionServiceHub.cs` under the extension's id. `DllExtensions.Activate` makes it, puts it in (before the tools are shown, so a page drawn at that moment finds it), and disposes the one it replaces, which stops its timers and background work; `Remove` and `UnloadDev` dispose it too.

Pages ask for their services with `@inject` and Blazor fills them from the scope of the browser tab (a circuit). The app's provider is therefore wrapped (`HostServiceProvider`, made by `SeneschalServiceProviderFactory` in `Program.cs`): it answers with the app's own container first and then asks the hub, and its scopes also hold each extension's scoped services for that tab. The circuit factory of Blazor makes its scopes from the container's own scope factory, which does not know the wrapper, so `UseHostScopesForCircuits` registers it again with ours (the framework's type is internal, found by name; if it is not found the app writes a line to the error output and extensions with services cannot be injected). Everything that needs to see extension services (`DllExtensions`, `McpRegistry`, the lifecycle hooks) uses `ExtensionServiceHub.Host`, never an injected `IServiceProvider`, which is the app's own container.

Not supported: keyed services in an extension.

## Live reload of dev builds

`Dev/DevWatcher.cs` (a hosted service) watches every dev folder (`SENESCHAL_DEV_EXTENSIONS`, and `artifacts/extensions` of the repository when running from source), each extension's folder for itself. A build writes many files, so an extension is loaded once its folder has been quiet for about a second, through `DllExtensions.LoadDev`: the page, the stylesheets and the MCP tools follow, and an open page re-renders. A failure (the build may still hold a file, so it is tried twice) shows in Options, Extensions as a failed extension until a later build loads. A dev folder that does not exist at the start is watched once it appears, and what is in it is loaded; what was there at the start was loaded by the start. A dev build (or one that failed to load) can be removed in Options, Extensions with **Remove Build**: `DllExtensions.RemoveDevBuild` deletes its staged folder, only when it is inside a dev folder, and unloads it. A built-in extension (one next to the app) has no remove, and its menu says it is part of this copy of the app. Folders come from three places: the environment variable and the repository (fixed, `ExtensionStartup.DevFolderPaths`) and the ones added in Options (`UiSettings.DevFolders`, saved in `ui.json`, loaded at the start too and watched without a restart: adding one also loads what it already holds). A project folder is turned into its `artifacts/extensions`. The assistant can list, add (it asks first: it loads code) and remove them (`seneschal_dev_folders`, `_add`, `_remove`). The switch is `UiSettings.DevLiveReload` (on by default, shown in Options, Extensions only where there is a dev folder).

## The assistant's character

`Assistant/AssistantPrompt.cs` builds the system prompt that `ClaudeChat` passes with `--append-system-prompt` on every message: who the assistant is (the Seneschal, a steward: calm, brief, plain), what Seneschal is, how the tools work over MCP, the rules (look before acting, never invent ids, never reveal the secret, Destructive asks), and a "Right now" part with the date, the version, the MCP address, whether the SDK is there, and the tools that are on or off. The list of tools is cut at 60 because a command line is limited. A change to the wording is one place; keep it short, because the whole text is sent on every message.

## Options, MCP

`Shell/Sections/McpSection.razor` is where a person connects their own AI: the address (`McpAddress`), the secret (shown on request, `AssistantSettings.RegenerateSecret` makes a new one and the old one stops working at once), a copyable setup per client (Claude Code, Cursor, VS Code, Claude Desktop, any other), the *Ask before every change* setting, and every tool the server offers (`McpRegistry.All()`). Resetting the secret and the approval setting are deliberately not MCP tools: an AI must not be able to lock itself in or out, or switch off its own questions.

## Icons

`Shell/GroupIcons.cs` decides which icon is drawn for a group and for a tool: the user's choice from `icons.json` in the data folder (`{ "Groups": {...}, "Tools": {...} }`, the older `groups.json` is read once when there is no `icons.json`), otherwise the default (what the extension gave the tool, or the built-in icon for a group name, then a folder). Everything that draws a tool or group icon calls `GroupIcons.Tool` or `GroupIcons.Get`, and subscribes to `Changed`. The user changes them in Options, Appearance (`Shell/Sections/AppearanceSection.razor`, with `GroupIconDialog` as the picker); the assistant can with `seneschal_find_icons` and `seneschal_set_icon`.

## What the assistant is told

`Assistant/InstructionStore.cs` holds the texts the assistant follows: the character (`Assistant/Instructions/character.md`), the extension guide with its test strategy (`guide.md`, with `{{build}}` and `{{root}}` filled in by `Mcp/AuthoringGuide.cs`), the UI guidelines (`docs/ui-guidelines.md`) and the SDK reference (`src/Seneschal.Sdk/README.md`). The defaults are embedded in the app. A text the person changed in Options, Assistant (`Shell/Sections/AssistantSection.razor`, with `Shell/InstructionDialog.razor` as the editor) is a file in `%AppData%/Seneschal/assistant/instructions/<id>.md` and wins; it is read each time it is needed, so a change applies from the next message. `AssistantPrompt` and `HostMcpTools` take every text from the store.

## Checking and looking at an extension

- `Dev/ExtensionCheck.cs` (`seneschal_extension_check`) reads the files of a project and returns findings with file and line, as error, warn or review: colors written instead of theme variables, font sizes off the scale, spacing off the 4px grid, global CSS selectors, button and title case, trailing punctuation, destructive tool names that are not `Destructive`, tool names without the tool's id, no `IToolMcp`, no prompts, missing states, and the manifest. Only a script can be exact about these, so what it cannot judge is marked review and never fails the check.
- `Dev/HeadlessBrowser.cs` (`seneschal_extension_preview`) starts the Edge or Chrome that is on the machine, headless (or with its window when `AssistantSettings.ShowTestBrowser` is on), and talks to it over the DevTools Protocol with a web socket: it opens the page with `?theme=dark` or `?theme=light` (`MainLayout` honours it without saving it), waits until the page has drawn (the frame is there and the placeholders are gone), takes the screenshot and collects console errors, failed requests, content wider than the window and the page text. The pictures are returned as MCP image content (`McpContent` in `Mcp/McpServer.cs`).

## Developer tools for the assistant

`Dev/DotnetSdk.cs` looks for a .NET SDK of version 10 or newer (the `dotnet` on PATH, the one the installer puts in `%LOCALAPPDATA%\Seneschal\dotnet`, Program Files, the dotnet-install default) and looks again every minute, so installing it while the app runs is noticed. `SENESCHAL_DOTNET=none` pretends there is none (a lite install, or switching the tools off). `seneschal dev install` installs it with Microsoft's dotnet-install script.

The *Help me create a new extension* card is always the first starter card. When the SDK is there, `HostMcpTools` offers the authoring tools: the guide (`Mcp/AuthoringGuide.cs`), the UI guidelines and the SDK README (the texts come from `InstructionStore`), `seneschal_extension_create_from_template` (the one tool that only Seneschal can do: the starting project), `_check`, `_preview` and `_list`, and `_files`, `_read`, `_write` and `_build`, which exist for an assistant with no file access or terminal: an AI that can edit files and run `dotnet` develops the ordinary way and uses only the template, the guides, the check and the preview. Without the SDK only `seneschal_extension_guide` is listed, and it tells the assistant to have the user run `seneschal dev install`.

`Dev/Authoring.cs` does the work. Run from source it uses the repository's `extensions/` and `Workspace.BuildAsync` (live load). Installed, it uses `SENESCHAL_EXTENSIONS_DIR` (default `source/seneschal-extensions` in the home folder), builds with `dotnet build -c Release` and imports the `.snx` like Options, Extensions does. Reading and writing is confined to one project folder, only text file types, never `bin`, `obj` or `artifacts`, at most 256 KB a file. `seneschal_extension_build` is `Destructive` on purpose: it runs the written code, so it always asks.

## Extension templates

`Dev/Scaffolder.cs` makes new extension projects. `Create` is for the Developer page (inside this repository, added to the solution). `CreateStandalone` is for everyone else: a project that builds from the `Seneschal.Sdk` package, used by `seneschal extension new` and by the assistant tool `seneschal_extension_create_from_template`. `seneschal_extension_guide` gives the assistant the guide to follow.

## The Assistant

The **assistant** is a button in the bottom right corner of every page that opens a chat with the Claude Code that is already installed and signed in on this machine, so the Claude subscription is used and no key is needed.
Each message starts `claude -p` (and resumes the same session after the first) with Seneschal as its only tool source, so Claude can do what the user can do in the app and nothing else (`--tools ""`, `--strict-mcp-config`, project settings only so user hooks and plugins do not run).

- `Assistant/ChatHistory.cs` keeps the last 30 chats in `assistant-history.json`; a chat from the history goes on with Claude's own session id, even after a restart
- The panel draws its frame first and its content a moment after, and `ClaudeChat.ClaudePath` keeps the result of the PATH search, so opening it is not held up by the lookup
- `Assistant/ClaudeChat.cs` runs Claude and turns its stream into chat lines (text, tool calls, errors); `Shell/AssistantWidget.razor` is the corner button and chat panel
- `Mcp/McpServer.cs` is the MCP endpoint at `POST /mcp` (Streamable HTTP, JSON answers). Only this machine can reach it, and only with the secret in `%AppData%/Seneschal/mcp-secret.txt`. It is served over plain http because Claude does not know the dev certificate
- `Mcp/McpRegistry.cs` asks everything that is on for its tools on every call: the app (`HostMcpTools`), tools compiled into it and every loaded extension that implements `IToolMcp`
- `Mcp/McpApprovals.cs` holds a destructive call (or any change, when the user chose to be asked) until the user answers in the chat panel. No answer in five minutes counts as no
- The settings dialog has the Claude path, the model (Default, Opus, Sonnet or Haiku, also switchable in the panel header), "Ask before every change" and a command that adds Seneschal to a Claude Code of your own
- A run's raw output is kept in `%AppData%/Seneschal/assistant/last-run.txt` to find out why a run showed nothing
## UI conventions

- Every page except the welcome page uses `PageShell`: title bar with icon and actions on the right, a divider, then the content.
- The popups (dialogs, menus, snackbars) are rendered inside the themed layout on purpose: the colors below are CSS variables defined on that layout, so a popup placed outside it would get none of them.
- Colors come from the CSS variables set by `app-dark` and `app-light` in `app.css` (`--fg`, `--border`, `--accent`, `--card` and so on). Extensions use the same variables so they follow the theme.
  The light theme is a cool paper-white page with solid white cards (a hairline border and a soft shadow, `--card-shadow`), the navy of the steward's coat as the accent and a light terminal (`--terminal`). `--on-accent` is the text color to use on the accent (white in light mode, near black in dark mode) so nothing hard-codes it.
- Results and notices use `Banner` (`Shell/Banner.razor`), not a bare `MudAlert`: the icon, the text and the buttons are centered on one row. It takes a `Tone` (success, warning, error or info) and either a `Title` and `Detail` or a `Message`, where the first line is the title and the lines after it the detail. Keep a message to what is new: a banner that asks for a restart already says so, so the result of the action does not repeat it.
- Buttons share one standard in `app.css`: sentence case, 36px high, the accent color with dark text for the primary action and a soft outline for the others.
- A tool marked `Persistent` stays mounted and is hidden with `persist-hidden` while the user is elsewhere.

## Build

```powershell
dotnet build Seneschal.slnx
```

Building the app also builds the bundled extensions. If the app is running, the build cannot replace its files, so stop it first.
