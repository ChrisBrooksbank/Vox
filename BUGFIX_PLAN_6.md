# Bug Fix Plan — Round 6

This is a sixth static review, of `master` at `b043266`, done on 2026-09-28 after the round-5 fixes were merged. It covers two things:
- how the round-5 changes interact with the rest of the code;
- the parts earlier rounds only skimmed: the first-run wizard, the Elements List, audio cues, the UIA thread and application lifetime.

The baseline is 643 tests passing on Linux, plus 14 that need Windows. Items marked **(verify)** depend on Windows, UIA, Chromium or SAPI behaviour and need confirming on Windows.

Tasks use the `IMPLEMENTATION_PLAN.md` checkbox format and are ordered by severity. Each fix needs a regression test in `tests/Vox.Core.Tests/` where the code is testable.

## P1 — Wrong behaviour in common use

### 1. Page menus no longer work with the arrow keys (round-5 regression)
Round 5 made automatic mode switching symmetric: Focus mode is now left when focus moves to a control that doesn't need it (`NavigationManager.HandleFocusChanged` uses `NeedsFocusMode`). But `FormControls.NeedsFocusMode` doesn't include menus. `FocusModeControlTypes` (`FormControls.cs:25`) and `FocusModeRoles` (`FormControls.cs:30`) have no `Menu`/`MenuItem`/`MenuBar` or `menu`, `menubar`, `menuitem`, `menuitemcheckbox`, `menuitemradio`. The old test was `IsFormField`, which did include them.

The effects on a page's own menus (an "Actions" button opening a `role=menu`, a site menubar):
- Opening one from Focus mode now switches back to Browse mode as focus lands on the first item. Up and Down then move the virtual cursor instead of the menu.
- Opening one from Browse mode never enters Focus mode.

The Escape handling added in round 4 (Escape goes to the page while a menu has focus) assumes the menu is used in Focus mode.
- [ ] Add the menu control types and roles to the Focus-mode sets. `IsFormField` already lists them.
- [ ] Tests: Focus mode stays on when focus moves from an edit to a `menuitem`; focus moving onto a `menuitem` from Browse mode enters Focus mode.
- [ ] (verify) Also consider `tab`/`tablist`: ARIA tabs are operated with the arrow keys, and NVDA treats tab controls as needing focus mode.

### 2. The wizard's voice step changes the voice when the user accepts the current one
`RunVoiceSelectionStepAsync` starts at `voices[0]` unless `VoiceName` is set (`FirstRunWizard.cs:213`). On first run `VoiceName` is null, and the voice actually in use is the one `SapiSpeechEngine.SelectOneCoreVoice` picked at startup, often not `voices[0]`. So the step announces the wrong "Current voice", and pressing Enter to keep it saves `voices[0]` and switches to a voice the user never heard.
- [ ] Add `string? CurrentVoice { get; }` to `ISpeechEngine` (SAPI: `_synthesizer.Voice.Name`), and start the step at that voice. Only save `VoiceName` when the user actually changed voice.
- [ ] Test, with a fake engine whose current voice is the second of three: the prompt names it, and Enter keeps it (no `SetVoice`, `VoiceName` unchanged).

### 3. Focusing a large text box reads its entire contents
Round 5 speaks a field's value on focus (`FormControls.SpokenValue`, `FormControls.cs:56`) with no limit. Tabbing into a `<textarea>` holding a long draft, a comment box, or a code editor exposed as an Edit reads all of it, and the only way to stop it is Ctrl. The value is also put into the buffer in full, so a big textarea adds thousands of characters and many lines of editable text to browse mode. NVDA reads only the current line of a multi-line field.
- [ ] Speak at most the first line of a value, cut at `MaxLineLength` at a word boundary, with "…" when truncated. In the buffer, cap the value (e.g. 1,000 characters) and keep it on the field's own line(s).
- [ ] Tests: a multi-line value speaks only its first line; a long single line is cut at a word boundary; the buffer text is capped.

### 4. Leaving the wizard early keeps the previewed rate and voice but not the settings
The rate and voice steps change the engine immediately while previewing (`FirstRunWizard.cs:191`, `FirstRunWizard.cs:237`), but save the setting only when Enter is pressed. If the user presses Escape or stops answering in the middle of either step, `WizardExitException` saves the settings "so far" (`FirstRunWizard.cs:110`), which don't include the preview. The engine keeps the previewed rate or voice. `ApplySettings` doesn't correct it, because it only re-applies values that changed since last time. What the user hears then differs from `settings.json` until Vox restarts, and the next settings change doesn't fix it.
- [ ] On early exit (and when skipping at the welcome step), re-apply the saved `SpeechRateWpm` and `VoiceName` to the engine before speaking "Setup ended".
- [ ] Test: change the rate with Up, then Escape → the engine's last `SetRate` call is the saved rate.

