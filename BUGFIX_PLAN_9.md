# Bug Fix Plan — Round 9

This is a ninth static review, of `master` at `e11f928`, done on 2026-10-11. It covers code added since round 8:
- the update checker and its "Check for updates" command;
- start at logon and portable copies;
- the input gestures and settings dialogs, and the Vox dialogs opened with `BeginOwnDialog`;
- math exploring;
- the cookie-banner detector.

A read-only audit produced 10 candidate findings. Each was traced through the code before being fixed. All 10 hold up. Three of the fixes go further than, or differ from, what the audit suggested (tasks 3, 5 and 8); the details are under each task.

The baseline is 1,796 tests passing on Linux, 2 skipped, plus 5 that need Windows (STA threads and WinForms). Items marked **(verify)** depend on Windows behaviour (Windows Installer, the keyboard hook, UIA focus events) and need confirming on Windows.

## Status (2026-10-11)

All 10 tasks are implemented. 1,815 tests pass on Linux, 19 of them new; the 2 skipped and the 5 that need Windows are unchanged. The build has no compiler warnings left in `src/`: the CS0108 warning on `CommandSearchDialog.Update()` is fixed by renaming the method to `ShowResults()`.

These need checking on Windows:
- that msiexec can open the installer while Vox holds it with read sharing, and does so before Vox exits (task 6);
- that capturing Numpad 8 with Num Lock off in the input gestures dialog binds "Numpad 8" (task 4);
- that focus coming back after the settings dialog closes is announced, and is not announced after a command chosen from the Vox menu (task 8).

Where the implementation made a specific choice:
- Task 2: a portable copy never touches the Run key. The General-tab toggle is still shown there; hiding it would change `SettingsPages.cs`, which is being edited in parallel.
- Task 3: the result comes back as a new generic `PipelineWorkEvent` (an `Action` run on the pipeline thread). A dedicated event would also have needed new wiring in `ScreenReaderService`.
- Task 6: `UpdateChecker` is now `IDisposable`. The checked installer stays open until the host disposes the checker at shutdown.
- Task 8: a command chosen from the Vox menu still keeps focus quiet when it comes back (`VoxDialogClosedEvent.QuietFocusReturn`), so the command's own speech isn't cut off.
- Task 10: a banner that mentions cookies only inside a link is no longer found. Real banners say it in their own text or on a button.

Tasks use the `IMPLEMENTATION_PLAN.md` checkbox format and are ordered by severity. Each fix has a regression test in `tests/Vox.Core.Tests/` where the code is testable.

## P1 — Wrong behaviour in common use

### 1. Vox may not close after starting an update installer
After `checker.InstallAsync` has started msiexec, `InstallAsync` (`UpdateCommands.cs:71`) awaits `speech.EnqueueAndWaitAsync` for the goodbye. That task is cancelled when:
- focus moves to the UAC prompt or the installer window, and the focus announcement interrupts it;
- speech is paused;
- sleep mode drops it.

The cancellation lands in the generic `catch`, which says "The update couldn't be downloaded", and `exit()` is never called. The MSI then runs while Vox's files are in use.
- [x] Only the download step reports "couldn't be downloaded". Once the installer has started, the goodbye waits at most `GoodbyeTimeout` (3 s), cancellation and the timeout are swallowed (as `ScreenReaderService.QuitAsync` does), and `exit()` is always called.
- [x] Test (`UpdateCommandsTests`): with speech muted, accepting the update starts the installer and Vox still exits.

### 2. A portable copy rewrites or deletes the installed Vox's start-at-logon entry
`ScreenReaderService.ApplySettings` always runs `StartAtLogon.Apply` at startup. The Run entry in HKCU is shared by every copy:
- a portable copy (`VoxPaths.IsPortable`) with `StartAtLogon` false deletes the installed Vox's entry;
- a portable copy with `StartAtLogon` true points the entry at the USB stick.
- [x] `StartAtLogon.Apply(..., isPortable)` leaves the registration alone in a portable copy. `ScreenReaderService` passes `VoxPaths.IsPortable`.
- [x] Test: with `isPortable: true`, the installed entry is unchanged both when the setting is on and when it is off.

## P2 — Wrong behaviour in less common situations

### 3. The update question touches pipeline-only state from the thread pool
`CheckNowAsync` resumes on a thread-pool thread after `await checker.CheckAsync().ConfigureAwait(false)` and then calls `browse.BeginOwnDialog()` (`UpdateCommands.cs:47`). That method changes `_modalOpen`, `_ignoreFocusReturnTo` and the document-active state, all of which belong to the pipeline thread. When `BeginOwnDialog` returned false (another Vox dialog open), nothing was said, so the check seemed to have done nothing.
- [x] The check posts a `PipelineWorkEvent`, and `EventPipeline` runs it on the pipeline thread. That is where `BeginOwnDialog` is called and the question is asked.
- [x] When a Vox dialog is already open: "Vox N is available. Close the open Vox dialog and check again to install it."
- [x] Tests: the question isn't asked until the posted work runs; with a Vox dialog open, the message above is said.

