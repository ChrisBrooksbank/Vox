# Review Cursor, Object Navigation & Mouse

## Overview

Let users explore anything on screen, not just what has keyboard focus: object navigation through the UIA tree, a review cursor over text, mouse tracking and simulated clicks, and a laptop keyboard layout. Workstream W3 of `COMMERCIAL_PARITY_PLAN.md` (Milestone B).

## User Stories

- As a blind user, I want to read a status bar or label that I can't Tab to
- As a blind user, I want to click a control that has no keyboard access
- As a blind user, I want to review text (line, word, character, spelled) without moving the caret
- As a blind laptop user, I want all of this without a numpad

## Requirements

- [ ] `ObjectNavigator`: navigator object with parent / first child / next / previous sibling over the UIA control view (all calls through `UIAThread`); speaks the new object with `AnnouncementBuilder`; boundary cue at the ends
- [ ] Commands: move navigator to focus (Insert+Numpad-), move focus to navigator (Insert+Shift+Numpad-), report navigator (Insert+Numpad5), activate navigator (Insert+Numpad Enter)
- [ ] Simple-review mode setting: skip layout-only objects (Group/Pane with no name)
- [ ] `ReviewCursor` over an `ITextDocument` (see desktop-text-and-controls.md) for the navigator object, the focused document, or the whole window
- [ ] Review commands: Numpad 7/8/9 line, 4/5/6 word, 1/2/3 char; Shift+Numpad7/9 top/bottom; press twice to spell, three times to spell phonetically (NATO)
- [ ] Review follows focus and caret (setting), and can be tethered to the virtual buffer in browse mode
- [ ] Mouse tracking (setting, off by default): speak the element under the pointer, throttled to one lookup per 100 ms with `ElementFromPoint`; report text unit under the pointer (setting: object / line / word)
- [ ] Mouse commands: route mouse to navigator (Insert+Numpad/), left click (Numpad/), right click (Numpad*), lock left button
- [ ] Laptop keymap: alternative bindings using CapsLock as modifier and no numpad (NVDA laptop layout); `KeyMap` supports loading a named layout; first-run wizard asks Desktop or Laptop
- [ ] Copy navigator object text to clipboard (Insert+Ctrl+C)

## Acceptance Criteria

- [ ] Unit tests for `ObjectNavigator` over a mock element tree (moves, boundaries, simple-review skipping)
- [ ] Unit tests for `ReviewCursor` units and repeat-press spelling
- [ ] Unit tests that the laptop layout resolves every command the desktop layout has
- [ ] Manual: read Notepad's status bar, click a toolbar button with no keyboard access, review terminal scrollback on a laptop

## Out of Scope

- OCR-based screen review (ocr-ai.md)
- Touch gestures (later)
