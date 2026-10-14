# UI audit plan

Goal: every screen, dialog and state of Seneschal is checked against [What to avoid](../ui-guidelines.md#what-to-avoid) and the rest of the guidelines, and every miss is fixed. A miss that appears twice becomes a rule.

## Method

1. Run a copy of the app (own port, scratch data folder) with the LoopUp extension, a sample dev project and sample data, so every state can be reached.
2. For each surface below, open it at **1440px** and **760px**, in **dark** and **light**, and at **375px** for the overflow check. Take a screenshot of each.
3. Go through the checklist (rule ids from the guidelines). Record each miss as `surface | rule | what | fix`.
4. Fix it in the code, rebuild the copy, look again. A surface is done when a pass finds nothing.
5. After each round, ask: did the same kind of miss appear on more than one surface, or was it a rule that was missing? Add or tighten the rule.
6. Rounds repeat until a full pass over all surfaces finds nothing new.

## Surfaces

| # | Surface | States to reach |
|---|---------|-----------------|
| 1 | Home | with tools, with no tools, tool search |
| 2 | Sidebar and search | expanded, collapsed rail, long names, search with results and with none |
| 3 | Group page | with tools |
| 4 | Tool pages (LoopUp: Logs, Token Generator, Projects; Text: Json Diff) | loading, empty, with data, error |
| 5 | Options, Extensions | list, filter, no match, needs attention, row menu, import |
| 6 | Options, Appearance | groups, icon picker, custom badge, reset |
| 7 | Options, Assistant | Claude found, not found, model select, switch |
| 8 | Options, Instructions | five texts, edit dialog, reset |
| 9 | Options, MCP | secret hidden and shown, reset confirm, client cards, approvals switch |
| 10 | Options, MCP Tools | filter, groups open, no match |
| 11 | Options, Projects | live reload on and off, project open and closed, built and not built, empty |
| 12 | Dialogs | Import extension, New Project, Add Project, Delete Project, icon picker, group icon, instruction editor, reset secret, remove build, delete extension, restart notice |
| 13 | Assistant panel | empty, chat with tool calls, running, approval card, destructive approval card, question card (single, multiple, other), history, Claude not found, long text, settings gear |
| 14 | Tutorial page | all panels |
| 15 | System | error page, not found page, reconnect modal, update notice, banners, toasts |
| 16 | Extension pages (LoopUp) | all dialogs: Add Projects, Add Containers, Add Instances, Edit Project, Startup Plan, Process Log, Errors, Folder Browser |

## Checklist per surface

The rule list is [What to avoid](../ui-guidelines.md#what-to-avoid) (A1 to A15, B1 to B11) plus the older checklist at the end of the guidelines. Quick checks to do on every screenshot:

- chips and badges: count per row, does each say something new
- colors in use: how many status colors, any gradient or glow
- cards: how many levels of visible boundary
- alignment: left edges, icon and text centers, equal button heights
- spacing: any value off the scale, any related pair farther apart than an unrelated pair
- text: sizes, weights, trailing punctuation, case, marketing words, helper text that says nothing
- contrast in both themes, state shown by more than color
- loading, empty, error, disabled, hover, focus
- sideways scroll at 375, 760, 1440

## Log

Findings, fixes and new rules are recorded in the report (`docs/reports`).
