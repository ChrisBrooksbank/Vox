# Braille

## Overview

Braille output and input for refreshable braille displays: liblouis translation, the Windows HID braille standard, BRLTTY for older displays, focus-context presentation, routing keys, panning and braille keyboard input. Workstream W6 of `COMMERCIAL_PARITY_PLAN.md` (Milestone C).

## User Stories

- As a braille user, I want what has focus and where my cursor is shown on my display
- As a deafblind user, I want to use Vox fully with speech off
- As a braille user, I want routing keys to move the caret or activate an element
- As a braille user, I want to type in contracted braille on my display's keyboard

## Requirements

- [ ] `IBrailleDisplay`: `IsConnected`, `CellCount`, `Write(cells, cursorPos)`, `KeyPressed` event (with display-specific key names)
- [ ] `IBrailleTranslator` and `LibLouisTranslator` (P/Invoke to `liblouis.dll`, LGPL, loaded dynamically): forward translation with cursor mapping, back-translation for input; tables chosen in settings (default `en-ueb-g2.ctb`, plus computer braille `en-us-comp8.ctb`)
- [ ] Show the uncontracted word at the cursor when contracted braille is on (setting)
- [ ] `HidBrailleDisplay`: Windows HID braille usage page driver (auto-detect)
- [ ] `BrlttyDisplay`: client for BRLTTY's BrlAPI for serial/USB displays not covered by HID (optional component)
- [ ] `BrailleDisplayManager`: auto-detect, reconnect on plug/unplug, choose driver in settings
- [ ] `BraillePresenter`: builds the line from the focused object plus changed ancestors (focus context: fill display / only when scrolling back / changed context), and from the caret line in edits and the browse-mode cursor line
- [ ] Abbreviated role and state labels (`lnk`, `btn`, `h2`, `☐`) from a localisable table
- [ ] Cursor shape and blink settings; selection shown with dots 7–8
- [ ] Panning left/right, word wrap, tether to focus or review cursor
- [ ] Routing keys: move caret / virtual cursor to the cell, second press activates
- [ ] Flash messages: spoken announcements also shown for N seconds (setting), then the line returns
- [ ] Braille input: chords → characters through back-translation, chord commands (space+dots) mapped to `NavigationCommand`s, per-display key maps in `assets/braille/keymaps/*.json`
- [ ] Braille settings in the settings dialog

## Acceptance Criteria

- [ ] Unit tests with a fake display and fake translator: presenter output for focus, edit and browse cases; panning; routing; flash message timeout; key map resolution
- [ ] Integration test of liblouis translation (skipped when the DLL is absent)
- [ ] Manual: with speech muted, read and reply to an email and browse a news site on a 40-cell HID display and on a BRLTTY-connected display

## Out of Scope

- Braille on secure screens until robustness-secure-desktop.md secure mode supports it
- Nemeth/UEB maths braille beyond what MathCAT provides