### 4. A keypad key captured with Num Lock off rebinds the dedicated arrow key (verify)
The keyboard hook hands `CaptureNextKey` the raw virtual key. With Num Lock off, Numpad 8 arrives as `VK_UP` with `IsKeypad` set. `InputGesturesDialog.KeyPressed` made `new Gesture(modifiers, vk, mode, keypad)`, but `GestureEditor` keys bindings by `(modifiers, VkCode)` only. Binding Numpad 8 therefore rebound the Up arrow.
- [x] `Gesture.FromKey` converts with `NumpadKeys.BindingCodes`, as the keymap resolves keys: Numpad0–9, and Numpad Enter as 269. The dialog uses it.
- [x] A Num Lock digit (code -1) isn't bound. The dialog says "Number pad digits type numbers and can't be used. Turn Num Lock off to use the number pad keys."
- [x] Tests (`GestureEditorTests`):
  - Numpad 8 is saved as `Numpad8` and doesn't conflict with Up's binding;
  - Up keeps its binding;
  - Numpad Enter becomes 269;
  - Num Lock digits give no gesture.

### 5. Math exploring outlives the page
The explorer stopped only on a non-math command or a new document. After Alt+Tab to Notepad, Insert+Up (`ReadCurrentLine`, bound "Any") still went to the explorer (`BrowseModeController.HandleCommand`) and read the expression instead of Notepad's line. The same happened while a Vox dialog was open.
- [x] A real focus move (a different element, inside or outside the page) stops exploring.
- [x] `HandleCommand` also stops it when the document isn't active (another app, a Vox dialog) or the mode isn't Browse. This goes beyond the audit's suggestion.
- [x] Tests: after focus moves to Notepad, or a Vox dialog opens, `ReadCurrentLine` sends no MathCAT command.

### 6. The checked installer can be swapped before msiexec reads it (security; verify)
`UpdateChecker.InstallAsync` downloaded to a predictable `%TEMP%\Vox-<version>.msi`, closed the file, hashed it from a second handle, and then started msiexec on the path. Another process could pre-create that path or replace the file between the hash and the install.
- [x] The download goes into a new randomly named folder (`Directory.CreateTempSubdirectory("Vox-update-")`) and is created with `FileMode.CreateNew`.
- [x] The file is reopened read-only with `FileShare.Read`. Writes and deletes are denied, but msiexec can still read it. The hash is taken through that handle, which stays open after the installer starts until the checker is disposed at shutdown.
- [x] A failed check, or any download error, deletes the folder.
- [x] Tests (`UpdateFeedTests`):
  - each install gets a fresh folder, not the temp folder itself;
  - the installer can read the held file;
  - a tampered download leaves no folder behind.
- [x] (verify) msiexec opens the package with read sharing only, and before Vox exits.

### 7. A feed entry without a channel crashes the update check
`UpdateFeed.Parse` didn't check `channel`. With the Stable channel, `Newer` calls `r.Channel.Equals(...)` (`UpdateFeed.cs:77`), and the resulting `NullReferenceException` isn't one of the exceptions `CheckAsync` catches.
- [x] `Parse` rejects an entry without a channel with a `FormatException`, which is reported as a failed check.
- [x] Test: a feed entry without "channel" is malformed.

### 8. Focus coming back after a Vox dialog closes isn't announced (verify)
`BeginOwnDialog` set `_ignoreFocusReturnTo = _lastFocusedRuntimeId` (`BrowseModeController.cs:1108`). This was copied from the Elements List, where it stops the returning focus from undoing the list's jump. The settings, input gestures, command search, portable copy, update and Vox menu dialogs don't move the cursor, so after closing them the user heard nothing.

One case depends on the quiet return: a command chosen in the Vox menu runs right after the menu closes, and the focus announcement would interrupt its speech ("Checking for updates").
- [x] `BeginOwnDialog` no longer sets the ignore, and clears one left by an earlier dialog.
- [x] `VoxDialogClosedEvent` has a `QuietFocusReturn` flag, passed to `EndOwnDialog`. `SettingsCommands` sets it only when the Vox menu closes with a command chosen.
- [x] Tests: focus returning after `BeginOwnDialog`/`EndOwnDialog` is announced; after `EndOwnDialog(quietFocusReturn: true)` it isn't. The existing Elements List test is unchanged.

## P3 — Smaller issues

### 9. The settings dialog undoes settings changed elsewhere while it is open
`SettingsDialog.Change` applied each field's setter to a snapshot taken when the dialog opened, and Cancel re-applied that whole snapshot (`SettingsDialog.cs:87-91, 157-164`). A setting changed while the dialog was open was lost by the next change in the dialog, or by Cancel. Such changes come from the settings ring, Insert+M, sleep mode or an edited settings file.
- [x] New `SettingsEditSession`: each change goes to `IOptionsMonitor.CurrentValue` as it is then. Cancel puts back only the fields the dialog changed, to their opening values. `ISettingsDialogPresenter.ShowAsync` now takes a `Func<VoxSettings>`.
- [x] Tests (`SettingsEditSessionTests`):
  - a change keeps a setting changed meanwhile;
  - Revert restores only the dialog's fields;
  - Revert with no changes applies nothing.

### 10. A footer with a "Cookie policy" link is reported as a cookie banner
The banner search starts from every button or link and takes the smallest container whose text mentions cookies (`OverlayDetector.cs:51-63`). A footer with a "Cookie policy" link qualified, and was announced as "Cookie banner" after every load.
- [x] The consent word must appear in the container's text outside links: plain text, a button or a heading.
- [x] The container must also offer an answer: an accept, reject or close button or link, or a close symbol.
- [x] Tests:
  - footers with a "Cookie policy" link and a Subscribe or Close button are not banners;
  - text about cookies without an answer button is not a banner;
  - the existing banner tests still pass.
