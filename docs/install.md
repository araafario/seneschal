# Install guide

Seneschal installs for the current user only, so it needs no administrator rights. Windows only for now (x64 and arm64).

## Install or update

In PowerShell:

```powershell
irm https://raw.githubusercontent.com/araafario/seneschal/main/install.ps1 | iex
```

From a Command Prompt, run `install.cmd` from the repository, or download it and run it. Both do the same thing.

It downloads the newest release, checks its SHA-256, and puts it in `%LOCALAPPDATA%\Programs\Seneschal`. It adds that folder to your PATH and a **Seneschal** shortcut to the Start menu. Running it again updates an existing install: it stops Seneschal, replaces the files and leaves your data alone.

### Options

| Option | What it does |
| --- | --- |
| `-Version 1.0.5` | Install that release instead of the newest |
| `-InstallDir <folder>` | Install somewhere else |
| `-Zip <file>` | Install from a zip you already have, without downloading |
| `-NoPath` | Do not add the folder to PATH |
| `-NoShortcut` | Do not add the Start menu shortcut |
| `-Desktop` | Also add a shortcut on the desktop |
| `-Uninstall` | Remove Seneschal, its PATH entry and its shortcuts |
| `-RemoveData` | With `-Uninstall`, also delete your data (see below) |

When you pipe the script into `iex`, options cannot be passed on the line. Use environment variables instead (`SENESCHAL_VERSION`, `SENESCHAL_INSTALL_DIR`), or run it like this:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/araafario/seneschal/main/install.ps1))) -Version 1.0.5 -Desktop
```

## Opening Seneschal

Seneschal runs in the background with no window of its own. Open it with:

- the **Start menu** shortcut (pin it to the taskbar if you like), or
- the `seneschal` command in a terminal.

Both start it first when it is not running, then show it in an app window without tabs, when Edge or Chrome is installed. Set `SENESCHAL_OPEN=tab` to get a normal browser tab instead.

## The `seneschal` command

```
seneschal            start it if needed and open it
seneschal start      start it in the background
seneschal stop       stop it
seneschal restart    stop it and start it again
seneschal status     is it running, and where
seneschal logs       the end of the log
seneschal version    the version
seneschal update     look for a new version and install it
seneschal extension new <Name> [folder]   start a new extension project
```

Projects and containers Seneschal started keep running when it stops, and show up again as running when it starts. Inside the app, **Quit** is at the bottom of the sidebar and **Restart App** is in the Extension Manager.

## Updates

An installed Seneschal looks for a new release every 3 days while it runs.

- **Same major version** (1.2.0 to 1.3.0, or 1.2.1): it downloads the release by itself, checks its SHA-256, and installs it the next time Seneschal restarts. A banner says **Update 1.3.0 is ready** with a **Restart App** button, or just wait: `seneschal restart` or the next start installs it.
- **New major version** (1.x to 2.0): nothing is installed by itself, because it can break things. The banner says **Major Update Available** and links to the release notes. Run `seneschal update` when you are ready.

```
seneschal update            look for a new version and install it, whatever its number
seneschal update --check    only look
seneschal update auto off   only announce updates (on turns it back on)
```

Your data and settings are never touched. Only a copy made by the installer updates itself, not one built from source. `SENESCHAL_UPDATE_DAYS` changes how often it looks.

## Where things are

| What | Where |
| --- | --- |
| The program | `%LOCALAPPDATA%\Programs\Seneschal` |
| Your data: projects, startups, settings, chat history, imported extensions | `%AppData%\Seneschal` |
| The log | `%AppData%\Seneschal\logs\seneschal.log` |
| The address | `http://localhost:5380`, or the next free port when that one is taken (`seneschal status` tells you) |

Set `SENESCHAL_PORT` to choose a different port, and `SENESCHAL_DATA_DIR` to keep the data somewhere else.

## Uninstall

```powershell
.\install.ps1 -Uninstall            # keeps your data
.\install.ps1 -Uninstall -RemoveData
```

## When something does not start

- Run `seneschal logs` and read the end of it. The address is also in `seneschal status`.
- **The page has no styling and shows a huge icon.** You started `Seneschal.exe` from a build folder of an old version. Install a release, or rebuild from the current source.
- **A message box says it did not start, or that `Seneschal.exe` was not found.** It shows the reason. `SeneschalApp.exe` has to sit in the same folder as `Seneschal.exe`, as it does in an install. Run from a checkout, it looks for the `Seneschal.exe` you built last under `src/Seneschal.Host/bin`, so build the app first. The same reasons are in the log.
- **`seneschal` is not recognised.** Open a new terminal: PATH changes only reach terminals started after the install.
