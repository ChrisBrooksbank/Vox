# Desktop Text & Controls

## Overview

Make Vox useful outside web documents: caret and selection tracking in native edit controls through UIA `TextPattern`, terminals, and the focus, state, value and selection announcements users expect in Explorer, Settings, dialogs and menus. Workstream W2 of `COMMERCIAL_PARITY_PLAN.md` (Milestone A).

## User Stories

- As a blind user, I want to hear the character, word or line I move to when I arrow through a text box in any app
- As a blind user, I want to hear what I select and what I delete so I can edit confidently
- As a blind user, I want to hear new output in a terminal
- As a blind user, I want to hear when a check box toggles, a slider moves, a list selection changes, or a dialog opens
- As a blind user, I want "where am I" commands: window title, focus, status bar, time, battery

## Requirements

### Text editing

- [ ] `ITextDocument` abstraction over a text source with ranges, caret, selection and attributes; `VBufferCursor` gets an adapter so browse mode and native edits share one cursor/command layer
- [ ] `UIATextDocument`: `ITextDocument` over `IUIAutomationTextPattern`/`TextPattern2` (all calls through `UIAThread`)
- [ ] `TextCaretTracker`: subscribes to `TextSelectionChanged` and `TextEditTextChanged` on the focused edit; after a caret-moving key, speaks the unit matching the key (Left/Right: char; Up/Down: line; Ctrl+Left/Right: word; Ctrl+Up/Down: paragraph; Home/End, Ctrl+Home/End: line)
- [ ] Fallback when an edit has no `TextPattern`: caret position from `GetGUIThreadInfo` and text from `ValuePattern`
- [ ] Selection speech: "selected X" / "unselected X" for Shift+movement, "all selected" for Ctrl+A, "selection cleared" when appropriate
- [ ] Deletion echo: Backspace/Delete speak the removed character (or word with Ctrl)
- [ ] Commands (mode Any): read current line, word, char; read selection (Insert+Shift+Up); read formatting at caret (Insert+F: font, size, bold/italic/underline, colour, spelling error)
- [ ] Say All over an `ITextDocument` (reuse `SayAllController`)
- [ ] Spelling errors: say "misspelled" when the caret enters a word with the spelling-error text attribute; optional earcon instead
- [ ] Typing echo works in native edit controls, not only in web documents

### Terminals

- [ ] `TerminalMonitor`: for Windows Terminal and conhost, diff text on `TextChanged` and speak new lines; throttle bursts (max one utterance per 100 ms, collapse >20 lines to "N lines of output" with the last line read)
- [ ] Don't echo the user's own typed characters back as output

### Controls and events

- [ ] Speak the window title when the foreground window changes
- [ ] Dialog auto-read: when a window with control type Window and dialog-like class or `IsDialog` opens, read its title and static text that has no focus target
- [ ] `PropertyChangedEvent` handling for ToggleState, ExpandCollapseState, Value.Value, RangeValue.Value, IsEnabled, Name on the focused element
- [ ] Progress bars: speak percentage (setting: every 10 %, every 25 %, beep, off); background progress bars only when the setting allows
- [ ] `SelectionItem` / `Selection` changes in lists, grids, tabs and trees speak the new item
- [ ] Position info: "3 of 10" from `PositionInSet`/`SizeOfSet`, "level 2" from `Level`, controlled by verbosity profile
- [ ] Menus: "menu" on open, "leaving menu" on close, submenu entry, shortcut and accelerator text
- [ ] Tooltips, Start menu results, Alt+Tab and Win+Tab switchers, virtual desktop switch announcements

### Where am I (mode Any)

- [ ] Insert+T title, Insert+Tab focused element, Insert+End status bar, Insert+F12 time (twice: date), Insert+Shift+B battery, Insert+B read foreground window
- [ ] All new bindings in `assets/config/default-keymap.json`

## Acceptance Criteria

- [ ] Unit tests with a fake `ITextDocument`: unit selection per key, selection/unselection messages, deletion echo, spelling-error report
- [ ] Unit tests for terminal diffing and throttling
- [ ] Unit tests for property-change announcement text and progress-bar throttling
- [ ] Manual: in Notepad, write and edit a paragraph hearing every move and change; rename a file in Explorer; change a toggle and a slider in Settings

## Out of Scope

- Microsoft Office specifics (office-apps.md)
- Review cursor over non-focused text (review-object-navigation.md)
