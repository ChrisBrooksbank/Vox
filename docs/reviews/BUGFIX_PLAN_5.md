# Bug Fix Plan — Round 5

This is a fifth static review, of `master` at `2c027f8`, done on 2026-09-28 after the round-4 fixes were merged. It looks beyond the mode and focus machinery of earlier rounds, at what is actually spoken for form controls, how the buffer stays current after the user changes something, key handling while focus moves between the page and the rest of the browser, and the speech queue. The baseline is 576 tests passing on Linux, plus 14 that need Windows. Items marked **(verify)** depend on Windows, UIA, Chromium or SAPI behaviour and need confirming on Windows.

## Status (2026-09-28)

All 15 tasks are implemented, with regression tests for the testable parts (643 tests pass on Linux, 67 of them new; the 14 that need Windows are unchanged). The **(verify)** items, and the code that only runs on Windows, still need checking on Windows with Chrome or Edge: the `ValueValue`/`ValueIsReadOnly`, `IsRequiredForForm` and `LegacyIAccessibleState` values Chromium reports; the `FocusInDocumentEvent` path; the re-capture on state, name and value changes; and the default-voice reset.

Where the implementation made a specific choice:
- Task 1: a form field's value is spoken after its control type and read in the buffer as "label value". Values are shown for Edit, ComboBox, Spinner, Slider and ProgressBar, and the matching ARIA roles. They are skipped when equal to the name, and never shown for password fields.
- Task 2: value changes are ignored when the focused element's value is editable (`ValueIsReadOnly == false`). Unknown counts as read-only, so it is spoken, except for Edit and Document.
- Task 3: `Value` changes also trigger a re-capture, so browse mode reads what was typed. The tracker's debounce limits this to about one capture per second while typing.
- Task 5: identical text is a new announcement after 1.5 s of quiet. Duplicate events inside that window restart the window.
- Task 6: Ctrl+Up/Down move by paragraph (a whole `'\n'`-terminated run with text), as in NVDA. Home/End speak the character reached; Ctrl+Home/End speak the line.
- Task 7: `MaxLineLength` is a setting (default 100, 0 turns splitting off) and is read at use time.
- Task 8: NVDA behaviour. Focus mode is left for any focused control that doesn't need it.
- Task 12: a selection or value change repeating the text spoken for the focused control in the last 300 ms is skipped.
- Task 15: T/Shift+T announce "name, table" and the table's first line.

Tasks use the `IMPLEMENTATION_PLAN.md` checkbox format and are ordered by severity. Each fix needs a regression test in `tests/Vox.Core.Tests/` where the code is testable.

## P0 — Common flows broken

### 1. Form field values are never spoken
Neither `FocusChangedEvent` nor `IVBufferElement` carries the UIA `ValueValue` property, and none of the cache requests fetch it (`UIAProvider.cs:60`, `UIAProvider.cs:87`). As a result:
- Tabbing to a `<select>` says "Country, combo box" but never the selected option.
- Tabbing into a pre-filled text box, or returning to one, doesn't say what is in it.
- Sliders and spin buttons don't say their value.
- In Browse mode, arrowing or pressing F over a filled-in form reads only the labels. Chromium's plain `<input>` has no control-view text child, so the typed text isn't in the buffer at all.

NVDA says "Country, combo box, United Kingdom" and "Search, edit, hello".
- [x] Add `UIA_ValueValuePropertyId` (30045) to the focus and subtree cache requests. Carry it as `Value` on `FocusChangedEvent`, `IVBufferElement`/`UIAElementSnapshot` and `VBufferNode`.
- [x] `AnnouncementBuilder`: after the control type (and states), append the value for Edit, ComboBox, Spinner, Slider and ProgressBar, and for the equivalent ARIA roles. Skip it when empty or equal to the name. Never read the value of a password field.
- [x] `VBufferBuilder`: emit a form field's value as its text (after the label) so line navigation reads it. Keep the value out of `Name`, so quick navigation and the Elements List still show the label.
- [x] Tests: focus announcement of a combo box with a value; an edit with a value; a password edit with a value (not spoken); buffer text for a labelled edit with a value.

### 2. Typing in an autocomplete search box speaks the whole text after every key (verify)
`HandlePropertyChanged` skips value changes only for `Edit` and `Document` (`BrowseModeController.cs:340`). A text input with `role="combobox"` is exposed by Chromium as a `ComboBox`, and that includes Google's and Bing's search boxes and most autocomplete fields. Its `ValueValue` changes on every keystroke, and each change is spoken at `Interrupt` over the typing echo, so typing "news" is heard as "n", "ne", "new", "news".
- [x] Add `UIA_ValueIsReadOnlyPropertyId` (30046) to the focus cache request and carry it on `FocusChangedEvent`. Treat a focused element whose value is editable like an Edit, and leave its value changes to typing echo. Keep speaking value changes of read-only combo boxes (`<select>`), sliders and spin buttons.
- [x] Test: a focused editable ComboBox value change is not spoken; a read-only ComboBox value change is.
- [x] (verify) the ControlType and `ValueIsReadOnly` Chromium reports for `<input role=combobox>` and for `<select>`.

