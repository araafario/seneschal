# Seneschal UI guidelines

How Seneschal looks, reads and behaves. They apply to the host (`src/Seneschal.Host`) and to every extension, so a new tool feels like it was always there.
When something here and the code disagree, fix the code, or change this file in the same change.

## Principles

1. **Quiet.** Content first. Chrome, borders and color are there to separate things, not to decorate them.
2. **Simple.** One primary action per area. If a screen needs a paragraph to explain itself, it needs a redesign.
3. **Obvious.** A control says what it does by its icon or its verb. Do not explain what is already clear: a play button is *Start*, not "Start the selected item and wait for it".
4. **Consistent.** The same thing looks and reads the same everywhere. Reuse a component before making a new one.
5. **Honest.** Show what is really happening (loading, busy, failed) and never leave the user guessing whether a click did something.

## Wording

Write like a person who is short on time and respects yours.

| Do | Don't |
| --- | --- |
| Short: 1 to 5 words for a label, one line for a message | Paragraphs, "please", "in order to", "successfully" |
| Plain words: *Start*, *Stop*, *Remove* | Jargon, internals, class or file names unless they help |
| No trailing punctuation: no `.` `,` `;` `!` `...` | "Done." "Loading..." "Sorry!" |
| Say it once | Repeat what a banner, a title or the button already says |
| Name the thing: *Delete Demo?* | Generic *Are you sure?* |

- **Case.** *Title Case* for page titles, section headers, dialog titles and text buttons (*Add Containers*, *Smart Startup*, *Restart App*). *Sentence case* for everything else: labels, placeholders, helper text, tooltips, status text.
- **Buttons are verbs**, and the confirm button repeats the verb of the dialog: *Delete*, *Remove*, *Add 3*. Never *Yes* or *OK*. The cancel button is always *Cancel*.
- **Add, Remove, Delete.** *Add* puts something on the page. *Remove* takes it off the page and leaves the thing itself alone. *Delete* destroys it. Use the right one.
- **Search and Filter.** *Search* finds across the app (the sidebar). *Filter* narrows a list that is already on screen. Placeholders: `Search tools`, `Filter containers`. No ellipsis.
- **Optional, not required.** Mark the optional field (`Title (optional)`), not the required one. No asterisks.
- **Status words**, in this order of preference: one word (`Idle`, `Stopped`, `Starting`, `Ready`), then a short phrase (`Up 6 days`, `Ready in 6s`). A state that is in progress has no ellipsis: the dot pulses and the bar moves.
- **Numbers** are digits (`3 items`). Units are short and attached (`6s`, `120 sec`).
- **Errors** say what happened in a few words, then what to do if there is something to do: `Port 5010 is in use` then `Stop what holds it or pick another port`. No apology, no blame, no stack traces.
- **Empty states** are one quiet line (`No projects yet`). The button that fills them is already on screen.
- **Descriptions** of a tool or an extension (the line under its name in the Extension Manager) follow the same rules: one short phrase, no period. `Follow several logs side by side`, not `Follow several logs at once: side by side, stacked or in a grid.`
- **Terms.** *project* (.NET), *container* (Docker), *microfrontend* (single-spa), *instance* (another copy of a project), *startup* (an ordered plan), *extension*, *tool*, *built-in*. Do not mix in synonyms.

## Layout and spacing

Everything sits on a **4px grid**. Use only these steps: `4 8 12 16 24 32`.

| Where | Value |
| --- | --- |
| Gap between buttons, between icon and text | 8 |
| Gap between rows (cards in a list) | 8 |
| Gap inside a toolbar | 12, with 12 below it |
| Card padding | 8 to 12 vertical, 12 to 16 horizontal |
| Gap between sections on a page | 24 |
| Dialog padding | 24, with 20 above and 16 below a divider |
| Field to field in a form | 16 |
| Page gutter | set by `PageShell`, do not add your own |

**Radius.** Cards and panels `6`. Controls (buttons, inputs, selects, small icon buttons) `4`. Pills (badges, chips) fully round. Nothing else.

**Alignment.** Left-align text and labels. Right-align actions. Within a row, everything is vertically centered. Controls that belong together sit on one baseline: an icon, its text and its button share one row, never stacked at different heights. Numbers in a column align by their digits.

