# Developing Seneschal

For people who want to change Seneschal itself or its bundled extensions. To just use it, see the [install guide](install.md).

## Layout

```
Seneschal.slnx
dev.ps1                     run from source; restarts the app on exit code 42
install.ps1, install.cmd    the installer
version.txt                 major.minor of the app version
src/
  Seneschal.Sdk/            what an extension compiles against
  Seneschal.Host/           the app: shell, sidebar, Extension Manager, Developer page, assistant, MCP endpoint, the seneschal command
  Seneschal.Launcher/       SeneschalApp.exe, what the Start menu shortcut starts (no console window)
extensions/
  DevEnvironment/           Projects (Docker, .NET, microfrontends, Smart Startup) and Logs
docs/                       these guides and ui-guidelines.md
assets/branding/            the logo and the icon
artifacts/                  build output only, never source
.github/workflows/          ci.yml and release.yml
```

## Run from source

```powershell
.\dev.ps1
```

The app project references the extensions of this repo, so building the app builds them. `dev.ps1` also loads their staged builds from `artifacts/extensions` (shown as **Dev** in the Extension Manager) and restarts the app when it exits with code 42, which is what the **Restart App** button does.

```powershell
dotnet build Seneschal.slnx
```

A copy of Seneschal that runs from the build folder (`seneschal start` on a `bin\Release` build) is stopped for you before the next build of the app, so it does not lock the files being replaced. A copy running from somewhere else, such as an installed one, is left alone, and so is a Seneschal started by `dev.ps1`: stop that one before a full build, or build one extension alone with `dotnet build extensions/DevEnvironment`.

## The Developer page

When the app runs from a checkout of this repo (Development environment), the **Developer** page manages `extensions/`:

- **New Extension** creates a complete project, adds it to `Seneschal.slnx` and can build and load it.
- **Build** runs `dotnet build`, shows compiler errors and loads the result live.
- **Delete** asks first, then moves the project to `.deleted/extensions/`, removes it from the solution and unloads it.

## Working safely

Test anything destructive or anything that changes the repo **on a copy**, never on the real one: the Developer page, moving folders, edits to `Seneschal.slnx` or `Seneschal.Host.csproj`. Run the copy on its own port with `SENESCHAL_DATA_DIR` pointing at a scratch folder. Use throwaway containers and projects for runner tests, and never start, stop or kill your own services or your own running Seneschal.

## The UI

Any change to a page or a stylesheet follows the [UI guidelines](ui-guidelines.md): wording, spacing, color, components, and a checklist. Check it in dark and light mode.

## Every feature is a tool for the assistant

When a page gains a feature, its tool gains an `IToolMcp` entry in the same change: list, create, delete, run, reorder. Name tools `<tool>_<verb>`, and mark anything that removes or overwrites as `Destructive`. See [Building extensions](extensions.md).

## Releasing

Pushing releases nothing. On GitHub open **Actions**, **Release**, **Run workflow**.

- Leave the version empty to get `version.txt` plus the run number (`1.0.37`), or type one (`1.2.0`, or `1.2.0-beta.1` for a pre-release).
- The workflow builds for Windows x64 and arm64 with .NET included, tags the commit, and attaches the zips, `SHA256SUMS.txt`, `install.ps1` and `install.cmd` to a GitHub Release.
- The **CI** workflow only builds, on every push and pull request.

The app version is passed to the build as `-p:AppVersion=...` and shows in `seneschal version`. A build on a developer machine says `0.0.0-dev`. Change `version.txt` for a new major or minor. The SDK keeps its own version in its project file.

## Publishing the SDK

`Seneschal.Sdk` is on nuget.org so extensions can live in their own repositories. Pushing it is a manual workflow, **Actions**, **Publish SDK**, which uses nuget.org **trusted publishing**: there is no API key to keep. One time setup:

1. On nuget.org, open your profile, **Trusted Publishing**, and add a policy: the repository owner and name as on GitHub, the workflow file `publish-sdk.yml`, and no environment.
2. In the repository settings, add the secret `NUGET_USER` with your nuget.org user name (the profile name, not the email address).

Then run the workflow. Leave the version empty to use `<Version>` from `src/Seneschal.Sdk/Seneschal.Sdk.csproj`, or type one (`1.2.0-beta.1` for a pre-release). A version can only be pushed once, so raise it in the project file first. The workflow checks that the build rules are inside the package before it pushes, and keeps the package as a download of the run. Untick **Publish** to only build it.

The SDK version follows its own rule, not the app's: a major number for a breaking change, a minor number when something is added.

## How the installed app runs

The same `Seneschal.exe` is the app and the `seneschal` command. See [the app's README](../src/Seneschal.Host/README.md) for how the hidden background copy, its restart and its shutdown work.
