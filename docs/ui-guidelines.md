# Seneschal UI guidelines

How Seneschal looks, reads and behaves. They apply to the host (`src/Seneschal.Host`) and to every extension, so a new tool feels like it was always there.
When something here and the code disagree, fix the code, or change this file in the same change.

## Principles

1. **Quiet.** Content first. Chrome, borders and color are there to separate things, not to decorate them.
2. **Simple.** One primary action per area. If a screen needs a paragraph to explain itself, it needs a redesign.
3. **Obvious.** A control says what it does by its icon or its verb. Do not explain what is already clear: a play button is *Start*, not "Start the selected item and wait for it".
4. **Consistent.** The same thing looks and reads the same everywhere. Reuse a component before making a new one.
5. **Honest.** Show what is really happening (loading, busy, failed) and never leave the user guessing whether a click did something.
6. **Dense, not cramped.** A developer tool shows a lot at once. Density comes from a tight type and spacing scale, not from removing padding or shrinking text below the scale. Compact is not minimal: do not add whitespace, large cards or decoration to look calm, and do not remove a needed affordance to look clean.
7. **Built for any amount.** Nothing is designed for a fixed number of tools, groups, rows or characters. One item and a hundred items, a two-letter title and a sixty-word description, must both look intended.

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
- **Descriptions** of a tool or an extension (the line under its name in Options, Extensions) follow the same rules: one short phrase, no period. `Follow several logs side by side`, not `Follow several logs at once: side by side, stacked or in a grid.`
- **Terms.** *project* (.NET), *container* (Docker), *microfrontend* (single-spa), *instance* (another copy of a project), *startup* (an ordered plan), *extension*, *tool*, *built-in*. Do not mix in synonyms.

## Layout and spacing

Everything sits on a **4px grid**. Use only these steps: `4 8 12 16 24 32` (`--gap-1` to `--gap-6`).

| Where | Value |
| --- | --- |
| Gap between buttons, between icon and text | 8 |
| Gap between rows (cards in a list) | 8 |
| Gap inside a toolbar | 12, with 12 below it |
| Card padding | 8 to 12 vertical, 12 to 16 horizontal |
| Gap between sections on a page | 24 |
| Gap between a page's main column and its aside | 32 |
| Dialog padding | 24 on every side (`.mud-dialog` rules in `app.css`) |
| Section to section in a dialog or form | a hairline with 24 above and 16 below, the small section label 12 above its fields |
| Field to field in a form | 16, from one wrapper (`.dlg-fields`), not a margin on each field |
| Page gutter | set by `PageShell`, do not add your own |

**Radius.** Cards and panels `6`. Controls (buttons, inputs, selects, small icon buttons) `4`. Pills (badges, chips) fully round. Nothing else.

**Alignment.** Things that belong together share one left edge, and it comes from the structure, never from a hand-set margin. A description under a label sits in the same text column as the label, so it starts exactly where the label starts: a switch, checkbox or icon is its own column to the left of that text column (`SwitchRow`, `SettingCard`). A number like `margin-left: 32px` that guesses where the label begins is a bug waiting for the next font or control size. Left-align text and labels. Right-align actions. Within a row, everything is vertically centered. Controls that belong together sit on one baseline: an icon, its text and its button share one row, never stacked at different heights. Numbers in a column align by their digits.

**Widths.** Content has a maximum width and does not stretch across a wide window: Home 1280px (`--w-home`), settings 920px, reading text about 75 characters. A grid of cards fills its row with `auto-fill` columns of at least `--card-min`, so a group with one card and a group with twelve both keep the same card size, and a narrow window gets fewer columns instead of a sideways scroll. Never size a card, row or column in a fixed number of pixels that the content has to fit: let it grow, wrap or clamp.

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
| Section head title (Options) | 1.375rem, 600 |
| Section title | 1rem, 600 |
| Row title, banner title | .9375rem, 600 |
| Body, controls | .875rem (14px) |
| Secondary, small controls | .8125rem |
| Caption, meta, badge | .8125rem, never below .75rem |

