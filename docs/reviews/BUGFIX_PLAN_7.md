# Bug Fix Plan — Round 7

This is a seventh static review, of `master` at `b8285f6`, done on 2026-09-28 after the round-6 fixes were merged. It looks at the side effects of round 6: menus in Focus mode, the UIA timeouts, and the audio output that now stays open. It also covers two areas earlier rounds didn't examine: quick navigation through nested elements, and the modifier keys. The baseline is 684 tests passing on Linux, plus 14 that need Windows. Items marked **(verify)** depend on Windows, UIA, Chromium or SAPI behaviour and need confirming on Windows.

## Status (2026-09-28)

All 8 tasks are implemented, with regression tests for the testable parts. 701 tests pass on Linux, 17 of them new; the 14 that need Windows are unchanged. Still to check on Windows: the capture timeout on large pages, `powercfg /requests` with Vox idle, the double-tapped modifier, and Escape on real site menus.

Where the implementation made a specific choice:
- Task 1: quick navigation goes by document order. Next is the first item after the cursor's node. Previous is the last item before it that doesn't contain it, and the backward search skips containing items too.
- Task 2: a menu item not in the buffer counts as inside a popup, because popups are added after the capture.
- Task 3: `UIAProvider.WithDocumentCaptureTimeout` raises `TransactionTimeout` to 20 s for whole-document `BuildUpdatedCache` calls (load and full re-capture). Everything else keeps 4 s.
- Task 4: the output closes 5 s after the last cue (`IdleClose`) and as soon as cues are disabled. The next cue re-opens it.
- Task 5: two taps within 500 ms, with no other key in between (Shift, Ctrl and Alt are ignored), let the second press through. For Caps Lock, the tracked Caps Lock state is flipped to match.
- Task 6: auto-repeat key-downs of printable keys are echoed and buffered. Backspace, Delete and caret keys keep their key-down handling.
- Task 8: `SpeechQueue.Suspend`/`Resume` drop anything enqueued meanwhile without cancelling the engine.

Tasks use the `IMPLEMENTATION_PLAN.md` checkbox format and are ordered by severity. Each fix needs a regression test in `tests/Vox.Core.Tests/` where the code is testable.

## P1 — Wrong behaviour in common use

### 1. D / Shift+D skip or go backwards through nested landmarks
`QuickNavHandler` starts searching from the collection item that *encloses* the current node (`IndexAfterCurrent`, `QuickNavHandler.cs:215`; `IndexBeforeCurrent`, `QuickNavHandler.cs:241`). That is right for headings, which never nest, but wrong for collections whose items contain other items: landmarks (a navigation or form inside main, a search region inside the banner) and focusable containers.

Take a page whose main landmark begins with a navigation landmark, with the cursor in main's text after that navigation:
- D starts at the item after main in the list, which is the navigation. That landmark comes *before* the cursor, so the jump goes backwards.
- Shift+D starts at the item before main, so it skips that navigation, which is the real previous landmark.

- [x] Next: the first item whose `Id` is greater than the current node's.
- [x] Previous: the last item whose `Id` is smaller than the current node's and that is not one of its ancestors (the element the cursor is in). This keeps "Shift+H inside a heading finds the previous heading".
- [x] Tests: nested landmarks (main containing navigation, cursor after the navigation) for D and Shift+D; the existing heading tests still pass.

### 2. Escape can't leave Focus mode on a menu bar item or a closed menu (round-6 interaction)
Round 6 put menus and menu items into Focus mode. Round 4 sends Escape to the page whenever the focused element is a `Menu`/`MenuItem` or has a menu role (`UpdateEscapeTarget`, `BrowseModeController.cs:643`), on the assumption that a popup menu is open.

Many sites use `role=menubar`/`menuitem` for ordinary navigation links. Tabbing onto one now enters Focus mode, and Escape never leaves it: it always goes to the page, so the user is stuck until they press Insert+Space. The same happens on a menu button's item after its popup has closed.
- [x] Escape goes to the page only while a popup is really open:
  - an expanded combo box or menu item (`_focusedExpanded`);
  - an item inside a popup menu, that is, whose nearest menu-like ancestor in the buffer is a `Menu` or `role=menu` rather than a `MenuBar` or `role=menubar`;
  - a menu item not in the buffer at all. Popups are usually added after the capture.
- [x] Tests: a collapsed menubar item → Escape leaves Focus mode; an item in a `role=menu` popup → Escape goes to the page; an expanded menubar item → Escape goes to the page.

### 3. Large pages may never load with the 4-second UIA transaction timeout (verify)
Round 6 set `TransactionTimeout` to 4 s for the whole automation object (`UIAProvider.cs:73`), so that a hung page can't block the UIA thread. But capturing a whole document is one `BuildUpdatedCache` call over the entire subtree (`BrowseDocumentTracker.cs:182`, `BrowseDocumentTracker.cs:320`). On a large page (a long article, a big GitHub diff, a spreadsheet-like grid) that can legitimately take several seconds. If it times out, the capture fails, the single retry fails the same way, and the page never gets a buffer.
- [x] Raise the timeout around whole-document captures only: set `TransactionTimeout` to about 20 s before `BuildUpdatedCache` on the document root and restore it afterwards. Both happen on the UIA thread, so nothing else runs in between. Keep the short timeout for everything else.
- [x] (verify) How long a capture takes on a 5,000- to 10,000-element page in Chrome and Edge.

### 4. The audio output that stays open may keep the PC awake (verify)
Round 6 keeps one `WaveOutEvent` playing silence all the time (`ReadFully = true`, `AudioCuePlayer.cs:75`), so cues start quickly. Windows treats an active audio stream as a reason not to sleep or turn off the display ("An audio stream is currently in use" in `powercfg /requests`). With Vox running, the PC may never sleep on its own. The stream stays open even when audio cues are turned off in settings.
- [x] Close the output after a few seconds without cues (e.g. 5 s) and reopen it on the next cue. Close it at once when cues are disabled.
- [x] (verify) `powercfg /requests` with Vox idle, before and after the change.

## P2 — Smaller issues

### 5. With Caps Lock or Insert as the Vox key, the key's own function is lost
Every press of the screen reader modifier is swallowed (`KeyboardHook.cs`, the `isScreenReaderModifier` branch). With Caps Lock as the modifier, Caps Lock can never be turned on (round 4 only turns it off). With Insert, overwrite mode can't be toggled. NVDA passes the key through when it is pressed twice quickly. Pressing the modifier twice within about 500 ms, with no other key in between, should send the key to the system once, using the injected-key marker round 4 added.

### 6. Holding a key down echoes it only once
Typing echo speaks on key-up (`TypingEchoHandler.cs`), and auto-repeat produces key-downs only. Holding "a" types "aaaa" but is echoed once, and holding Space to type spaces says "space" once. Echo repeated key-downs of a printable key (a key-down for a key that is already down) as extra characters, using that key's modifiers.

### 7. T on an empty table reads the next line on the page
`AnnounceTable` reads the line at the table's start offset (`BrowseModeController`, `AnnounceTable`). A table with no text of its own (for example cells not captured yet) has an empty range positioned at the following content, so "table" is followed by unrelated text. Read the line only when the table's subtree has text.

### 8. Page speech can talk over setup when it is run again
Running setup again (Insert+Ctrl+S) stops key dispatch, but the pipeline keeps speaking focus changes, live regions and notifications through `SpeechQueue`. An `Interrupt` from the queue cancels the engine, and with it the wizard prompt. Hold pipeline speech while the wizard runs: add a `SpeechQueue` suspend/resume that drops queued utterances, and resume in the `finally` block.
