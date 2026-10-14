# Seneschal.Host

The app: a Blazor Web App (interactive server, MudBlazor, .NET 10) that provides the shell and loads the tools that extensions provide.
The shell is the sidebar, search, favorites, the group pages and the tool pages.
It also contains the Extension Manager and, when running from a checkout, the Developer page.

## Run

```powershell
.\dev.ps1                                   # from the repository root: runs the https profile, restarts on exit code 42
dotnet run --project src/Seneschal.Host     # plain run
```

Default addresses (`Properties/launchSettings.json`): `https://localhost:7228` and `http://localhost:5293`.

## Layout

```
Core/    extension loading: manifest, package, load context, registry, settings, paths
Dev/     the Developer page's engine: repo detection, scaffolder, solution file edits, build, restart
Shell/   the UI: layout, sidebar, search, tool and group pages, dialogs, theme
  Pages/ Home, GroupPage, ToolHost, ExtensionManager, Developer, Tutorial, Error, NotFound
wwwroot/ app.css (theme variables and shared styles), the logo (seneschal.png) and the icons (favicon.ico and favicon.png)
seneschal.ico  the icon of Seneschal.exe (also built into the project as its ApplicationIcon)
```

## Startup

`Program.cs` builds in this order, which matters:

1. `AddExtensionManagement()` and `AddExtensions(env)` load every extension that is present, **before the container is built**, so an extension can register its own services through `IToolLifecycle.ConfigureServices`.
2. The container is built.
3. `DllExtensions.LoadAll()` activates what was loaded: it registers the tools and stylesheets.
4. `StartTools()` calls `IToolLifecycle.Started` on each tool.

An extension that registered no services can also be imported, updated or removed while the app runs. One with services waits for a restart, and the Extension Manager says so.

## Where extensions come from

| Source | Location | Notes |
| --- | --- | --- |
| Bundled | `extensions/<id>/` next to the app | Built with the app. The `BundledExtension` items in `Seneschal.Host.csproj` list them and copy them from `artifacts/extensions` on build and publish |
| Dev | the folders in `SENESCHAL_DEV_EXTENSIONS` (separated by `;`) | In Development the repository's `artifacts/extensions` is added without the variable. A dev build wins over a bundled copy of the same id |
| Installed | `%AppData%/Seneschal/extensions` | Imported by the user. Only these can be removed or replaced from the Extension Manager |

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
| `ASPNETCORE_ENVIRONMENT` | `Development` enables the Developer page (together with a repo checkout) |

## The Developer page

Available only in the Development environment when the app finds `Seneschal.slnx` above its folder (`DevMode`).

- **Create New Extension** (`Scaffolder`) writes a complete project into `extensions/<Name>` and adds it to the solution.
- **Build** (`Workspace`) runs `dotnet build`, shows the compiler errors and loads the result into the running app, live or after a restart.
- **Delete** moves the project to `.deleted/extensions/` at the repository root, removes it from the solution and deletes its build output.
  The dialog lists what will happen. A **built-in** extension (one the host project bundles, such as Dev Environment) can be deleted too, but only after typing `seneschal` to confirm: besides the steps above it is removed from the `BundledExtension` items of `Seneschal.Host.csproj` (otherwise the next build would look for a project that is gone) and the copy built next to the app is deleted. Restart Seneschal afterwards. Move the project back from `.deleted/extensions` and add the `BundledExtension` line again to restore it.

Only a direct child folder of `extensions/` that contains a project is ever touched, whatever name a caller passes.

## Running hidden (an installed app)

The same `Seneschal.exe` is the app and the command. Given a command (`open`, `start`, `stop`, `restart`, `status`, `logs`, `version`) it does that and ends (`Cli/EngineCli.cs`); with no command from source (`dotnet run`, `dev.ps1`) it serves as before.

- `seneschal start` runs a second copy of itself as the **engine**: `Seneschal.exe --engine=true --urls=http://localhost:5380 --environment=Production` (the next free port when 5380 is taken, `SENESCHAL_PORT` to choose), started through the shell so it inherits nothing from the caller (a script that reads the output of `seneschal start` would otherwise wait for the engine to end), no window, and its output in `%AppData%/Seneschal/logs/seneschal.log` (over 5 MB it moves to `.old`). `Cli/EngineMode.cs`
- The engine writes `engine.json` (process id, port, a secret, version) and removes it when it stops. The commands read it and talk to `GET /_engine/ping` and `POST /_engine/quit`, which only answer with the secret in the `X-Seneschal-Engine` header.
- **Restart** in the app starts a new copy that waits for the old one to end first (`--wait-pid=<id>`), because nothing supervises the engine. From source it still exits with code 42 for `dev.ps1`.
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

## Extension templates

`Dev/Scaffolder.cs` makes new extension projects. `Create` is for the Developer page (inside this repository, added to the solution). `CreateStandalone` is for everyone else: a project that builds from the `Seneschal.Sdk` package, used by `seneschal extension new` and by the assistant tool `seneschal_extension_new`. `seneschal_extension_guide` gives the assistant the guide to follow.

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
