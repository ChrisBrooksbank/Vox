# Bug Fix Plan — Round 8

This is an eighth static review, of `master` at `48fc20c`, done on 2026-09-28 after the round-7 fixes were merged. It covers three things:
- side effects of round 7: the UIA timeouts, the double-tapped modifier and the idle audio output;
- how the virtual cursor follows focus when focus lands on the page itself;
- the tracker's memory use on long-lived pages.

The baseline is 701 tests passing on Linux, plus 14 that need Windows. Items marked **(verify)** depend on Windows, UIA, Chromium or SAPI behaviour and need confirming on Windows.

Tasks use the `IMPLEMENTATION_PLAN.md` checkbox format and are ordered by severity. Each fix needs a regression test in `tests/Vox.Core.Tests/` where the code is testable.

## P1 — Wrong behaviour in common use

### 1. Focus landing on the page itself sends the virtual cursor to the top
`HandleFocusChanged` moves the cursor to whatever element received focus (`MoveTo(node)` at the end of `BrowseModeController.HandleFocusChanged`). When focus goes to the document itself rather than an element in it, that node is the document root. Its text range starts at offset 0, so the cursor jumps to the top of the page and the user loses their place. Focus goes to the document when:
- a modal dialog closes and the page doesn't move focus anywhere;
- the user clicks the page background;
- a script calls `blur()`;
- Escape closes a menu and focus falls back to the body.

`HandleDocumentChanged` already treats "focus is the page" as "keep or restore the position". The focus path doesn't.
- [ ] When the focused node is the document root, leave the cursor and `CurrentNode` where they are. Still apply the mode rules: the page doesn't need Focus mode, so an automatic switch back to Browse mode applies.
- [ ] Test: cursor on a heading, focus event for the document root → cursor unchanged, Browse mode.

### 2. Searches for one element can time out on large pages, losing activations and updates (verify)
Two paths look up a single element with `FindFirstBuildCache(TreeScope_Descendants, RuntimeId == …)` (`FindInDocument`, `BrowseDocumentTracker.cs:395`). That searches the whole document, and it runs under the 4 s transaction timeout round 6 set. Round 7 only raised the timeout for whole-document captures.
- **Activation** (`BrowseDocumentTracker.cs:415`): on a large page, Enter or Space on a link or button can time out. The user hears the error cue and nothing is activated.
- **Updates** (`ProcessPendingChanges`, `BrowseDocumentTracker.cs:330`): a timeout, or any other UIA exception for one changed element, escapes the `foreach`. The rest of the batch is dropped, with only a debug log line, so the buffer silently misses changes.
- [ ] Run `FindInDocument` with the document-capture timeout too (it is a whole-tree search).
- [ ] Catch exceptions per changed element in `ProcessPendingChanges`. When a lookup fails, request a full re-capture (once per batch) instead of dropping the change.
- [ ] (verify) How long `FindFirst` by runtime id takes on a 5,000- to 10,000-element page.

## P2 — Smaller issues

### 3. Caps Lock turned on by a double tap isn't reflected in typing echo (round-7 interaction)
Since round 7, double-tapping Caps Lock while it is the screen reader key turns Caps Lock on. But the hook reports `CapsLockOn = ScreenReaderModifier != CapsLock && CapsLockOn` (`KeyboardHook.cs:377`), which is always false when Caps Lock is the modifier. Letters typed in capitals are then echoed, and mapped through the keyboard layout, as lower case. Report the tracked Caps Lock state whatever the modifier; the tracker already flips it when the tap passes through.

### 4. The tracker's set of captured runtime ids grows for as long as a page stays open
`_capturedIds` (`BrowseDocumentTracker.cs:355`) is only cleared when a new document is loaded. Every subtree re-capture adds its ids, and removed elements are never taken out. On a page that stays open for hours and keeps changing (a chat app, a social feed, a dashboard), the set grows without limit, one string per element ever seen. Rebuild it from scratch on every whole-document capture. If it grows past a limit (e.g. four times the size of the last whole capture), clear it; focus changes then fall back to the ancestor walk, and that is still correct.

### 5. A cue can be lost when the idle timer closes the output at the same moment
`Play` calls `EnsureMixer()` and then `AddMixerInput`, and restarts the idle timer only after that (`AudioCuePlayer.cs:105`). If the idle timer fires in between, `ResetOutput` closes the device and discards the mixer. The cue is added to a mixer nothing is playing, so it isn't heard. Stop the idle timer before getting the mixer, and add the input under the output lock, so the mixer can't be closed in between.