**Density.** One screen, one scroll. A list row is about 56 to 60px high. Do not add a row of decoration to fill space. The whole app is sized from one scale of CSS variables (`--fs-*` for text, `--h-ctl` for controls, `--gap-*` for spacing, at the top of `app.css`) and from the theme typography in `MainLayout.razor`, so it can be made tighter or looser in one place. Never write a size that is not on the scale.

## Color and theme

Never write a color in a component. Use the variables defined on the themed layout (`app-dark` and `app-light` in `app.css`).

| Variable | Use |
| --- | --- |
| `--fg`, `--fg-dim`, `--placeholder` | text |
| `--border`, `--card-border` | hairlines |
| `--card`, `--card-hover`, `--card-shadow` | card surfaces (the shadow is for light mode only) |
| `--field`, `--field-edge` | input surfaces |
| `--hover`, `--active`, `--tile` | interaction and tinted fills |
| `--accent`, `--on-accent` | the one highlight color, and the text on it |
| `--ok`, `--warn`, `--bad`, `--info` | meaning: success, warning, error, information |
| `--terminal` | log and code surfaces |

- **One accent.** It marks the primary action, the selection and the active step. It is never used for decoration.
- **Meaning, not mood.** Green, amber and red only say *ok*, *careful* and *failed*. A status is never color alone: it always has a word or an icon.
- **Both themes are first class.** Check every change in dark and light. Contrast is at least 4.5:1 for text and 3:1 for icons and borders.
- **Popups live inside the themed layout** so they inherit these variables. Do not move a provider outside it.

## Typography

One family (Segoe UI Variable, then system). Sizes come from this scale only.

| Role | Size and weight |
| --- | --- |
| Page title | 1.625rem, 600 |
| Section title | 1rem, 600 |
| Row title, banner title | .9375rem, 600 |
| Body, controls | .875rem (14px) |
| Secondary, small controls | .8125rem |
| Caption, meta, badge | .8125rem, never below .75rem |

Uppercase with letter spacing is allowed for one thing only: the small label of a field group inside a card or dialog.

## Components

Reuse these. Their classes and files are the source of truth.

- **Sidebar**: compact, so more of the page is visible. 300px wide (a 52px rail when collapsed), rows 36px high, labels .875rem, icons 20px, 8px gutters, the search field about 34px high. Long names are cut with an ellipsis and show in full as a tooltip. Group items are indented so their icons line up with the group's label, and the current page has a 3px accent mark on the left. Everything in it (home, groups, tools, the footer links) uses the same row.
- **Page** (`PageShell`): icon, title, actions on the right, a divider, then content.
- **Section** (`SectionFrame`): a collapsible header (title, short summary), then a toolbar, then the content. A thin bar on the header means that section is loading. Never for background refresh.
- **Toolbar**: buttons that act on a list (*Add*, *Import*, *New Extension*) sit on the same row as that list's title or filter, at the right end with the primary last, never in the page header. In a section toolbar of the tools, primary add button first, then the filter, then on the right *Select all* and the bulk actions (*Start*, *Stop*).
- **Row card** (`.run-row`): grip, checkbox, status dot, main (name, up to 3 badges, one line of meta), state text, actions. The same order in every list. A badge appears only when it adds something: never repeat the name, the kind, or what the section already says.
- **Buttons**: *primary* (filled accent) for the one main action in an area, *outlined* for secondary actions, *text* for tertiary ones, *icon* for repeated row actions. Title Case, 36px high (30px when small). A text button that already names its action has no icon, so no `+` on *Add Containers*. Icon-only buttons need a tooltip with the verb (`Start`, `Stop`), nothing more.
- **Badge**: pill, `.75rem`, a hairline border. At most three on a row.
- **Feedback**: a banner is for a state that needs attention and stays until resolved (*Restart to use demo*). The success of an action whose result is already visible shows nothing. A long action shows a spinner on its button, then a short toast (bottom right) for success. A failure is a toast that stays until dismissed. Never show the same news as a banner, a toast and a row at once.
- **Banner** (`Banner` in the host, `MudAlert` with `Dense` in extensions): a notice or a state. A short title, an optional line of detail, actions on the right, a close button when it can be dismissed. It reports what is new and does not repeat another banner.
- **Dialog**: a title (Title Case) with at most one short line under it, the content, then the actions on the right, *Cancel* then the primary. A destructive primary uses the error color. The first field has focus, *Esc* closes, the primary is disabled until the form is valid. Dialogs open at most two deep.
- **Destructive actions**: ask once, in a dialog that names the thing and lists what happens in short lines. Something that cannot be undone, or that removes part of the app itself, asks for a typed word (see *Delete Extension*).
- **Empty state** is left-aligned: the logo, *How can I help*, one line about what it can do, then up to four starter cards in two columns. Each card is a request a tool offers for itself (`ToolMetadata.Prompts`, one from each tool in turn), with the tool's icon and name, so they are things worth asking and never questions about the assistant itself.
- **Loading**: answer within 100ms, then say what is going on. Draw the frame first and the slow content after it (`ToolPage` does this for every tool), a thin accent bar at the top of the window while the app loads or a page is being drawn, a bar on a section header while that section waits, skeleton rows in the shape of what is coming instead of a flash of wrong content or a blank gap, a spinner on the button of a long action (beside it when the button is disabled, never inside), a ring around the assistant button while it opens or works, and a locked row with a pulsing dot while an action runs. A second click must never start a second action. Do slow work (disk, processes, PATH, network) once and keep the answer, never on every redraw.
- **Forms**: label above the field (the floating label), a placeholder that shows an example, a helper line only when the field is not obvious, an error that replaces the helper and says what to do. Group long forms with short section labels and a hairline.
- **Icons**: Material outlined for actions and navigation, filled only for *play*, *stop* and the selected state. 20px in rows and in the sidebar.
- **Lists can be reordered** by dragging: a faint grip at the left of the row, a line where it will land, the order saved.

