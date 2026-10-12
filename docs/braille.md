# Braille

Vox shows what has focus, the caret's line and the browse cursor's line on a refreshable braille
display, and takes commands and typing from its keys (spec: `specs/braille.md`).

## Displays

`BrailleDisplayManager` looks for a display every 3 seconds while none is connected, so a display
plugged in (or BRLTTY started) later is found, and lets a lost one go. The `BrailleDisplay`
setting (Settings, Braille tab) chooses the driver:

| Setting | Driver |
|---|---|
| Automatic (`null`) | HID braille first, then BRLTTY |
| `HID` | `HidBrailleDisplay`: the Windows HID braille usage page (0x41), USB or Bluetooth, no driver to install |
| `BRLTTY` | `BrlttyDisplay`: BRLTTY's BrlAPI on `127.0.0.1:4101`, for displays HID doesn't cover (see `brltty.md`) |
| `None` | no braille |

Braille is not used on the sign-in, lock and UAC screens (secure mode).

## Translation

Braille tables come from liblouis (LGPL), an optional component like eSpeak NG:

1. Get a Windows build of liblouis (64-bit `liblouis.dll` and its `tables` folder), for example
   from <https://github.com/liblouis/liblouis/releases>.
2. Copy `liblouis.dll` and the `tables` folder into `%LOCALAPPDATA%\Vox\components\liblouis`
   (`userConfig\components\liblouis` in a portable copy).
3. Choose a table on the Braille tab (default: Unified English Braille grade 2, `en-ueb-g2.ctb`).

Without liblouis Vox shows 8-dot US computer braille (`ComputerBrailleTranslator`), whatever the
table setting. With a contracted table, the word at the cursor is shown uncontracted while editing
(setting "Show the word at the cursor uncontracted").

## What is shown

- **Focus**: the focused object as name, role and states with short labels from
  `assets/braille/labels-en.json` ("Remember me chk ( )", "Search edt", "Products h2", "Home lnk v"),
  after its context (the window, dialogs, groups and lists around it). The focus-context setting
  chooses how much: fill the display, only when panning back, or only the context that changed.
- **Text fields**: the caret's line, with the cursor and the selection (dots 7–8).
- **Web pages in browse mode**: the browse cursor's line, with the role and states after each link,
  button, form field, heading and graphic ("Home lnk About lnk").
- **Review**: after a review command, the review cursor's line, until the focus or caret moves.
  `Insert+Ctrl+T` (or Space+dots 2-4-5 on the display) switches between following both, only the
  focus, and only the review cursor.
- **Messages**: what Vox says that didn't come with a line of its own (mode changes, the time,
  live regions, notifications) is shown for the message timeout (default 4 seconds; 0 turns
  messages off), then the line returns. Pressing a key dismisses it.

The cursor's shape (dots 7–8, dot 8, all dots) and blinking are settings. Panning never splits a
word unless word wrap is off.

## Keys

Display keys are resolved through `assets/braille/keymaps/common.json`, overlaid by the driver's
own file (`hid.json`, `brltty.json`). An entry maps a chord of the driver's key names to an
`action` (PanLeft, PanRight, Route, PreviousLine, NextLine, CycleTether, Space, Backspace, Enter),
a Vox `command` (any `NavigationCommand`) or a `key` sent to the application ("Shift+Tab").

| Keys | Does |
|---|---|
| Pan left / right | Show the previous / next part of the line; past its start or end, the previous line (shown from its end) or the next line |
| Rocker up / down | Previous / next line |
| Routing key | Move the caret (text fields), the browse cursor (pages) or the review cursor to that cell; on the browse cursor's cell, activate what is there; on a focused object, activate it |
| Dots | Typed as braille: each cell at once in uncontracted braille; in contracted braille the word is translated when Space or Enter ends it |
| Space, Dot 7, Dot 8 | Space, Backspace (the last cell not yet translated, else the Backspace key), Enter |
| Space+Dot 1 / 4 / 3 / 6 | Up / Down / Left / Right arrow |
| Space+Dot 2 / 5 | Home / End |
| Space+Dots 4-5, Space+Dots 1-2 | Tab, Shift+Tab |
| Space+Dots 1-5 (e), Space+Dots 1-4-5 (d) | Escape, Delete |
| Space+Dots 1-2-5 (h), 1-2-3 (l) | Next heading, next link |
| Space+Dots 1-2-4 (f), 1-4 (c), 2-3-4-5 (t) | Find, say focus, say title |
| Space+Dots 2-3-4 (s), 1-3-4 (m) | Say All, Vox menu |
| Space+Dots 1-2-3-6 (v) | Switch browse and focus mode |
| Space+Dots 2-4-5 (j) | Switch what braille follows |
| Space+all six dots | Stop speech |

BRLTTY reports its own commands rather than the display's keys: `brltty.json` maps them
(lineUp/lineDown, panLeft/panRight, routing, toggleTether, typed dots, keyboard keys).

## Code

- `Braille/BrailleController` holds the lines (focus, caret or browse cursor, review, message),
  picks the one the tether shows, and runs display keys. `BrailleFeed` turns focus events, focused
  text reads (UIA thread), browse cursor moves (after each pipeline event) and review moves into
  lines; `Vox.App/BrailleService` wires them up.
- `BraillePresenter` builds lines as text plus cursor, selection and source offsets;
  `BrailleViewport` translates a line, keeps the window panned over it and draws the cursor and
  selection; `BrailleLabels` holds the role and state labels.
- `IBrailleRouter` (`BrailleRouter`) carries routing keys to the caret (UIA `Select`), the browse
  cursor (`BrailleRouteEvent` on the pipeline), the review cursor and the focus (its default action).

## Arabic and Hebrew

Braille shows text in reading order, as Arabic and Hebrew braille are read left to right. The
invisible direction marks that mixed-direction text carries (U+200E, U+200F, embeddings and
isolates) are left out of the display; the cursor and routing keys still land on the right
characters. Changing Vox's language also changes the braille table when it was the old
language's usual one.