Uppercase with letter spacing is allowed for one thing only: the small label of a field group inside a card or dialog.

## Components

Reuse these. Their classes and files are the source of truth.

- **Sidebar**: compact, so more of the page is visible. 300px wide (a 52px rail when collapsed), top-level rows 36px high and the tools under a group 32px (`--h-nav-sub`), labels .875rem, icons 20px, 8px gutters, the search field about 34px high. A group row is a link to the group page, the number of its tools (dim, `tabular-nums`), and a separate chevron button that only expands (`aria-expanded`). The tools of a group hang from a hairline under the group's icon, with their icons lined up with the group's label. Long names are cut with an ellipsis and show in full as a tooltip, and the current page has a 3px accent mark on the left. It must stay short with many tools: past 12 tools the groups start collapsed, and choosing a tool opens its group. The sidebar scrolls on its own (`.drawer-scroll`) with Options pinned below it. Every row (home, groups, tools, the footer links) is a real link or button, reachable by Tab with a visible focus ring.
- **Home**: the page head (the 72px logo, the title at `--fs-hero`, one line) on the glow, then the tool groups at the left and an aside (Get Started, What's New) at the right, all under the same section heading (`.home-group-title`: the name as a link to its group page, then the count). The aside stays in view however long the groups are and drops under them below 1100px. Onboarding is a compact list of rows with a hairline between, never cards: it must not compete with the tools. A group appears only when it has tools, and with no tools there is a single quiet line.
- **Group color**: each group has one of the logo's colors (blue, red, gold, or neutral: `--hue-0` to `--hue-2`, `--fg-dim`). The user picks it in Options, Appearance, next to the group's icon. Until then it is handed out in turn along the groups in alphabetical order (`GroupIcons.Color`), so groups next to each other never share one and nothing depends on a hidden hash. The color only tints the icon tile and the dot before the group's heading, never text, borders or card backgrounds, and it identifies a group, it does not mean a state, so it does not count against A2. Contrast of the icon on its tile is 3:1 or better in both themes.
- **Tool card** (`ToolCard`, laid out by `ToolGrid`, used on Home, group pages and Favorites): one link to the tool, with the tool's icon (20px, in a 36px tile tinted with its group's hue), a title on one line and a description of two lines at most, both cut with an ellipsis and complete in the tooltip. Hover tints the card and shows a chevron, keyboard focus draws the accent ring. A card does not expand: it opens the tool, so a card that needs more room is a sign the description is too long. Always use `ToolGrid` for a set of tools, never a new grid.
- **Page** (`PageShell`): icon, title, actions on the right, a divider, then content.
- **Options** (`/options`): the one place for how Seneschal itself is set up, built like a settings page. See *Settings pages* below.
- **Section** (`SectionFrame`): a collapsible header (title, short summary), then a toolbar, then the content. A thin bar on the header means that section is loading. Never for background refresh.
- **Toolbar**: buttons that act on a list (*Add*, *Import*, *New Extension*) sit on the same row as that list's title or filter, at the right end with the primary last, never in the page header. In a section toolbar of the tools, primary add button first, then the filter, then on the right *Select all* and the bulk actions (*Start*, *Stop*).
- **Row card** (`.run-row`): grip, checkbox, status dot, main (name, up to 3 badges, one line of meta), state text, actions. The same order in every list. A badge appears only when it adds something: never repeat the name, the kind, or what the section already says.
- **Buttons**: *primary* (filled accent) for the one main action in an area, *outlined* for secondary actions, *text* for tertiary ones, *icon* for repeated row actions. Title Case, 36px high (30px when small). A text button that already names its action has no icon, so no `+` on *Add Containers*. Icon-only buttons need a tooltip with the verb (`Start`, `Stop`), nothing more.
- **Badge**: pill, `.75rem`, a hairline border. At most three on a row.
- **Feedback**: a banner is for a state that needs attention and stays until resolved (*Restart to use demo*). The success of an action whose result is already visible shows nothing. A long action shows a spinner on its button, then a short toast (bottom right) for success. A failure is a toast that stays until dismissed. Never show the same news as a banner, a toast and a row at once.
- **Banner** (`Banner` in the host, `MudAlert` with `Dense` in extensions): a notice or a state. A short title, an optional line of detail, actions on the right, a close button when it can be dismissed. It reports what is new and does not repeat another banner.
- **Dialog**: a title (Title Case) with at most one short line under it, the content, then the actions on the right, *Cancel* then the primary. A destructive primary uses the error color. The first field has focus, *Esc* closes, the primary is disabled until the form is valid. Dialogs open at most two deep.
- **Destructive actions**: ask once, in a dialog that names the thing and lists what happens in short lines. Something that cannot be undone, or that removes part of the app itself, asks for a typed word (see *Delete Extension*).
- **Assistant button**: a round 52px button at the bottom right of every page that opens the chat. It names itself and its shortcut (`Ctrl+J`) on hover and focus, shows a ring while it works and a dot when something waits for approval. Pages keep `--fab-clear` of space at the bottom so the button never covers content, and toasts sit above it.
- **Empty state** is left-aligned: the logo, *How can I help*, one line about what it is, then four starter cards in two columns. The first is always *Help me create a new extension* (Seneschal can start empty), the others are each a request a tool offers for itself (`ToolMetadata.Prompts`, one from each tool in turn), with the tool's icon and name, so they are things worth asking and never questions about the assistant itself.
- **Loading**: answer within 100ms, then say what is going on. Draw the frame first and the slow content after it (`ToolPage` does this for every tool), a thin accent bar at the top of the window while the app loads or a page is being drawn, a bar on a section header while that section waits, skeleton rows in the shape of what is coming instead of a flash of wrong content or a blank gap, a spinner on the button of a long action (beside it when the button is disabled, never inside), a ring around the assistant button while it opens or works, and a locked row with a pulsing dot while an action runs. A second click must never start a second action. Do slow work (disk, processes, PATH, network) once and keep the answer, never on every redraw.
- **Toggle**: in a dialog or form use `SwitchRow` (the switch, then one text column with the label and its single dim line, a click on the text toggles it); in a settings page use `SettingCard`. Never put a `MudSwitch` with a `Label` and a separate hint line under it. Every on or off setting is the pill switch (`MudSwitch`): a 40x24 fully rounded track with a round thumb that slides, the same as the day and night toggle. Never a square track, a checkbox or a two-state button for on or off. A checkbox is only for picking items in a list. The label sits `--gap-2` (8px) from the switch, never touching it. A switch with no visible label gets an `aria-label`. The global rule on `.mud-switch` in `app.css` does this, so do not restyle it in a component.
- **Forms**: label above the field (the floating label). MudBlazor 9 puts `mud-shrink` on the input and floats the label with the rule `input.mud-shrink ~ label`, so never write a rule on the label alone: it holds the label down on top of the value, a placeholder that shows an example, a helper line only when the field is not obvious, an error that replaces the helper and says what to do. Group long forms with short section labels and a hairline.
- **Icons**: the icon an extension gives its tool, and the one a group has by name, are only defaults. The user can pick another for any tool or group in Options, Appearance (kept in `icons.json`) and reset it, so always draw a tool's icon through `GroupIcons.Tool(tool)` and a group's through `GroupIcons.Get(group)`, never from the metadata directly. Material outlined for actions and navigation, filled only for *play*, *stop* and the selected state. 20px in rows and in the sidebar.
- **Lists can be reordered** by dragging: a faint grip at the left of the row, a line where it will land, the order saved.

