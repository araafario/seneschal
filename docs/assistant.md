# The assistant

A button in the bottom right corner of every page opens a chat. You tell it what you want and it does it with the same tools you would click, so it can start and stop things, change the order of a startup, read a log and more.

## What it runs on

It uses the **Claude Code that is already installed and signed in on this machine**, so your Claude subscription is used and there is no key to set up. If Claude Code is missing, the panel says so and links to the install guide. If it is installed but not signed in, run `claude` once in a terminal and sign in.

Seneschal starts Claude Code with only Seneschal's own tools: it cannot read your files or run commands, only use what the app offers. Your own Claude settings, hooks and plugins are not loaded.

## Who it is

The assistant knows it lives inside Seneschal: it introduces itself as the Seneschal, the steward of your own Seneschal, and answers briefly, calmly and in plain words. Before every message it is told what Seneschal is, how its tools reach it over MCP (what Read, Change and Destructive mean, and that Seneschal itself asks you before anything destructive), the tools that are turned on right now and what they do, whether it can make extensions (the .NET SDK), the date and the MCP address, and to send you to Options, MCP when you want to connect another AI. It is rebuilt for every message, so turning a tool off or importing an extension shows in its next answer. The text is in `Assistant/AssistantPrompt.cs`.

## Using it

An empty chat shows the Seneschal logo and four starter cards. The first is always *Help me create a new extension*, so even a Seneschal with no tools has something to start with. The others are requests the tools in Seneschal offer for themselves (such as *What is running right now?*), one from each tool in turn. Press one to send it.

- Type in the box and press Enter. Shift+Enter adds a line.
- Tool calls show as one line each. Open one to see what was sent and what came back.
- Copy an answer with the button under it. Esc closes the panel.
- When there are a few clear options, it asks with a card: the options as rows, a box for your own answer, and **Skip**. A single choice is answered by the click, several need **Send**.
- **History** keeps your last 30 chats. Open one to carry on, even after a restart. **New Chat** starts fresh.
- Pick the **model** from the header: Default, Opus, Sonnet or Haiku. These are Claude Code's short names, and each means the newest of its kind. Default uses whatever Claude Code is set to.

## Safety

| What it does | How it is treated |
| --- | --- |
| Looks at something (lists, status, logs) | Runs |
| Changes something that is easy to undo (start, stop, add) | Runs, or asks first when you turn on **Ask before every change** in the settings |
| Removes or overwrites something | **Always asks**, in a card in the chat. No answer in five minutes counts as no. **Options, Assistant, Always allow actions** turns every question off, this one included, for every MCP client |

Passwords and tokens are never written into the saved history: if one shows up in a chat or in a tool's result, it is replaced with `[hidden]` before the chat is stored. In the live chat it still shows, and it passes through Claude like anything else you type.

## Use Seneschal from your own AI

Open **Options, MCP**. It shows the address and the secret, and has a ready setup to copy for **Claude Code**, **Cursor**, **VS Code**, **Claude Desktop** and any other client that speaks MCP, the list of every tool offered, the setting that makes changes ask first (for every client), and **Reset** to make a new secret (every client that had the old one stops working until it gets the new setup). For Claude Code, **Copy Command** gives:

```
claude mcp add --transport http seneschal http://localhost:5380/mcp --header "Authorization: Bearer <secret>"
```

The tools work whenever Seneschal is running, with or without a page open. Anything that deletes still asks in the assistant panel, so keep a Seneschal tab or window open when you want that.

The endpoint only answers from this machine and only with the secret, which is made once and kept in `%AppData%\Seneschal\mcp-secret.txt`.

## Without Claude Code

The built-in assistant needs Claude Code on this machine. When it is not found, the panel says so and still shows how to connect any other MCP client (Claude Desktop, Cursor, VS Code and so on): **Copy Config** gives the JSON, **Copy Command** the `claude mcp add` line. The full list is in Options, MCP. Every tool in Seneschal works there too, whenever Seneschal is running. Install Claude Code later and the panel works without any setup.

## Creating an extension with it

Ask *Help me create a new extension*. It needs the .NET SDK (the installer adds it, see the [install guide](install.md)). Without it the card is still there, and the assistant tells you to run `seneschal dev install`.

The assistant reads Seneschal's extension guide and the design rules, then asks what the tool should do, whether Claude should be able to use it (MCP, the default), what it must remember, whether it needs services of its own and what it may touch. Then it works with these tools:

