# Bug Fix Plan

Found by a static review of `src/` on 2026-09-28. The code was not built or run: the review environment was Linux with no .NET SDK, and all projects target `net9.0-windows`. Items marked **(verify)** rely on Win32/UIA/SAPI runtime behaviour and should be confirmed on Windows before or while fixing.

Tasks use the `IMPLEMENTATION_PLAN.md` checkbox format, so they can be moved there for the build loop. They are ordered by severity and dependency. Each fix needs a regression test in `tests/Vox.Core.Tests/` where the code is testable.

## P0 — Core behaviour broken

### 1. Speech cannot be interrupted mid-utterance
`SpeechQueue.ProcessQueueAsync` awaits `_engine.SpeakAsync` (`src/Vox.Core/Speech/SpeechQueue.cs:115`) and reads the channel only between utterances (`TryPeek` at line 106). An `Interrupt` utterance posted while a long utterance is playing waits until that utterance ends. This breaks the "user navigation always interrupts" rule. The existing test uses an engine that returns instantly, so it cannot catch this.
- [ ] Watch the channel while speaking. For example, run `SpeakAsync` with a linked per-utterance CTS and have `Enqueue`/`EnqueueAsync` cancel it (and call `_engine.Cancel()`) when an `Interrupt` arrives. Add a test with a fake engine whose `SpeakAsync` blocks until cancelled.
- [ ] Add a flush API, e.g. `SpeechQueue.Clear()` / `CancelAll()`, that drains pending utterances and cancels current speech. Use it for StopSpeech (task 4) and Say All cancel (task 6).

### 2. Keyboard hook never swallows keys
`KeyboardHook.HookCallback` always returns `CallNextHookEx` (`src/Vox.Core/Input/KeyboardHook.cs:243`). In Browse mode, pressing H posts `NextHeading` but the page also receives `h`. Insert passes through and toggles overwrite mode. Insert+Space also sends Space to the page. Arrow keys scroll the page while also moving the virtual cursor.
- [ ] Decide inside the callback whether to consume the key, then return `1` to swallow it. The check must be O(1) and allocation-free to stay under 1ms. One design: a `volatile` snapshot of "keys consumed in the current mode" (a frozen set built from `KeyMap` and published whenever the mode changes), plus always swallowing the modifier key (Insert, or CapsLock per settings) while it is used as a modifier.
- [ ] Swallow the key-up that matches every swallowed key-down.

### 3. KeyInputDispatcher mode is never updated
`KeyInputDispatcher.SetMode` has no callers, so key resolution always uses Browse bindings. In Focus mode, typing `h`, `k`, `d`, `1`–`6` or arrows produces NavigationCommands, which `NavigationManager` then drops (`NavigationManager.cs:65`). Those keys are not posted as `RawKeyEvent`, so typing echo skips them.
- [ ] Have `NavigationManager` expose a `ModeChanged` event, or handle `ModeChangedEvent` in `ScreenReaderService`, and call `_keyInputDispatcher.SetMode(...)` and update the hook's swallow set (task 2). Add a test: after switching to Focus, `H` produces a `RawKeyEvent`.

### 4. StopSpeech is unbound, and modifier keys arrive as left/right VK codes (verify)
- The keymap binds StopSpeech to `Ctrl` + `vkCode 0` (`assets/config/default-keymap.json:31`). vkCode 0 never occurs, so speech cannot be stopped.
- The code tests modifier keys against generic `VK_SHIFT`/`VK_CONTROL`/`VK_MENU` codes. `WH_KEYBOARD_LL` reports the left/right-specific codes instead (`VK_LSHIFT` 0xA0, `VK_RCONTROL` 0xA3, etc.).
- `GetKeyState` in an LL hook (`KeyboardHook.cs:226`) reads the hook thread's key-state table. That table is not synchronised with global input, so modifier flags can be wrong. **(verify)**
- [ ] Track modifier state inside the hook from the key-down/key-up events themselves, handling L/R codes 0xA0–0xA5, Insert and CapsLock, or use `GetAsyncKeyState`. Keep the callback allocation-free.
- [ ] Bind StopSpeech to a real key: a bare Ctrl press (vk 0xA2/0xA3 key-down with no other key, NVDA convention) and/or Insert+Escape, which the wizard tutorial already announces (`FirstRunWizard.cs:315`). Route it to `SpeechQueue.CancelAll()` (task 1).

