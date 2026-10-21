# Developing Seneschal

For people who want to change Seneschal itself or its bundled extensions. To just use it, see the [install guide](install.md).

## Layout

```
Seneschal.slnx
dev.ps1                     build and run from source through the launcher, like an installed copy
install.ps1, install.cmd    the installer
version.txt                 the lowest major.minor a release can have
src/
  Seneschal.Sdk/            what an extension compiles against
  Seneschal.Host/           the app: shell, sidebar, Options, assistant, MCP endpoint, the seneschal command
  Seneschal.Launcher/       SeneschalApp.exe, what the Start menu shortcut starts (no console window)
extensions/                 nothing ships or is made here; projects live in Documents/Seneschal/Projects
docs/                       these guides and ui-guidelines.md
assets/branding/            the logo and the icon
artifacts/                  build output only, never source
.github/workflows/          ci.yml and release.yml
```

## Run from source

```powershell
.\dev.ps1
```

The app project references the extensions of this repo, so building the app builds them. `dev.ps1` builds the app, which also builds the launcher (`SeneschalApp.exe`) into the same folder, then starts it through the launcher, hidden, exactly as an installed copy starts: control it with the `seneschal` command from that folder (`seneschal logs`, `stop`, `restart`), and **Restart App** works by itself. A copy started from a checkout runs as Development, so it has Options, Extension Projects and loads the staged builds in `artifacts/extensions` (shown as **Dev** in Options, Extensions). `dev.ps1 -Attached` runs it in the window instead (`dotnet run`) and starts it again when it exits with code 42.

```powershell
dotnet build Seneschal.slnx
```

Any Seneschal that runs from the folder a build writes to (`bin\Debug\net10.0` or `bin\Release\net10.0`: one started with `seneschal start`, `dev.ps1` or `dotnet watch`) is stopped before the next build of the app (`StopRunningCopy` in `Seneschal.Host.csproj`), so it does not lock the files being replaced. A copy running from somewhere else, such as an installed one, is left alone. So a build you only want to try (a script, an assistant) must not be of the checkout you are running: build a copy of it, or build one extension alone.

**Visual Studio.** The first profile, **Seneschal (hot reload)**, is the one F5 runs (or run `.\dev.ps1 -Watch`): it starts `dotnet watch` with hot reload, so Razor, CSS and most C# edits apply without a restart and the rest rebuilds and restarts by itself. The `http` and `https` profiles run `bin\Debug\net10.0\Seneschal.exe`, which holds the build files open until you stop debugging.

## Options, Extension Projects

**Options, Extension Projects** is for whoever makes extensions (it shows when the .NET SDK is installed or the app runs from source). Live Reload lives there: a project builds into its own `artifacts\extensions` folder, Seneschal watches that folder and loads each build the moment it finishes, with no import and no restart.

- **New Project** creates a complete project in the projects folder (`Documents/Seneschal/Projects`, changeable in Options, Extension Projects) and watches it. Every project in the projects folder is listed and watched by itself, with nothing to add: a project from somewhere else is added by hand with **Add Project**. Extensions you installed (a `.snx`, such as LoopUp) are not projects: they are under **Options, Extensions**. It is never made inside this repo, and it builds against the SDK copy in `.seneschal/` of that folder.
- **Add Project** watches a project you already have.
- **Build** runs `dotnet build`, shows compiler errors and loads the result live. **Remove Build** unloads it.
- **Delete Project** (projects already committed in this repo only) asks first, then moves the project to `.deleted/extensions/`, removes it from the solution and unloads it.

## Working safely

Test anything destructive or anything that changes the repo **on a copy**, never on the real one: Options, Extension Projects, moving folders, edits to `Seneschal.slnx` or `Seneschal.Host.csproj`. Run the copy on its own port with `SENESCHAL_DATA_DIR` pointing at a scratch folder. Use throwaway containers and projects for runner tests, and never start, stop or kill your own services or your own running Seneschal.

## The UI

Any change to a page or a stylesheet follows the [UI guidelines](ui-guidelines.md): wording, spacing, color, components, and a checklist. Check it in dark and light mode.

## Every feature is a tool for the assistant

When a page gains a feature, its tool gains an `IToolMcp` entry in the same change: list, create, delete, run, reorder. Name tools `<tool>_<verb>`, and mark anything that removes or overwrites as `Destructive`. See [Building extensions](extensions.md).

## Releasing

Pushing releases nothing. On GitHub open **Actions**, **Release**, **Run workflow**.

- Choose the kind of release: **fix** (`1.0.4` to `1.0.5`), **normal** (to `1.1.0`) or **breaking** (to `2.0.0`). The number is counted from the latest `v*` release tag, so no file is changed or committed for a release. The first release is `1.0.0`.
- `version.txt` is the lowest a release can be. Raise it (`2.0`) to start a new major or minor line.
- Type a version (`1.2.0`) only to set the number yourself. Tick **pre-release** for `-beta.<run number>` on the version, marked as a pre-release on GitHub. Pre-releases do not count when the next number is worked out.
- The workflow builds for Windows x64 and arm64 with .NET included, tags the commit, and attaches the zips, `SHA256SUMS.txt`, `install.ps1` and `install.cmd` to a GitHub Release.
- Before it publishes anything it opens each zip and stops if the app, its launcher, the SDK or `wwwroot` is missing.
- A release is made from the commit the workflow is started on, so commit and push what you want in it first.
- The **CI** workflow builds, packs the SDK and checks the package, on every push and pull request. It publishes nothing.

The app version is passed to the build as `-p:AppVersion=...` and shows in `seneschal version`. A build on a developer machine says `0.0.0-dev`. The SDK keeps its own version in its project file.

## Publishing the SDK

`Seneschal.Sdk` is on nuget.org so extensions can live in their own repositories. Pushing it is a manual workflow, **Actions**, **Publish SDK**, which uses nuget.org **trusted publishing**: there is no API key to keep. One time setup:

1. On nuget.org, open your profile, **Trusted Publishing**, and add a policy: the repository owner and name as on GitHub, the workflow file `publish-sdk.yml`, and the environment `production` (the job uses it, and the two must match). GitHub makes the environment on the first run; add a required reviewer in **Settings, Environments, production** if a push should need approval.
2. In the repository settings, add the secret `NUGET_USER` with your nuget.org user name (the profile name, not the email address).

Then run the workflow. Leave the version empty to use `<Version>` from `src/Seneschal.Sdk/Seneschal.Sdk.csproj`, or type one (`1.2.0-beta.1` for a pre-release). A version can only be pushed once, so raise it in the project file first. The workflow checks that the build rules are inside the package before it pushes, and keeps the package as a download of the run. Untick **Publish** to only build it.

The SDK version follows its own rule, not the app's: a major number for a breaking change, a minor number when something is added.

## How the installed app runs

The same `Seneschal.exe` is the app and the `seneschal` command. See [the app's README](../src/Seneschal.Host/README.md) for how the hidden background copy, its restart and its shutdown work.
