# Behavior test plan

Goal: every behavior Seneschal promises is exercised once, on a copy of the app (own port, scratch data folder, `SENESCHAL_NO_SHELL=1`), never on the real data or the running app. Each case has a result: pass, fail (fixed, then pass), or not testable here with the reason.

Tools: Playwright for pages, `curl`-style calls to `/mcp` for MCP, the `seneschal` command for the CLI, files in the scratch data folder for persistence.

## 1. Start, stop, command line
- `seneschal start` starts hidden, `status` shows the address and process, `stop` ends it, `restart` starts a new one, `logs` shows output, `version`
- Starting twice does not start a second engine
- Port already in use is reported
- `seneschal dev` shows the SDK state, `dev install` is not run here (changes the machine)

## 2. Shell
- Home lists tools, hides tools that are turned off, shows the empty state with no extensions
- Sidebar: groups expand and collapse and remember it, rail mode, current page mark, long names cut
- Search finds by title and description, no match state, keyboard open and close
- Theme toggle switches and remembers, `?theme=` override
- Unknown URL gives the not found page, an exception gives the error page
- Reconnect modal after the engine restarts, page recovers

## 3. Extensions
- Import a `.snx`: loads live, services extension loads without restart, wrong file is refused with a reason, same id twice replaces
- Turn a tool off and on: sidebar, Home and search follow, MCP tools of it disappear and come back
- Remove an extension: asks first, files gone, tools gone
- Needs attention group shows a failed extension with Details
- Filter and chips: All, Built-in, Installed, Disabled

## 4. Appearance
- Change a tool icon and a group icon, reset, persists across restart (`icons.json`)
- Font: pick a font and *Other*, the text changes everywhere, the default is labeled, persists across restart
- Default and Custom badges are right

## 5. Extension Projects (Developer)
- Section hidden without SDK or dev mode, shown with
- New Project creates, shows, builds, loads live, tool appears; with *Create a git repository* on it has a first commit, with it off it has no `.git`
- Add Project: *Browse* opens the native folder picker (Windows), a folder with no project or a missing one is refused with a reason
- Add Project of an existing folder, folder that does not exist yet, folder with no project
- Build: success loads, compile error is shown with details and nothing breaks, build again after a fix replaces it
- Live Reload off: a build is not loaded, on again: it is
- Remove Build, Remove Project (stop watching), Delete Project (repo only) asks first and moves to `.deleted`
- Open folder, open solution, reveal: actions are offered (not executed in the test)

## 6. Assistant panel
- Opens, closes, remembers nothing it should not, `Esc`
- Claude not found state offers Copy Config and Copy Command
- With a fake `claude` that streams: message appears, tool call rows, result, stop button, error
- Starter cards: first is always Help me create a new extension, others come from tools
- Approval card: Allow and Deny both work, destructive variant, no time limit, closing it counts as no, shows with the panel closed (launcher badge)
- Question card: single answers on click, multiple needs Send, own answer, Skip
- Question modes (Options, Assistant, Questions): wait for my answer, wait then use the recommended answer (countdown, *Recommended* tag, the assistant is told it was automatic), use the recommended answer at once
- Panel position: floating and sidebar, width by drag is remembered, the page scrolls sideways in the middle when docked and narrow
- Pictures: paste, drop, *Add image*, a fifth is refused with a note, remove with the ✕, a message of only pictures, history keeps small copies
- Snip: the app's own window is captured with nothing to choose (`POST /assistant/snip` needs the `X-Seneschal` header and a local, same-origin request, and gives 204 when it cannot), the crop overlay keeps a selection or the whole window, the browser's capture is the fallback
- Picture viewer: opens from the chat and from the tiles, fit and actual size, arrows and count for the pictures of one message, Esc and outside click close it and focus returns, a history chat shows the small copy with a note
- History: list, open, delete, 30 limit
- Model select persists
- Long messages and code blocks wrap or scroll inside

## 7. MCP server
- `initialize`, `tools/list` (every tool has a schema and description), `tools/call` for each Read tool
- No secret or a wrong secret is refused, local only
- Reset secret changes it and the old one stops working
- Approvals: a Destructive tool asks, with Ask before every change on a Change tool asks too
- Image content (`seneschal_extension_preview`) returns PNGs
- Tool naming: `<tool>_<verb>`, all unique
- Instruction tools: list, read, update (asks), lesson add (appends a dated line, reaches the prompt)
- `seneschal_ask_user` round trip

## 8. Assistant instructions
- Edit and reset each of the five texts (a text edited by hand does not get new default steps, say so), change is used from the next message, default is not kept as a change
- The prompt contains the date, address, installed tools, lessons

## 9. Authoring
- `seneschal_extension_guide`, `_create_from_template`, `_files`, `_read`, `_write`, `_build` (asks), `_check` finds planted problems, `_preview` returns screenshots and findings
- Path safety: a path outside the project is refused, a non-text file is refused

## 10. LoopUp extension (separate repo, shipped as `.snx`)
- Logs, Token Generator, Projects open and show their empty states with no data
- Add and edit dialogs validate and cancel cleanly
- Nothing in it starts or stops anything on the machine during the test

## 11. Persistence and safety
- Data lives in the data folder only, a restart keeps settings, tools on/off, icons, instructions, history
- A corrupt settings file does not stop the app
- Nothing writes into the repo except the Projects actions that say so

## 12. Install and update
- Install script parameters (`-NoDev`), update notice appears for a newer version (simulated feed), not run for real here
- The uninstaller entry (Settings, Apps) and `uninstall.cmd` remove the app and leave the data folder, not run for real here

Results are in the report (`docs/reports`).