## Assistant

The chat panel in the bottom right corner is part of the app and follows the same rules. It is a work tool, not a chat toy.

- **Quiet, not decorated.** No gradient and no bubbles for the assistant. A small accent-tinted icon sits at the left of the header and the Seneschal logo greets the empty chat, nothing else is decoration. The assistant's answer is plain text on the panel. The user's message is a tinted block (`--active`), never the accent. The accent marks the send button, the current chat, the focus border of the composer and the icons of the starter cards.
- **Surface.** The panel is `--drawer-bg` with a hairline border and radius 6, the same as the sidebar. Header 48px, one row: icon, title, model, icon buttons on the right (History, New Chat, Settings, Close), a 2px bar under it that moves while it works. The composer is a box of its own (radius 8, `--card`) with its hint underneath.
- **Tool calls** are one 32px row each (status, name, arguments in dim text) that opens to the input and result in the `--terminal` surface. Failed ones get the `--bad` border.
- **Empty state** is left-aligned: a title, one line (how many tools), and suggestions as plain rows with hairlines, not pills.
- **Waiting** is a pulsing dot with the word *Working*, never bouncing dots. A change that needs approval is a tinted card with *Deny* and *Allow*, and the delete variant uses `--bad`.
- **History** keeps the last 30 chats, newest first, each a title and when it was used. The current one has the accent mark on the left. Delete is an icon on hover.
- **Wording** is the same as everywhere: short, no trailing punctuation, Title Case on buttons.

## Behavior

- **Feedback within 100ms.** Show the busy state first, then do the work off the UI thread.
- **Confirm only what hurts.** Starting, stopping and filtering never ask. Deleting does.
- **Remember what the user arranged**: order, collapsed sections, layout, last folder.
- **Never lose work silently.** If something fails, keep the form and say why.
- **Keyboard and screen readers**: every control reachable by keyboard, a visible focus ring, an accessible name on every icon button, status conveyed by text as well as color.
- **Safe by default.** Test destructive features on a copy, not on the real thing.

## Extensions

Extensions get the same look by using the same variables, the same wording rules and the same class names (`.run-row`, `.run-empty`, `.banner` and so on). Host components such as `PageShell` and `Banner` are not available to extensions: use `MudAlert`, and the shared CSS classes, to get the same result. Keep class names for your own pieces specific (a prefix such as `jd-`).

## Checklist for a change

- [ ] Every string is short, has no trailing punctuation and is in the right case.
- [ ] Spacing is on the 4px grid and radii are 6 (cards) or 4 (controls).
- [ ] No color or font size is written by hand.
- [ ] Looks right in dark and in light.
- [ ] Empty, loading, busy, error and long-text states are handled.
- [ ] Icon buttons have a tooltip and an accessible name.
- [ ] Nothing explains what is already obvious, and nothing is said twice.
- [ ] A dialog has *Cancel* and a primary named with a verb.