## Settings pages

Options is a settings page, and it follows the Fluent app settings guidance (learn.microsoft.com, Windows apps, design, app settings: group related settings under headers, one card per setting, sub-options in an expander that is never nested deeper than one level, a scrollable page of about 1000px at most). The classes are in the settings block of `app.css`.

- **Layout** (`.opt-layout`): a list of sections at the left (232px, small group labels *General*, *AI*, *Developer* above their rows, each row 36px with an icon and a short Title Case name, the current one marked), the section at the right at 920px at most. Under 800px the list runs along the top. Every section is a component listed in `Shell/Sections/OptionSections.cs`. *General* holds Extensions, Appearance and About; *AI* holds Assistant, Instructions, MCP and MCP Tools. *Developer* only shows when the .NET SDK is installed or the app runs from source. Keep a section about one subject, and split it when it grows long: the MCP address and the list of MCP tools are two sections for that reason.
- **Section head** (`OptionHeader`): the title, one line that says what the section is for, the main action at the right end. Nothing else above the first group.
- **Group heading** (`.opt-group`, 14px semibold, `--gap-5` above): names a few related settings. The first group sits directly under the head.
- **Setting card** (`SettingCard`): an icon at the left, a bold title and one dim line that says what it does, the control at the right end (switch, select, button, menu). A form or a list that needs the full width goes under the row as the card's content. Cards in a group are 8px apart (`.opt-stack`).
- **Expander** (`SettingExpander`): a card whose rows open under it, with a chevron at the right end. One level only: a row inside is a plain `SettingCard` with `Nested`, never another expander. It starts closed unless the person is searching or only one thing is there.
- **Status** is a badge after the title (*Default*, *Custom*, *Loaded*, *Not built yet*), never a sentence in the description. A state that needs attention is a banner at the top of the section.
- **Actions**: a button that acts on one row sits on that card at the right end, a menu (the three dots) holds the rarer ones, and the one that removes something is last, in the error color, behind a confirmation dialog.
- **Empty** is a dashed box, left-aligned and as wide as the cards around it, with one line (*No tools yet*, *No match*) and, when there is something to do, a dim line or the button that does it. A first-run guide, a numbered list of steps or a link floating below the page is not an empty state: the actions are already on screen, so do not repeat them.
- **Wording**: a title is a noun or a short phrase, a description is one sentence with no end punctuation that says what it does and not how to use the control.