| Tool | What it does |
| --- | --- |
| `seneschal_ask_user` | Asks you to choose: a card in the panel with the options as rows (and a box for your own answer), and the tool waits for it. You can skip |
| `seneschal_instructions_list`, `seneschal_instruction_read`, `seneschal_instruction_update` | Lets it read and change the texts that shape it, its character included. Changing one always asks you first |
| `seneschal_lesson_add` | Writes down one line it learned from a correction. Lessons are sent with every message and you can edit them in **Options, Instructions** |
| `seneschal_settings_reset` | Puts the theme, sidebar, icons, favorites and assistant settings back to the defaults at once. Extensions, projects and chats stay. It always asks you first. The same as **Options, About, Reset Settings** |
| `seneschal_extension_guide`, `seneschal_ui_guidelines`, `seneschal_sdk_reference` | What it reads first: the baseline and the test strategy, the design rules and the SDK |
| `seneschal_extension_create_from_template` | Creates the starting project from Seneschal's template. The only step that needs Seneschal: after it, development is ordinary, so an AI that can edit files and run `dotnet` (your own Claude Code) just does that |
| `seneschal_extension_check` | Checks the project against the design rules and the SDK without changing it, and returns each finding with its file and line |
| `seneschal_extension_preview` | Opens the page in a browser, in dark and light, and returns a screenshot of each plus what it found: console errors, failed requests, content wider than the window, the text of the page |
| `seneschal_extension_files`, `seneschal_extension_read`, `seneschal_extension_write` | For an assistant without file access (the built-in one): reads and writes files, only inside that one project and only text files |
| `seneschal_extension_build` | For an assistant without a terminal: builds and loads it. It runs the code that was written, so it always asks you first. A compile error goes back to the assistant, which fixes it and builds again |

Projects are kept in `Documents\Seneschal\Projects` (change it in **Options, Projects**, or with `SENESCHAL_EXTENSIONS_DIR`), whether Seneschal is installed or run from source, and never in the Seneschal repository. A build is loaded live. They build against the copy of the Seneschal SDK that the app itself uses, so there is nothing to install. Every feature of the page also becomes a tool for the assistant, and anything that deletes asks first.

An extension runs with the same access to your machine as you have, and the assistant writes it, so read what it made before you trust it. Its files are in the project folder.

### How it tests what it made

The guide it follows ends with a definition of done, so it does not say "it works" because the code looks right. In order: a build with no errors or warnings, `seneschal_extension_check` clean, the extension loaded without a failure, every one of the extension's own tools used with a normal call and a bad call (a change is done on made-up data and undone, a delete only after asking you), saved state still there after a rebuild, the engine tried with edge input (empty, very long, not English), the page looked at in dark and light with `seneschal_extension_preview`, and the design checklist gone through. It then tells you what it tested and what only you can: clicking through the main action with real data.

The preview runs a headless Edge or Chrome, which is the one already on the machine. In **Options, Assistant, Testing**, **Show the test browser** shows its window while it works, off by default.

## Changing what it is told

**Options, Instructions** lists the texts the assistant follows. Each has **Edit**, and **Reset to default** brings back the one that ships with Seneschal:

| Text | What it is |
| --- | --- |
| Character | Who it is and how it talks. Sent with every message |
| Lessons | What it learned from being corrected, one dated line each. Sent with every message |
| Extension Guide | What it asks, builds and tests when you ask it to make an extension. `{{build}}` and `{{root}}` are filled in when it is read |
| UI Guidelines | The design rules it follows for the pages it makes |
| SDK Reference | The types an extension can use |

A changed text is kept in `%AppData%\Seneschal\assistant\instructions\<name>.md` and used from the next message, with no restart. **Options, Assistant** sets the Claude path, the model and whether the test browser is shown.

## It learns

When you correct it, or it notices a mistake of its own, the assistant calls `seneschal_lesson_add` with one short line: what to do or not do, and why. Lessons are sent with every message from then on, so the same mistake is not made twice. You can read, edit or clear them in **Options, Instructions**. It never writes a secret into a lesson.

It can also read and change its own texts, its character included, with `seneschal_instruction_read` and `seneschal_instruction_update`. Changing one is marked destructive, so you are asked first.

## For extension authors

A tool becomes available to the assistant by implementing `IToolMcp` next to `IToolModule`. See [Building extensions](extensions.md) and the [SDK reference](../src/Seneschal.Sdk/README.md).

How it is built (the stream from Claude, the approvals, the MCP endpoint) is in [the app's README](../src/Seneschal.Host/README.md).