## P1 — Wrong behaviour in common use

### 3. The buffer keeps old check-box, radio and selection states after the user changes them
Toggle and selection states are captured into the buffer (round 3), but a change to them only triggers speech. `ScreenReaderService.OnPropertyChangedProcessed` re-captures the element only for `ExpandCollapseState` (`ScreenReaderService.cs:237`), and Chromium raises no StructureChanged event for a state change.

So after checking a box (Enter in Browse mode, or Space in Focus mode), arrowing back over it in Browse mode still says "not checked". Selecting a radio button leaves the old one reading "checked", and switching tabs leaves the old tab reading "selected". The same applies to `Name` changes of elements that aren't focused, such as a button whose label changes from "Play" to "Pause", or a counter.
- [x] Re-capture the changed element (`_documentTracker.OnStructureChanged`) for `ToggleToggleState`, `SelectionItemIsSelected` and `Name` changes too, not only `ExpandCollapseState`. The tracker's debounce and full-re-capture threshold already cover bursts.
- [x] Test: in `BrowseModeController`, a subtree update for a toggled check box changes what its announcement says. `ScreenReaderService` wiring is manual (verify on Windows).

### 4. Keys typed straight after leaving the page are swallowed or run browse commands
The document stays "active" until the tracker has walked the new focus's ancestors and posted `DocumentChangedEvent(null)`. That path is: pipeline focus coalescing, a UIA-thread hop, up to 64 cross-process parent calls, and back through the pipeline. Until it completes, `KeyInputDispatcher` resolves keys in the Browse context (`KeyInputDispatcher.cs:140`).

After Ctrl+L, Alt+D, F6 or Alt+Tab from a page in Browse mode, the first letters typed into the address bar or the other app are swallowed as "unbound typing keys". Bound keys (H, K, arrows, Space, Enter) run quick-nav commands against the page instead of reaching the new focus. `HandleFocusChanged` already knows focus has left the buffer (`BrowseModeController.cs:262`), but it returns without doing anything.
- [x] When a focus event's element is not in the current document, deactivate browse-mode key handling at once: raise `DocumentActiveChanged(false)` but keep the document and cursor.
- [x] Have the tracker post a small "focus is still in the current document" event from `DetectDocument`'s same-document paths (the `_capturedIds` shortcut and the same-runtime-id return). That event reactivates the document for elements the buffer doesn't contain yet, such as a newly added dialog.
- [x] Tests: document loaded, focus event for an element outside it → document inactive immediately, cursor kept. A later "still in document" event → active again, same position.

### 5. A live region that repeats the same message is only spoken the first time
`LiveRegionMonitor.Evaluate` drops text equal to the region's last text (`LiveRegionMonitor.cs:75`). Clearing the region never resets that last text: `UIAEventSubscriber` doesn't post empty text (`UIAEventSubscriber.cs:330`), and `EventPipeline` returns early for it before reaching the monitor (`EventPipeline.cs:289`). Pages commonly clear a status region and set the same text again to re-announce it ("Item added to cart", "Saved", "1 new message"). Vox speaks it once and then never again for that region.
- [x] Post live-region changes with empty text, and let the monitor record them as `LastText = ""` without speaking.
- [x] Also allow identical text again once a short interval has passed since it was last spoken (e.g. 1.5 s), because a fresh LiveRegionChanged event with the same text means the page announced it again.
- [x] Tests: "Saved", then "", then "Saved" → spoken twice. "Saved" twice within the interval → once. "Saved" twice beyond the interval → twice.

### 6. Standard NVDA reading keys are missing in Browse mode
The keymap binds word movement to Ctrl+Down/Up (`default-keymap.json`). NVDA, whose conventions the project follows, uses Ctrl+Left/Right for words and Ctrl+Up/Down for paragraphs. Home, End, Ctrl+Home and Ctrl+End are not bound at all. They pass to the browser, which scrolls the page but leaves the virtual cursor where it was, so the next Down arrow reads from the old place. Ctrl+Left and Ctrl+Right do nothing.
- [x] Bind Ctrl+Right/Left to `NextWord`/`PrevWord`.
- [x] Add and bind `StartOfLine`/`EndOfLine` (Home/End) and `TopOfDocument`/`BottomOfDocument` (Ctrl+Home/Ctrl+End), implemented on `VBufferCursor`.
- [x] Ctrl+Up/Down move by paragraph (`PrevParagraph`/`NextParagraph`), as NVDA does.
- [x] Tests: keymap resolution for the new bindings; cursor movement for each new command, including boundary cues.