## Assistant

The chat panel in the bottom right corner is part of the app and follows the same rules. It is a work tool, not a chat toy.

- **Quiet, not decorated.** No gradient and no bubbles for the assistant. A small accent-tinted icon sits at the left of the header and the Seneschal logo greets the empty chat, nothing else is decoration. The assistant's answer is plain text on the panel. The user's message is a tinted block (`--active`), never the accent. The accent marks the send button, the current chat, the focus border of the composer and the icons of the starter cards.
- **Surface.** The panel is `--drawer-bg` with a hairline border and radius 6, the same as the sidebar. Header 48px, one row: the icon, the model picker (no word "Assistant": the icon says it), icon buttons on the right (History, New Chat, Settings, Close), a 2px bar under it that moves while it works. The composer is a box of its own (radius 8, `--card`) with no hint line under it and no send button: Enter sends, Shift+Enter makes a new line, and a stop button replaces nothing but appears while it works. Typing stays in the browser and is sent only on Enter, so a keystroke never redraws the panel. It keeps the same 12px (`--gap-3`) from the panel's left, right and bottom edge: never let it touch the bottom border.
- **Tool calls** are one 32px row each (status, name, arguments in dim text) that opens to the input and result in the `--terminal` surface. Failed ones get the `--bad` border.
- **Empty state** is left-aligned: a title, one line (how many tools), and suggestions as plain rows with hairlines, not pills.
- **Waiting** is a pulsing dot with *Running for 12s* (the time is counted in the browser, so the panel does not redraw each second), never bouncing dots. A change that needs approval is a tinted card with *Deny* and *Allow*, and the delete variant uses `--bad`. A question the assistant asks (`seneschal_ask_user`) is a card on the panel color with the options as rows: a round mark for one choice, a square one for several, a bold label with a dim line under it, and a box to write an own answer. The card sits above the composer with the same 12px margin, never taller than half the panel (it scrolls inside). *Skip* is the close icon at the top right of the card, a single choice answers on the click, several need *Send*, and the box for an own answer sends on Enter. The cards show in every state of the panel, including *Claude Code was not found*, because another MCP client can call a tool that asks. That state also offers *Copy Config* and *Copy Command* so any MCP client can be connected.
- **History** keeps the last 30 chats, newest first, each a title and when it was used. The current one has the accent mark on the left. Delete is an icon on hover.
- **Wording** is the same as everywhere: short, no trailing punctuation, Title Case on buttons.

