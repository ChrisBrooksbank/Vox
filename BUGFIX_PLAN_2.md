# Bug Fix Plan — Round 2

This is a second static review of `master` at `f883059`, done on 2026-09-28 after the fixes in `BUGFIX_PLAN.md` were merged. It covers code that round didn't read closely (Elements List, settings reload, live regions, sounds) and takes a fresh look at the code added in round 1 (`BrowseModeController`, `BrowseDocumentTracker`, key suppression). Items marked **(verify)** depend on runtime behaviour of Windows, UIA, Chromium or SAPI and need confirming on Windows.

Tasks use the `IMPLEMENTATION_PLAN.md` checkbox format and are ordered by severity. Each fix needs a regression test in `tests/Vox.Core.Tests/` where the code is testable.

## P0 — Broken behaviour or privacy

### 1. Typed characters in password fields are spoken
`TypingEchoHandler` echoes every printable key (`TypingEchoHandler.cs:109`). Nothing tells it the focused element is a password field, so passwords are read aloud.
- [ ] Add `UIA_IsPasswordPropertyId` (30019) to `UIAProvider`'s cache request and carry `IsPassword` on `FocusChangedEvent`.
- [ ] Have `BrowseModeController` track the focused element's password state. While it is set, echo "star" for each character (NVDA convention) and never add characters to the word buffer.

### 2. Focus events cut off the setup wizard's speech
`ScreenReaderService` subscribes to UIA events (`ScreenReaderService.cs:82`) before running the wizard (`ScreenReaderService.cs:95`). `EventPipeline` speaks every focus change as an `Interrupt`, and each `Interrupt` calls `ISpeechEngine.Cancel()`, which cancels the prompt the wizard is speaking directly on the engine. `PromptAsync` treats that as the user pressing a key and waits silently, so the user hears half a prompt.
- [ ] Run the wizard before `SubscribeAsync`, or route the wizard's speech through `SpeechQueue` and suppress pipeline speech while the wizard runs.

### 3. The Elements List jump is undone when focus returns
`OpenElementsList` records the focused element in `_ignoreFocusReturnTo` (`BrowseModeController.cs:318`). The next focus event is the dialog itself, and `HandleFocusChanged` clears the token on any event (`BrowseModeController.cs:177`). When the dialog closes and focus returns to the browser, the virtual cursor jumps back to the old element. The dialog's own focus events also reach `NavigationManager.HandleFocusChanged`, which can change the mode.
- [ ] While `_modalOpen` is set, skip focus-driven cursor sync and mode switching entirely. After the dialog closes, ignore only the first focus event that matches the recorded element.
- [ ] Add a test that sends a focus event for the dialog, then the close event, then the returning focus event, and checks the cursor stays on the selected node.

### 4. Earcon sound files are missing
`assets/sounds/` does not exist and `Vox.App.csproj` only copies config files. `AudioCuePlayer` finds no files, so every cue is silent: browse/focus mode, boundary, wrap and error. The speech-engine and navigation specs require these cues.
- [ ] Add the five Phase 1 files (`browse_mode.wav`, `focus_mode.wav`, `boundary.wav`, `wrap.wav`, `error.wav`). Short generated tones are enough for now.
- [ ] Copy them to `bin/.../assets/sounds/` in the same MSBuild target that copies the config files.

## P1 — Wrong behaviour in common use

### 5. Say All syncing moves the cursor to a stale position
`StopSayAll` runs at the start of every command. It syncs the virtual cursor from `_sayAllCursor` whenever that field is set (`BrowseModeController.cs:290`), even if Say All finished long ago and the user has since moved by focus or quick nav. Pressing H after tabbing somewhere then navigates from where Say All ended.
- [ ] Clear `_sayAllCursor` whenever the cursor is moved by other means (`MoveTo`, document change, subtree change).
- [ ] Only sync from the Say All cursor if reading was in progress, or has just finished and nothing has moved the cursor since.

### 6. Updates made while the page loads are lost
`LoadDocument` captures the subtree (`BrowseDocumentTracker.cs:132`) before scoping StructureChanged events to the document (`BrowseDocumentTracker.cs:139`). Changes made between the two are never seen. Chromium often focuses the document before content finishes loading, so the buffer can stay incomplete until something else changes.
- [ ] Scope the events first, then capture. Duplicate updates are harmless.
- [ ] (verify) Also subscribe to `UIA_AsyncContentLoadedEventId` (20023) on the document and re-capture it when content finishes loading.