### 5. The virtual buffer is never built from live pages
Nothing calls `QuickNavHandler.SetDocument`, and no UIA-backed `IVBufferElement` exists. `VBufferBuilder` mentions `UIAElementAdapter`, but that class does not exist. `IncrementalUpdater` and `LiveRegionMonitor` are also never used. As a result, H/K/D/F and Say All do nothing in a real browser.
- [ ] Add `UIAElementAdapter : IVBufferElement` in `Vox.Core/Accessibility/`. It should read cached properties through a cache request with `TreeScope_Subtree` (or a cached TreeWalker) on `UIAThread`.
- [ ] On a focus change into a `Document` element (Chrome/Edge), build the buffer on the UIA thread and call `QuickNavHandler.SetDocument`. Set `QuickNavHandler.CurrentNode` from the focused element's RuntimeId.
- [ ] Handle `StructureChangedEvent` by calling `IncrementalUpdater.ApplyUpdate` (after fixing task 8), throttled or debounced.

## P1 — Wrong behaviour in wired features

### 6. Say All enqueues the whole document at once and cannot be stopped
`SayAllController.SpeakLineAsync` enqueues a line and only calls `Task.Yield()` (`SayAllController.cs:110-114`). The loop pushes every line straight into the unbounded queue, where `SpeechQueue` coalesces Normal utterances into one giant utterance. `Cancel()` stops enqueuing but leaves queued speech in place. The cursor is also created fresh at offset 0 (`ScreenReaderService.cs:163`), so reading always starts from the top instead of the current position.
- [ ] Speak each line and wait for it to finish, either through a completion task on the utterance or by calling the engine directly. Advance the cursor only after the line is spoken.
- [ ] Flush the speech queue on cancel. Cancel Say All on any key-down, as the spec requires, not only on StopSpeech.
- [ ] Keep one long-lived `VBufferCursor` per document, positioned at `QuickNavHandler.CurrentNode`, and share it between Say All and line/word/char navigation.

### 7. Duplicate cues and wrong mode switching in NavigationManager
- The mode-change cue plays twice: once in `NavigationManager.SwitchTo` (`NavigationManager.cs:106`) and again in `EventPipeline.HandleModeChangedAsync` (`EventPipeline.cs:231`). Remove one.
- `IsEditField` returns true for any focusable node (`NavigationManager.cs:118`). Enter on a link or button therefore switches to Focus mode. Limit it to editable control types (Edit, ComboBox, and possibly Document with editable content).

### 8. IncrementalUpdater corrupts the tree
- `BuildNode` does `parent?.Children.Add(node)` for the new subtree root (`IncrementalUpdater.cs:228`). `RepairParentChildPointers` then also puts the root at the old index, so the parent lists the new root twice.
- New nodes get IDs from 0 (`IncrementalUpdater.cs:157`). IDs are no longer unique or in document order, which breaks `QuickNavHandler`'s Id-based fallback search.
- The downstream shift mutates `TextRange` on nodes shared with the previous "immutable" document (`IncrementalUpdater.cs:110`). Anything holding the old snapshot, such as a cursor or the Elements List, sees corrupted offsets.
- [ ] Build the replacement root without attaching it to the parent, and renumber `Id` over the merged list. Either clone the shifted nodes or document that the old snapshot becomes invalid and swap all holders atomically. Deduplicate the node-building logic with `VBufferBuilder` (currently copy-pasted).

### 9. Headings without `level` are dropped (verify)
`role=heading` with no `level` in AriaProperties parses to 0 (`VBufferBuilder.cs:225`, `UIAEventSubscriber.cs:364`). The node then fails the heading check and is left out of `Headings`. ARIA's default level is 2. Chromium exposes heading level via `UIA_HeadingLevelPropertyId` (30173), which is not in the cache request.
- [ ] Default to level 2 when the role is heading and no level is given. Add `HeadingLevel` (30173) to the cache request and prefer it.

### 10. Text is duplicated in FlatText (verify)
Every named non-container node appends its Name (`VBufferBuilder.cs:191`). In Chromium's UIA tree, a link or heading named "Foo" usually has a child Text node also named "Foo", so the text is spoken twice by line navigation and Say All.
- [ ] Emit text only from leaves, or skip a child whose Name equals its parent's. Test against a Chromium-shaped mock tree.

