# Seneschal

A local daily helper: a Blazor Web App (interactive server, MudBlazor, .NET 10) with Claude and an MCP server built in, whose tools are all **extensions** that can be added or removed.
The frame (shell, Options, assistant, MCP, `seneschal` command, updates) is general and ships with no extension. Nothing in it may depend on a particular extension.
Tools live in their own repositories as packages (`.snx`) built against the `Seneschal.Sdk` NuGet package. The Dev Environment tools (Projects, Logs, Token) are in the separate LoopUp extension repository, not here.

## Read first

- **UI work follows [docs/ui-guidelines.md](docs/ui-guidelines.md)**: wording, spacing, color, components and a checklist. Read it before touching any `.razor` or `.css` file.
- Module docs: `src/Seneschal.Host/README.md` (the app), `src/Seneschal.Sdk/README.md` (what extensions build against).

## UI rules in short

- All text is short and humanized: no trailing `.` `,` `;` `!` or `...`. Buttons are verbs. Say a thing once. Do not explain an obvious button.
- Title Case for page titles, section headers, dialog titles and text buttons. Sentence case for everything else.
- A banner is for a state that needs attention. Success of a visible result shows nothing, a long action shows a spinner then a short toast, a failure is a toast that stays with a Details popup. Buttons that act on a list sit on that list's row, at the right end.
- Loading: draw the frame first and the slow part after it, show skeletons and bars instead of blank gaps, never do slow work (disk, PATH, processes) on every redraw. The assistant panel has its own rules in the guideline.
- 4px grid, radius 6 for cards and 4 for controls, colors only from the theme variables in `app.css`. Sizes come from the density scale at the top of `app.css` (`--fs-*`, `--h-ctl`, `--gap-*`) and the theme typography, never from a number written in a component.
- Check every UI change in **dark and light** mode, and handle empty, loading, busy and error states.
- Host components (`PageShell`, `Banner`) are not available to extensions: use `MudAlert` and the shared CSS classes.

## Every feature is a tool for the AI

- A tool's features must also be MCP tools (IToolMcp on the tool class): list, create, delete, run, reorder. When you add a feature to the page, add its tool in the same change. See the SDK README.
- Anything that removes or overwrites uses McpEffect.Destructive, which asks the user first. Name tools <tool>_<verb>.
- The assistant starts the local Claude Code with only Seneschal's tools. Test it on a copy of the repo, not on the real data.

## Layout

```
src/Seneschal.Sdk/        what an extension compiles against
src/Seneschal.Host/       the app: shell, Options (with Projects for extension makers), loader
src/Seneschal.Launcher/   SeneschalApp.exe: what the shortcuts start (no console window)
extensions/                Only the build files for an extension made in Options, Projects. Nothing ships here
docs/                     ui-guidelines.md
assets/branding/          logo and icon
artifacts/                build output only
```

## Build and run

```powershell
.\dev.ps1                      # build, then run it through the launcher like an installed copy (hidden; -Attached runs it in this window)
dotnet build Seneschal.slnx    # build everything
```

- A running Seneschal locks `Seneschal.exe` and the staged extension files. Stop it before a full build.
- Every extension loads live, with or without services: each has a container of its own that is replaced when it is rebuilt, imported or removed. No app restart.

## Working safely

- **Test destructive or repo-changing features on a copy of the repo**, never the real one. That means Options, Projects (create, delete), anything that moves folders, and edits to `Seneschal.slnx` or `Seneschal.Host.csproj`. Run the copy of the app on its own port with `SENESCHAL_DATA_DIR` set to a scratch folder.
- Use throwaway containers and throwaway apps for runner tests. Never start, stop or kill the developer's own services, containers or running Seneschal.
- Stop a test process by matching its scratch path, never by its name.
- Options, Projects acts on whichever repo the app runs from. An extension in a `BundledExtension` item of the host project can only be deleted after typing `seneschal`.

## Conventions

- Prefer a shared component or a shared class over a new one. Keep class names for an extension's own pieces specific (a prefix such as `jd-`).
- When code and a README or the guideline disagree, fix one of them in the same change.
- Data the tools keep lives in the extension's data folder (`%AppData%/Seneschal/extension-data/<id>`), not in the repo.
