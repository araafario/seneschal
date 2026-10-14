<p align="center">
  <img src="assets/branding/seneschal.png" width="150" alt="Seneschal">
</p>

<h1 align="center">Seneschal</h1>

<p align="center">
  <b>The steward of your development machine.</b><br>
  Start your stack, follow your logs, or just ask.
</p>

<p align="center">
  <a href="https://github.com/araafario/seneschal/releases"><img alt="Release" src="https://img.shields.io/github/v/release/araafario/seneschal?include_prereleases&label=release&color=1f4fa3"></a>
  <a href="https://github.com/araafario/seneschal/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/araafario/seneschal/actions/workflows/ci.yml/badge.svg"></a>
  <img alt="Windows" src="https://img.shields.io/badge/platform-Windows-0078D4">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4">
</p>

<p align="center">
  <a href="#install">Install</a> &nbsp;·&nbsp;
  <a href="#what-you-can-do">What you can do</a> &nbsp;·&nbsp;
  <a href="#ask-it">Ask it</a> &nbsp;·&nbsp;
  <a href="#make-it-yours">Extensions</a> &nbsp;·&nbsp;
  <a href="#documentation">Docs</a>
</p>

<p align="center">
  <img src="docs/images/hero.png" alt="Seneschal with its assistant open" width="860">
</p>

## What is Seneschal

A seneschal is the steward of a great household: the one who knows where everything is and keeps it all running.

Seneschal does that for your computer. It is a small app that lives on your machine and looks after the things you start every morning: Docker containers, .NET projects and front ends. It brings them up in the right order, shows you what is running, and follows their logs. When you would rather not click, you ask the assistant, and it does it for you.

Seneschal itself runs **locally**: your projects and settings stay on your machine. The assistant talks to Claude through your own Claude Code, so what you ask it is handled like any other Claude Code chat.

## What you can do

| | |
| --- | --- |
| **Run your stack** | Start and stop Docker containers, .NET projects (even several copies of one) and microfrontends from one list, and see what is up at a glance |
| **Start it in order** | A Smart Startup is a plan: the database first, then the API once it is ready, then the UI. Seneschal waits for each step before the next |
| **Follow the logs** | Watch several logs side by side, stacked or in a grid, and filter the lines |
| **Ask for it** | A built-in assistant, powered by the Claude you already have on this machine, uses every feature above |
| **Make it yours** | Add your own tools as extensions, in a few lines of C# |

## Install

On Windows, in PowerShell:

```powershell
irm https://raw.githubusercontent.com/araafario/seneschal/main/install.ps1 | iex
```

That installs it for you alone, with no administrator rights. Then open **Seneschal** from the Start menu, or type `seneschal` in a terminal.

Seneschal runs quietly in the background. There is no console window to keep open, and `seneschal stop` (or **Quit** in the sidebar) closes it. More about installing, updating and removing it is in the [install guide](docs/install.md).

## Ask it

The assistant sits in the corner of every page. Click it and say what you want:

- *Start my usual morning stack*
- *What is running right now, and what is not?*
- *Show me the last errors in the gateway log*
- *Stop everything that is running*

It works through the same tools you click, so it can do anything you can, and it **asks before it deletes anything**. It uses the Claude Code already signed in on your machine, so there is no extra account or key to set up. See [the assistant guide](docs/assistant.md) for how it works, the choice of model, and using Seneschal from your own Claude Code.

## Make it yours

Every tool in Seneschal is an extension, including the ones that ship with it. Writing one is a class, a Blazor page and a manifest. The Developer page can create a complete project for you, build it, and load it live. Your tool is available to the assistant too, with one more method.

Start with [the extension guide](docs/extensions.md), or open the tutorial inside the app.

## Documentation

| | |
| --- | --- |
| [Install guide](docs/install.md) | Installing, updating, removing, the `seneschal` command, and when something does not start |
| [The assistant](docs/assistant.md) | How it works, models, history, safety, and using Seneschal from your own Claude Code |
| [Building extensions](docs/extensions.md) | What an extension is, where Seneschal finds them, and where to learn more |
| [Developing Seneschal](docs/development.md) | Running from source, the project layout, testing safely, and making a release |
| [UI guidelines](docs/ui-guidelines.md) | How everything looks, reads and behaves |
| [Extension SDK](src/Seneschal.Sdk/README.md) &nbsp;·&nbsp; [The app](src/Seneschal.Host/README.md) &nbsp;·&nbsp; [Dev Environment](extensions/DevEnvironment/README.md) | Module references |

<br>

<p align="center">
  <sub>Made for people who start the same ten things every morning. Windows first, built with .NET 10 and Blazor.</sub>
</p>