### 7. The structure-change debounce never fires on busy pages
`OnStructureChanged` restarts a 300 ms timer on every event (`BrowseDocumentTracker.cs:170`). On pages with a ticker, carousel or animation, the timer never expires, so the buffer never updates.
- [ ] Add a maximum wait: flush at most one second after the first pending change, whatever keeps arriving.

### 8. Changes under elements outside the control view are dropped (verify)
The StructureChanged sender can be a raw-view element that isn't in the control view, such as a Chromium generic container. Its runtime id isn't in the buffer, so `FindInDocument` (`BrowseDocumentTracker.cs:208`) captures an element that `IncrementalUpdater.ApplyUpdate` cannot find, and the update is silently discarded.
- [ ] On the UIA thread, normalise the changed element to its nearest control-view ancestor (`ControlViewWalker.NormalizeElement` or walk parents) before capturing.
- [ ] If the ancestor still isn't in the buffer, fall back to re-capturing the whole document.

### 9. Polite live-region updates are dropped, and whole regions are re-read
- `LiveRegionMonitor` *drops* a polite update that arrives within 500 ms of the previous one (`LiveRegionMonitor.cs:69`). "Loading…" followed quickly by "Done" never announces "Done".
- [ ] Defer the latest text instead: announce it when the cooldown ends. This needs a timer, or flushing on the next event.
- Chromium raises LiveRegionChanged on the region root, and Vox speaks the whole region text each time (`LiveRegionMonitor.cs:50-54` only checks for equality). A chat log re-reads every message whenever one is added.
- [ ] When the new text starts with the previous text, announce only the added part (the default `aria-relevant="additions"` behaviour).
- [ ] Bound the per-source dictionaries (for example with an LRU cap) so long sessions don't grow them without limit.

### 10. A settings file that fails to parse resets live settings to defaults
When `settings.json` changes on disk, `SettingsMonitor.OnFileChanged` calls `_manager.Load()` (`SettingsManager.cs:201`). If parsing fails, `Load` returns defaults (`SettingsManager.cs:69`). A half-written file (editors often save in steps) or a typo therefore resets rate, voice, verbosity and modifier key while Vox is running.
- [ ] Add `TryLoadUserSettings(out VoxSettings)`. On reload failure, keep the current settings and log a warning.
- [ ] Make `Save` atomic: write to a temporary file, then `File.Replace`/`File.Move(overwrite)` (`SettingsManager.cs:83`). A crash mid-write should not corrupt settings and re-trigger the first-run wizard.

### 11. F navigates every list item
`VBufferDocument.FormFieldControlTypes` includes `List` and `ListItem` (`VBufferDocument.cs:43`), and Chromium maps `<ul>`/`<li>` to those control types. Every bullet is treated as a form field. `NavigationManager` has the same list (`NavigationManager.cs:126`), and `IsEditField` includes `List` (`NavigationManager.cs:121`), so Enter on a plain list switches to Focus mode. `VBufferBuilder.FocusableControlTypes` also marks every `ListItem` as focusable (`VBufferBuilder.cs:72`).
- [ ] Remove `List`/`ListItem` from these sets. Recognise real listboxes by ARIA role (`listbox`, `option`) instead.

### 12. Modifier state can get stuck (verify)
`KeyStateTracker` only learns about key-ups the hook sees (`KeyboardHook.cs:259`). The hook sees nothing on the secure desktop: Ctrl+Alt+Del, UAC prompts, Win+L. A modifier held when the desktop switches stays "down" afterwards. Every key then resolves with that modifier, so browse keys and typing echo stop working until the user presses and releases it again.
- [ ] On each non-modifier key-down, reconcile Shift/Ctrl/Alt with `GetAsyncKeyState`. This is one syscall per key and fits the 1 ms budget.
- [ ] Clear the screen reader modifier on session or desktop switch (`WTSRegisterSessionNotification`). Its own async state is unreliable because the key is swallowed.

### 13. The key's key-up is echoed after a command changes the mode
`KeyInputDispatcher` posts every key-up as a `RawKeyEvent` (`KeyInputDispatcher.cs:121`), including the key-up of a key whose key-down ran a command. Enter on an edit field switches to Focus mode, then its key-up reaches typing echo and is spoken as "Return". Space after Insert+Space does the same.
- [ ] Track keys whose key-down became a command (`bool[256]`) and drop their key-ups.