### 11. Settings are ignored after startup
- The verbosity level is hardcoded to `Beginner` (`ScreenReaderService.cs:183`). Use `_settings.CurrentValue.VerbosityLevel`.
- Speech rate and voice are applied once in `StartAsync`, with no `OnChange` handler, so changes made in the wizard or `settings.json` don't take effect. Subscribe to `IOptionsMonitor.OnChange`.
- `AudioCuesEnabled`, `AnnounceVisitedLinks` and `ModifierKey` are never read. CapsLock as modifier is offered in the wizard but has no effect. Wire them up: `AudioCuePlayer.IsEnabled`, `AnnouncementBuilder`, and the hook's modifier handling (task 4).
- The built-in `VoxSettings.SpeechRateWpm` default is 450, the maximum (`VoxSettings.cs:27`), while `default-settings.json` uses 200. Align them.

### 12. Quick-nav speech is not an interrupt
Quick-nav results are enqueued at `High` (`ScreenReaderService.cs:185`). Pressing H repeatedly queues every heading instead of replacing the previous announcement. Use `Interrupt` for user navigation.

### 13. Browse-mode commands with no handler
`NextLine`/`PrevLine`/`Word`/`Char`, `ReadCurrentLine`/`ReadCurrentWord`, `ActivateElement` and `ElementsList` fall through `QuickNavHandler.Handle` and return null silently. T/Shift+T, F/Shift+F and Tab have handlers but no keymap entries. Shift+1–6 map to the same "next" command as 1–6.
- [ ] Route caret commands to the shared `VBufferCursor` (task 6).
- [ ] Implement ActivateElement via the UIA Invoke pattern on the current node's RuntimeId.
- [ ] Show `ElementsListDialog` on a dedicated STA WinForms thread.
- [ ] Add the missing keymap entries and `PrevHeadingLevelN` commands.

## P2 — Smaller correctness issues

### 14. Live regions
- [ ] `LiveSetting` Off (0) is treated as Polite (`UIAEventSubscriber.cs:239`). Map it to Off and drop the event.
- [ ] Pass live-region events through `LiveRegionMonitor.ShouldAnnounce` before speaking. It is registered in DI but unused, so diffing and throttling don't happen.
- [ ] (verify) Chromium live-region containers often have an empty Name. Fall back to the text of the element's subtree.

### 15. Typing echo
- [ ] Ctrl/Alt shortcuts are echoed and added to the word buffer, e.g. Ctrl+S says "s" (`TypingEchoHandler.cs:105`). Skip keys pressed with Ctrl or Alt.
- [ ] Caps Lock state is ignored, so letters are always lowercase unless Shift is held.
- [ ] In Browse mode, unmapped letters are echoed as if typed. Suppress echo in Browse mode.

### 16. VBufferCursor.PrevLine from mid-line
When the cursor is in the middle of a line (after char/word moves), `PrevLine` goes to the start of the current line instead of the previous line (`VBufferCursor.cs:199`). Find the current line's start first, then step back one line. With wrap enabled, `HandleBoundaryString` returns a line even for word moves.

### 17. SAPI rate mapping (verify)
`SetRate` maps 150–450 WPM linearly onto SAPI −10..+10 (`SapiSpeechEngine.cs:105`), so the default 200 WPM becomes rate −7. SAPI's rate scale is roughly logarithmic, with −10 ≈ ⅓× and +10 ≈ 3×, so this is far slower than 200 WPM. Measure on Windows and use a logarithmic mapping around the voice's rate-0 WPM (about 180).

### 18. First-run wizard
- [ ] Key presses during a prompt are lost, because the waiter is installed only after `SpeakAsync` completes (`FirstRunWizard.cs:339`). Install the waiter before speaking and cancel speech on key-down.
- [ ] Wizard keys (Enter, Escape, arrows) also reach the focused app (task 2).
- [ ] `cancellationToken.Register` registrations are never disposed.
- [ ] Prompts announce shortcuts that don't exist ("Insert F1 for help", "Insert Escape to stop speech").

### 19. Lifecycle
- [ ] `KeyboardHook.Uninstall` completes the readonly channel (`KeyboardHook.cs:197`), so a later `Install()` receives no events. `Install` also fails silently when `SetWindowsHookEx` returns 0; surface the error.
- [ ] `SettingsMonitor` watches `SettingsManager.DefaultUserSettingsPath` (`SettingsManager.cs:164`) instead of the manager's configured path, and never starts if the directory is missing at startup.
- [ ] (verify) Subscribing to StructureChanged and Name PropertyChanged over the whole desktop subtree is very noisy. Scope these to the active browser document once task 5 lands.
