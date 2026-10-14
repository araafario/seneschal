# What Seneschal lacks

## Why it matters

A developer's day is spread over small things: a command they keep forgetting, a script in a gist, three terminals, a token they fetch by hand, a Docker container they start every morning, a skill or prompt they copy between chats. Each is too small for its own app and too annoying to leave. Seneschal is the one place they can live, for three reasons:

1. **It is local.** Nothing leaves the machine, so tools may touch tokens, containers and source code that no hosted product would be trusted with.
2. **A tool takes minutes, not days.** The assistant writes the page, builds and tests it. The people who gain most are the ones who would never have made a UI for a script.
3. **Everything is also an MCP tool.** The same tools work from Claude Code, Cursor, VS Code or Claude Desktop, so an automation made once is reachable from every AI a developer uses.

What makes or breaks it is the first ten minutes (does it show value before the person has made anything), the trust in what the assistant builds (does it ask, does it test), and the daily friction (how fast do I get to my tool).

## Improvements

Priority: **P1** do now, **P2** next, **P3** later. Effect is for a developer using it daily. Status is at the time of writing.

| # | Improvement | Pri | Effect | Local | Status |
|---|-------------|-----|--------|-------|--------|
| 1 | Home that teaches: Get Started and What's New next to the tools | P1 | Big | yes | done |
| 2 | An empty Projects page that explains the three steps | P1 | Big | yes | done |
| 3 | Keyboard: Ctrl+K to the search, Ctrl+J to the assistant, Esc closes every dialog | P1 | Big | yes | done |
| 4 | The sidebar works in a narrow window (rail, opens over the page) | P1 | Big | yes | done |
| 5 | Build failures: a short toast with a Details popup and Copy, not a wall of text | P1 | Medium | yes | done |
| 6 | The assistant asks with choices instead of open questions | P1 | Big | yes | done |
| 7 | The assistant can learn from corrections and change its own character, with approval | P1 | Big | yes | done |
| 8 | Favorites on Home | P2 | Medium | yes | done |
| 9 | About: version, data folder, log, update check | P2 | Medium | yes | done |
| 10 | The MCP endpoint answers a wrong secret with a plain 401 | P1 | Small | yes | done |
| 11 | Extension version in the row menu, no repeated badge on every row | P2 | Small | yes | done |
| 12 | Toasts sit above the assistant button | P2 | Small | yes | done |
| 13 | A tool can be pinned to Home in a chosen order (drag) | P2 | Medium | yes | open |
| 14 | `seneschal doctor`: checks the SDK, the Claude path, the data folder and the secret, and says what to fix | P2 | Medium | yes | done |
| 15 | Back up and restore all settings (one file) | P2 | Medium | yes | open |
| 16 | Recently used tools on Home and in the search | P3 | Small | yes | open |
| 17 | More templates for a new extension (command runner, form and table, dashboard) | P2 | Big | yes | open |
| 18 | MCP resources and prompts, so a tool can offer a ready prompt to any client | P3 | Medium | yes | open |
| 19 | A gallery of sample extensions people can import (separate repository) | P2 | Big | no | open |
| 20 | Scheduled runs: a tool action that runs every morning or on a trigger | P3 | Big | yes | open |
| 21 | Notifications when a long action ends | P3 | Medium | yes | open |
| 22 | Sign a package and show who made an imported extension | P2 | Big | no | open |
| 23 | A dry-run view that shows what an assistant-made extension will do before it is built | P3 | Medium | yes | open |
| 24 | Per-extension permissions (files, network, processes), shown at import | P3 | Big | no | open |
| 25 | Linux and macOS (the app is Windows only today) | P3 | Big | no | open |

"Local" means the change stays inside this repository and does not need a service, a store or a decision from someone else. The rule used: a local change that matches how Seneschal already works and has a big effect is done directly. Anything that needs a hosted piece, a signing story or another platform is listed and left.

## Done in this round

Items 1 to 12 and 14. The details, with before and after, are in the report in `docs/reports`.