### 14. A key can be swallowed but still treated as typing
The hook decides whether to swallow a key using the mode at press time, on the hook thread (`KeyInputDispatcher.cs:93`). The dispatcher resolves the command later, on the consumer thread, using the mode at that time. A mode change in between, such as typing straight after Insert+Space or Enter, can make a key that was swallowed become a `RawKeyEvent` (the key is lost), or a key that was passed through also run a browse command.
- [ ] Make the hook's decision authoritative. Have the filter return the resolved binding and carry it on the event (for example `KeyEvent.Command`/`Suppressed`), so the dispatcher uses the press-time result instead of resolving again.

### 15. Combo box, list and value changes are silent (verify)
PropertyChanged events are only logged (`EventPipeline.cs:188`). There is no handling for `ValueProperty` changes or `SelectionItem_ElementSelected`. Arrowing through a collapsed `<select>`, or pressing Enter on a disclosure button, announces nothing, and the buffer node's `IsExpanded` goes stale.
- [ ] Announce ExpandCollapse state and Name/Value changes on the focused element.
- [ ] Subscribe to `UIA_SelectionItem_ElementSelectedEventId` (20012) within the document.
- [ ] Treat an expand/collapse change as a structure change for that node.

## P2 — Smaller issues

### 16. Announcements use raw UIA control type names
`AnnouncementBuilder` appends `ControlType.ToLowerInvariant()` (`AnnouncementBuilder.cs:59`) and `EventPipeline.BuildFocusAnnouncement` appends the raw name (`EventPipeline.cs:308`). Chromium headings are `Text`, so a heading is announced as "heading level 1, Welcome, text". Links are announced as "hyperlink".
- [ ] Map control types to spoken names: Hyperlink→link, Text/Group/Custom/Pane→omitted, ComboBox→combo box, CheckBox→check box, RadioButton→radio button, ListItem→list item.
- [ ] Have focus announcements use `AnnouncementBuilder` so they respect verbosity.

### 17. Typing echo waits behind long announcements
Typing echo is `High` priority (`EventPipeline.cs:290`), so it waits for the current utterance to finish. Typing right after a long focus announcement produces delayed echoes. Consider `Interrupt` for typing echo, where each character cuts off the previous speech (NVDA behaviour).

### 18. Mode toggling and "Any" commands outside web documents
- Insert+Space toggles `NavigationManager` and announces "Focus mode"/"Browse mode" in any app (`NavigationManager.cs:49`), although modes only matter in web documents. Moving focus from a browser edit field to another app also announces "Browse mode".
- [ ] Ignore ToggleMode, or say "not in a document", when no document is active. Only auto-switch modes for focus changes inside the active document.
- Insert+Down (Say All) and Insert+Up (read line) are swallowed in every app but do nothing outside web documents. Either implement them for the focused control (read its caret line or text via the UIA TextPattern), or give feedback.

### 19. Elements List opens with focus on the tabs
The first focusable control is the tab strip, so typing changes tab instead of filtering, contrary to the spec's "type to filter".
- [ ] Focus the list (or filter box) on `Shown`.
- [ ] Forward printable keys typed in the list to the filter box.

### 20. Inline content is split into one line per text run
`VBufferBuilder` ends every text node with `'\n'` (`VBufferBuilder.cs:234`). A paragraph such as "Read the <a>docs</a> first" becomes three lines, so line navigation and Say All read fragments. Join inline siblings (text, links, inline elements) of a block container into one line.

### 21. Duplicate text when a node's text descendants appear later
The rule "emit your own name only if no descendant has text" is evaluated when the subtree is built. After an incremental update gives a previously empty descendant some text, an ancestor that emitted its own name keeps it, so the text is duplicated. `IncrementalUpdater` should re-evaluate ancestors of the changed subtree, or re-capture from the nearest ancestor that emitted its own name.

### 22. A missing keymap file crashes startup
`KeyMap.LoadFromFile` throws if `default-keymap.json` is missing or invalid (`ServiceRegistration.cs:54`). DI then fails with an unhelpful error. Log the problem clearly, and fall back to built-in defaults or exit with a spoken or logged message.

### 23. Settings changes re-apply the voice even when unchanged
`ScreenReaderService.ApplySettings` calls `SetVoice` and `SetRate` on every settings change, including from the watcher thread while speech is playing. **(verify)** Whether SAPI tolerates changing the voice mid-utterance. Only apply values that actually changed.