## P2 — Smaller issues

### 7. Down arrow reads a whole paragraph as one line
A line is everything up to the next `'\n'` (`VBufferCursor.cs:170`), and inline runs are joined onto one line (round 3). A long paragraph is therefore one "line": Down arrow reads all of it, and Say All can't be stopped at a point smaller than a paragraph. NVDA splits browse-mode lines at 100 characters by default. Split lines longer than a limit (a setting, default 100) at the last word boundary before the limit. Do this in `VBufferCursor` line movement and `ReadCurrentLine`/Say All; the flat text needn't change.

### 8. Focus mode stays on after tabbing from a text box to a check box or radio button
`NavigationManager.HandleFocusChanged` leaves Focus mode only when the new focus isn't a form field at all (`NavigationManager.cs:82`). Check boxes and radio buttons are form fields that don't need Focus mode. Tabbing onto one from an edit field therefore stays in Focus mode, while tabbing onto the same control from Browse mode stays in Browse mode. The mode depends on where the user came from, and H or arrows then behave differently on the same control. Use `NeedsFocusMode`, as the Browse → Focus direction does, so automatic switching is symmetric (NVDA returns to Browse mode here).

### 9. Queued high-priority speech waits behind lower-priority utterances drained in the same batch
`SpeechQueue` sorts each drained batch by priority, then speaks every group in it before reading the channel again (`SpeechQueue.cs:158`). If several `Low` polite live-region updates were drained together, a `High` utterance enqueued meanwhile waits until all of them have been spoken: a state change, a user-toggled mode, an assertive region or an important notification. Keep one pending list across iterations, and after each spoken group merge in newly queued items and re-sort, so a later `High` overtakes queued `Low`/`Normal` items.

### 10. "Visited" is never announced (verify)
Visited state is read from an ARIA property named `visited` (`VBufferBuilder.cs:226`, `UIAEventSubscriber.cs:175`). No such ARIA property exists, and Chromium doesn't put one in `AriaProperties`, so the `AnnounceVisitedLinks` setting has no effect. Read `STATE_SYSTEM_TRAVERSED` (0x800000) from `LegacyIAccessibleState` (30100) in the focus and subtree cache requests instead. Similarly, prefer `IsRequiredForForm` (30025) over parsing `required` from `AriaProperties`, so native `required` inputs count. Verify both properties on Chromium and Gecko.

### 11. A buffer update can join a paragraph onto the next one
`JoinInlineRuns` only ever turns `'\n'` into `' '` (`VBufferBuilder.cs:160`). After an incremental update removes the last inline element of a run, or leaves it with no text (`IncrementalUpdater.cs:175`), the previous sibling keeps the space it was given when it was joined. Its line then runs straight into the next block. Before re-joining after an update, restore `'\n'` at the end of the sibling nodes around the splice point, then join again.

### 12. The same option can be spoken twice when arrowing through a collapsed combo box
For a collapsed `<select>`, Chromium raises `ElementSelected` on the option and a `ValueValue` change on the combo box. Both are spoken at `Interrupt` (`BrowseModeController.cs:344`, `BrowseModeController.cs:358`), so each arrow press stutters ("Ba… Banana"). Skip a value change whose text equals the element-selected name spoken in the last ~300 ms, or the reverse.

### 13. Clearing the voice setting doesn't go back to the default voice
`ApplySettings` only calls `SetVoice` when `VoiceName` is non-empty (`ScreenReaderService.cs:199`). Changing it back to empty leaves the old voice selected until restart. When it becomes empty, re-select the startup voice (`SelectOneCoreVoice`'s choice).

### 14. Typing echo word-buffer details
- Delete removes the *last* character of the word being typed (`TypingEchoHandler.cs:118`), but Delete deletes forwards. Clear the word instead, or ignore Delete.
- A pending dead key survives focus changes (`ResetWord` at `TypingEchoHandler.cs:91` doesn't clear `_pendingDeadKey`), so an accent pressed in one field combines with the first letter typed in the next. Clear it in `ResetWord`.

### 15. T and Shift+T are bound but always play the boundary sound
`QuickNavHandler` doesn't index tables (`QuickNavHandler.cs:107`), although the keymap binds T/Shift+T and the spec lists table navigation. Index `Table`/`DataGrid` control types and `table`/`grid` roles in `VBufferDocument`, and navigate them like the other collections, announcing "table" with the table's name.