## What to avoid

The audit list. Every screen, dialog and state is checked against it (`docs/qa/ui-audit-plan.md`), and every miss that shows up twice becomes a line here. Rule ids (`A1`, `B3`) are what the audit reports use. A pattern may stay only with a written reason next to it.

Seneschal is a dense desktop tool used with a mouse and a keyboard, so a few general rules are set for that: body text is 14px (`--fs-body`), dim meta text is 13px and nothing is below 12px, controls are 30px or 36px high instead of 44px touch targets, and the page is checked at 1440px, at about 760px and at 375px (nothing may scroll sideways at any of them).

Prose in documentation pages (the extension tutorial, the guides) is written in normal sentences with their full stops, because runs of sentences without them cannot be read. The no trailing punctuation rule is for labels, buttons, titles, toasts and the one-line description of a setting.

### A. Looks machine-made

- **A1 Color.** One accent (the theme accent), neutrals, and a semantic color only for a state that matters. No purple or indigo by default, no gradient text, no gradient on cards or buttons without a reason, no neon glow in dark mode. The exceptions are on Home only. A full-width glow behind the head in the logo's colors (`--wash-blue`, `--wash-red`, `--wash-gold`: radial gradients that fade out in 380px, static, off in forced colors, drawn by the page so it has no edge), and the group hues below. Nothing else gets a gradient.
- **A2 Status colors.** At most two status colors on one screen. A row of green, yellow, red, blue and purple badges is wrong: neutral badges for facts (*Built-in*, *Default*), a color only for *needs attention* or *failed*.
- **A3 Colored left border** on a card as the way to say important. Use a banner, or nothing.
- **A4 Cards.** A card is for one setting or one item. No card inside a card inside a card (two levels visible at most: the card and its rows, divided by hairlines). A grid of cards is right for things that are browsed and opened by their name and icon (tools); for anything with state, several facts or row actions, use a list of rows.
- **A5 Centered everything.** An option in a card, a row in a list and an empty state are left-aligned, and a button element does not center its content on its own, so a row made of a button sets `justify-content` itself. Left-align text, forms and headings. Center only a short isolated thing (an empty-state line, a spinner).
- **A6 Decoration.** One radius (6 for cards, 4 for controls), one shadow level or none, one border style. No glass, no blobs, no glow (except the Home head wash in A1), no animated borders, no scale-up on hover, no `transition: all`. Hover is a background tint, focus is the accent ring.
- **A7 Icons.** An icon is for navigation, a setting card or a status, not on every button, label and heading. A text button that names its action has no icon. Icons in one row are one size.
- **A8 Emoji** never stand in for icons or sit in headings or copy.
- **A9 Badges and chips.** A badge says an exception: an imported extension is the normal case and has none, a dev build says *Dev*, a loaded project has none and an unbuilt one says *Not built yet*. At most three on a row and only when each says something the row does not already say. No *New*, *Beta*, *Pro*. A filter row of more than five chips becomes a select. A chip that repeats the group or the section title is removed.
- **A10 Eyebrows.** No small uppercase label above every heading. Uppercase is allowed for the field-group label in a card or dialog only.
- **A11 Type.** Two weights on a screen (400 and 600), bold is not used inside paragraphs for emphasis, monospace is for code, paths, ids and addresses only.
- **A12 Copy.** No marketing words (seamless, powerful, effortless, unlock, supercharge, all-in-one), no invented numbers or testimonials, no cheerful empty states, no `!`. A sentence that could be on any SaaS site is rewritten to say the specific thing.
- **A13 Helper text** only where the field is not obvious. No tooltip on a control whose label already says it. An icon-only button that is not obvious has a tooltip with the verb.
- **A14 Over-building.** No footer, no extra settings page, no coming-soon section, no disabled placeholder button. A toast is for a long action or a failure, not for every click. A skeleton or an animation is for content that actually loads.
- **A15 Equal rhythm.** Not every section the same height and padding: the group heading, the card and the footer note have their own spacing from the scale.

