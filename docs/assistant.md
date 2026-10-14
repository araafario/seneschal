# The assistant

A button in the bottom right corner of every page opens a chat. You tell it what you want and it does it with the same tools you would click, so it can start and stop things, change the order of a startup, read a log and more.

## What it runs on

It uses the **Claude Code that is already installed and signed in on this machine**, so your Claude subscription is used and there is no key to set up. If Claude Code is missing, the panel says so and links to the install guide. If it is installed but not signed in, run `claude` once in a terminal and sign in.

Seneschal starts Claude Code with only Seneschal's own tools: it cannot read your files or run commands, only use what the app offers. Your own Claude settings, hooks and plugins are not loaded.

## Using it

An empty chat shows the Seneschal logo and four starter cards. They are requests the tools in Seneschal offer for themselves (such as *What is running right now?*), one from each tool in turn, plus *Help me create a new extension*. Press one to send it.

- Type in the box and press Enter. Shift+Enter adds a line.
- Tool calls show as one line each. Open one to see what was sent and what came back.
- Copy an answer with the button under it. Esc closes the panel.
- **History** keeps your last 30 chats. Open one to carry on, even after a restart. **New Chat** starts fresh.
- Pick the **model** from the header: Default, Opus, Sonnet or Haiku. These are Claude Code's short names, and each means the newest of its kind. Default uses whatever Claude Code is set to.

## Safety

| What it does | How it is treated |
| --- | --- |
| Looks at something (lists, status, logs) | Runs |
| Changes something that is easy to undo (start, stop, add) | Runs, or asks first when you turn on **Ask before every change** in the settings |
| Removes or overwrites something | **Always asks**, in a card in the chat. No answer in five minutes counts as no |

Passwords and tokens are never written into the saved history: if one shows up in a chat or in a tool's result, it is replaced with `[hidden]` before the chat is stored. In the live chat it still shows, and it passes through Claude like anything else you type.

## Use Seneschal from your own Claude Code

Open the assistant's settings and press **Copy Command**. Paste it into a terminal and Seneschal's tools appear in your Claude Code as well:

```
claude mcp add --transport http seneschal http://localhost:5380/mcp --header "Authorization: Bearer <secret>"
```

The tools work whenever Seneschal is running, with or without a page open. Anything that deletes still asks in the assistant panel, so keep a Seneschal tab or window open when you want that.

The endpoint only answers from this machine and only with the secret, which is made once and kept in `%AppData%\Seneschal\mcp-secret.txt`.

## Creating an extension with it

Ask *Help me create a new extension*. The assistant reads Seneschal's extension guide, asks what the tool should do and what to call it, creates the project (`seneschal_extension_new`) and walks you through building, trying and sharing it. It follows the same rules as everyone else: every feature of the page also becomes a tool for the assistant, and anything that deletes asks first.

## For extension authors

A tool becomes available to the assistant by implementing `IToolMcp` next to `IToolModule`. See [Building extensions](extensions.md) and the [SDK reference](../src/Seneschal.Sdk/README.md).

How it is built (the stream from Claude, the approvals, the MCP endpoint) is in [the app's README](../src/Seneschal.Host/README.md).