## P2 — Smaller issues

### 5. Polite live regions that re-render the same text are repeated (round-5 regression risk)
Round 5 speaks identical live-region text again after 1.5 s without events (`LiveRegionMonitor.cs:84`). Frameworks that re-render a status region with unchanged text every few seconds now have it spoken every time: "Connected", "All changes saved", a clock that only changes its minute. Pages that deliberately repeat a message almost always clear the region first, and that is already handled. Keep the gap rule for assertive regions only, and require a clear for polite ones.

### 6. Browse keys can stay off if the tracker reports a different focused element (verify)
`HandleFocusInDocument` only re-enables browse keys when the tracker's focused runtime id equals the controller's last focus event (`BrowseModeController.cs:479`). The tracker reads focus again with `GetFocusedElementBuildCache` later. If that returns a different element than the event's sender, keys stay off until the next focus change. That can happen when an event is raised for a container while a descendant holds focus, or with elements whose runtime id isn't stable. The guard exists only to ignore reports about an *earlier* focus. Number focus changes instead: pass a sequence number to `OnFocusChangedAsync` (`ScreenReaderService.cs:220`), echo it in `FocusInDocumentEvent`, and accept the report when it is the latest.

### 7. A hung page stalls all UIA work, and the backlog runs afterwards
Every UIA call runs in turn on one thread (`UIAThread`), with UIA's default timeouts. A busy or hung renderer blocks the thread in `BuildUpdatedCache` or `FindFirstBuildCache` for as long as UIA waits, which is up to its transaction timeout (20 s by default). Meanwhile each focus change queues another `DetectDocument`, and they all run once the thread is free.
- [ ] Set `IUIAutomation2.ConnectionTimeout`/`TransactionTimeout` to a few seconds.
- [ ] Coalesce `DetectDocument` in `BrowseDocumentTracker`: don't queue one while another is waiting to run.

### 8. Each audio cue opens a new output device on a sleeping pool thread
`AudioCuePlayer.Play` starts `Task.Run`, creates a `WaveOutEvent`, and polls `Thread.Sleep(10)` until the sound ends (`AudioCuePlayer.cs:47`). Every cue pays the device-open latency, and cues repeat quickly: boundary on a held arrow key, and mode cues. Each one also holds a thread-pool thread, and overlapping cues open several devices at once. Keep one `WaveOutEvent` open for the app's lifetime, feeding a `MixingSampleProvider`, and add a `CachedSoundSampleProvider` input per cue.

### 9. There is no keyboard command to quit Vox
The keymap has no exit command. Closing Vox means finding its console window or ending the process, which is hard to do without sight. NVDA uses Insert+Q. Add a `Quit` command bound to Insert+Q (mode Any). It should say "Vox exiting" and call `IHostApplicationLifetime.StopApplication()`. A second press within a few seconds could confirm it, so the key can't be pressed by accident.

### 10. "You can re-run it from settings" is not true
Skipping the wizard says "Setup skipped. You can re-run it from settings." (`FirstRunWizard.cs:73`), but there is no settings UI or command that re-runs it. The only way is to set `FirstRunCompleted` to `false` in `%APPDATA%\Vox\settings.json` and restart. Add a command (e.g. Insert+Ctrl+S, mode Any) that runs the wizard again, and change the message to name it.

### 11. The Elements List starts at the top and shows unnamed items by control type
- The list always selects the first item (`ElementsListDialog.cs:331`). NVDA selects the element at or after the virtual cursor, so the user starts where they are on the page. Pass the current node to the dialog, and select the first item at or after it in document order.
- Items without a `Name` are shown as "[Hyperlink]" or "[Edit]" (`ElementsListViewModel.cs:105`). An image link's text often comes from a child's alt text, so these show up often. Fall back to the node's text from the buffer (its subtree's range in `FlatText`), and for form fields add the control type and value ("Search, edit, hello").

### 12. Advanced verbosity drops every role, contrary to its description
The Advanced profile is documented as "only role when ambiguous" (`VerbosityProfile.cs:73`), but it turns `announceControlType` off entirely. An edit field, a button, a check box and plain text are then all announced just by name, and the user can't tell they can type into "Search". Either keep the control type for form controls and links at Advanced (a separate profile flag), or document the level as announcing no roles at all.