### B. Bad in any style

- **B1 Spacing** is only from the scale (4, 8, 12, 16, 24). Related things are closer than unrelated things: a label is nearer to its control than to the previous row, a card's description nearer to its title than to the next card. Inner padding is not bigger than the gap between cards without a reason.
- **B2 Sizes.** Titles are clearly bigger than rows, rows than meta. Buttons that sit together are one height, and a button next to an input is the input's height. Icons in one row are one size. Reading text is capped at about 75 characters wide (descriptions wrap, they do not run across 900px).
- **B3 Hierarchy.** A page made of several sections has no filled button: each section's add button is outlined, and a filled one is for the single main action of a page, a section that stands alone or a dialog. Empty lists do not show the filter, select all or bulk buttons, because a disabled placeholder says nothing. One primary action in an area. Not everything bold and not nothing bold. Two things the same size, weight and color are one thing too many.
- **B4 Contrast** at least 4.5:1 for text and 3:1 for large text and icons, in dark and in light. Dim text is for what is secondary, never for what the person needs. No pure black on pure white.
- **B5 State by color alone.** A state has text or an icon as well (*Failed*, a dot with a label).
- **B6 Alignment.** Icon and text are vertically centered, rows share one left edge, numbers are right-aligned with tabular figures, long paragraphs are not centered or justified.
- **B7 Consistency.** Same kind of action, same button style. One capitalization rule (Title Case for titles and text buttons, sentence case for the rest).
- **B8 Forms.** The label is not the placeholder, there is a visible focus ring, an error is next to its field, a disabled primary says why when it is not obvious, a required field is marked when not all are required.
- **B9 States.** Every interactive thing has hover, active, focus and disabled. Every list and page has loading, empty and error. Nothing jumps when content arrives or on hover.
- **B10 Responsive.** Under 800px the sidebar is the 52px rail and opens over the page, never beside it. No sideways scroll at 1440, 760 or 375. A table or a code block that is too wide scrolls inside its own box. Two columns collapse to one. A heading does not wrap to one word per line.
- **B11 Overloading.** An element does not have a border, a shadow and a background tint all at once (a card has a border and the card color, the shadow is the theme's one level). Animations are under 300ms and never block a click.

## Behavior

- **Feedback within 100ms.** Show the busy state first, then do the work off the UI thread.
- **Confirm only what hurts.** Starting, stopping and filtering never ask. Deleting does.
- **Remember what the user arranged**: order, collapsed sections, layout, last folder.
- **Never lose work silently.** If something fails, keep the form and say why.
- **Shortcuts**: `Ctrl+K` goes to the tool search, `Ctrl+J` opens the assistant, `Esc` closes a dialog or a panel. A dialog takes the keyboard when it opens (its first field has focus), and Tab stays inside it.
- **Keyboard and screen readers**: every control reachable by keyboard, a visible focus ring, an accessible name on every icon button, status conveyed by text as well as color.
- **Safe by default.** Test destructive features on a copy, not on the real thing.

## Interaction states

Every interactive thing answers the pointer and the keyboard in the same way, from shared rules instead of its own:

- **Hover** is a background tint (`--hover`, or `--card-hover` on a card), never movement or a scale. **Focus** is the 2px `--accent` ring (`:focus-visible`), kept visible and never removed with `outline: 0` unless another ring replaces it. **Active or selected** is `--active` plus, in navigation, the 3px accent mark. **Disabled** is dimmed and not focusable; if it is not obvious why, say why. **Busy** is a spinner or a locked row, **empty** is one quiet line, **error** is a toast or banner with what to do.
- A link navigates and a button acts. A whole card that opens something is one link, not a card with a button inside it. A control that expands something is a button with `aria-expanded`.
- Anything shown only on hover (a chevron, a delete icon) is also shown on keyboard focus, and is never the only way to do something.
- Every icon-only control has an accessible name. Do not state a status with color alone.

## Reuse and tokens

- Look for a shared piece before writing one: `ToolGrid`, `ToolCard`, `SettingCard`, `SettingExpander`, `OptionHeader`, `PageShell`, `Banner`, `.run-row`, `.run-empty`, the `.sc-*` and `.opt-*` classes. If one almost fits, extend it for everyone instead of copying it.
- A new visual pattern needs a reason a reviewer can read in one sentence (what the existing pieces cannot do). Without one, use the existing pattern.
- Colors come from the theme variables, sizes from `--fs-*`, `--h-ctl`, `--gap-*` and the layout tokens at the top of `app.css` (`--w-home`, `--card-min`, `--fab-clear`). A value that two places need becomes a token; a number written in a component is a smell. Do not copy a rule from another component, share its class.
- Shared UI knows nothing about a particular extension: no extension id, group name or title in the host's markup, CSS or code. An extension's styles are prefixed and cannot restyle the shell.

## Extensions

Extensions get the same look by using the same variables, the same wording rules and the same class names (`.run-row`, `.run-empty`, `.banner` and so on). Host components such as `PageShell` and `Banner` are not available to extensions: use `MudAlert`, and the shared CSS classes, to get the same result. Keep class names for your own pieces specific (a prefix such as `jd-`).

- **Hierarchy.** Make the one important thing on a page (the primary action, the result, the status) the first thing seen, by size, weight, position and contrast, and let the rest recede. If everything is bold or boxed, nothing is.
- **Dynamic content.** Lists have any length, titles any length and text any language. Use `min-width: 0`, ellipsis or wrapping, and `max-height` with scrolling inside the box instead of widths and heights that the content has to fit. A page works at 760px wide, and under it a table or code block scrolls in its own box.
- **Stay in your box.** The page draws only inside the area the app gives it, and does not set styles on `body`, `.mud-*` or the host's classes.

## Verification

A UI change is checked by looking at it, not by reading the code. Render the page in a browser (`seneschal_extension_preview` for an extension, a headless or real browser for the app) and look at the screenshot.

- Dark and light, at 1440, about 760 and 375 wide: nothing scrolls sideways, nothing is clipped or overlaps.
- Every surface the change touches, not only the page: open the dialogs, menus, popups and the empty and error states, and look at them. A page that is checked while its dialog is not has not been checked.
- The shape of the data changes the page: one item, many items, an empty list, a very long title and description, and the busy and error states.
- Expanded and collapsed (sidebar groups, sections, expanders), and the page with the sidebar as a rail.
- The keyboard: Tab reaches every control in a sensible order, the focus ring is visible, Enter and Space act, and Esc closes.
- Say what was checked and what was not. Never write that a screen was verified when it was only compiled.

## Checklist for a change

- [ ] Every string is short, has no trailing punctuation and is in the right case.
- [ ] Spacing is on the 4px grid and radii are 6 (cards) or 4 (controls).
- [ ] No color or font size is written by hand.
- [ ] Looks right in dark and in light.
- [ ] Empty, loading, busy, error and long-text states are handled.
- [ ] Icon buttons have a tooltip and an accessible name.
- [ ] Nothing explains what is already obvious, and nothing is said twice.
- [ ] An on or off setting is the pill switch with a gap to its label.
- [ ] A dialog has *Cancel* and a primary named with a verb, 24px padding, fields 16px apart, and every description starts where its label starts.
- [ ] Shared components and tokens were used, and any new pattern has a one-sentence reason.
- [ ] Works with one item and with many, with a long title, and under 800px wide.
- [ ] Hover, focus, disabled, busy, empty and error all exist, and the keyboard reaches everything.
- [ ] The page was rendered and looked at, in dark and light.
