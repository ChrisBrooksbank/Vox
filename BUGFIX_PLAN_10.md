# Bug Fix Plan — Round 10

This is a tenth static review, done on 2026-10-11. It covers code added since round 9 (which reviewed `master` at `e11f928`), up to the app modules commit `e21d7c0`:
- Milestone C braille: the display drivers (HID, BRLTTY), the display manager, the controller, viewport, presenter, key maps and braille input, `BrailleUia.cs` and `Vox.App/BrailleService.cs`;
- Milestone D app modules: `AppModuleManager` and `IAppModule`, the Office object-model thread (`OfficeObjectModel`), the Word, Excel, Outlook, PowerPoint, File Explorer, Teams, Zoom and quiet modules, `AppUiaSources.cs`, the pipeline hooks (`EventFilter`, `FocusAnnouncementFormatter`, `EventProcessed`), `KeyInputDispatcher.SetAppKeyMap` and the wiring in `ScreenReaderService` and `ServiceRegistration`.

The review looked for threading mistakes, races, leaks, wrong offsets, late-bound COM calls that fail at run time, reconnect loops, key handling, secure-mode and privacy problems, and focus announcements spoken out of order. Candidates were traced through the code before being accepted. Six hold up. These were looked at and rejected:
- **BRLTTY write packet.** The text of blanks, the zero AND mask and the cells as the OR mask give the cells as written, as brlapi's `writeDots` does.
- **liblouis called from two threads.** Translation runs under the controller's lock and back-translation on the display's key thread, but `LibLouisTranslator` takes one process-wide lock around every native call.
- **The braille router deadlocking the UIA thread.** Every `IBrailleRouter` method hands its work to the UIA thread or the pipeline and returns at once. Nothing is awaited under the controller's lock.
- **Indexed properties through `dynamic`** (`sheet.Cells(r, c)`, `range.Address(false, false)`, `SlideShowWindows(1)`). The COM binder invokes them as property gets with arguments, which Office's `IDispatch` accepts. Each optional read is also wrapped in `Try`.
- **Process names cached by process id.** A reused id could pick the wrong module. That needs the cached process to exit and its id to be reused while the cache still holds it, and the cost is a wrong key layer until another app is focused. Not worth the cost of looking names up on every switch.

The baseline is 2,389 tests passing on Linux, 2 skipped, plus 5 that need Windows (STA threads and WinForms). Items marked **(verify)** depend on Windows or Office behaviour and need confirming there.

## Status (2026-10-11)

Review written; fixes in progress.

Tasks use the `IMPLEMENTATION_PLAN.md` checkbox format and are ordered by severity. Each fix has a regression test in `tests/Vox.Core.Tests/` where the code is testable.

## P1 — Wrong behaviour in common use

### 1. Word's next and previous heading commands never move
`WordObjectModelSource.MoveToHeading` (`WordSource.cs:305`) calls `selection.GoTo(-1, forward ? 2 : 3)` with the comment "wdGoToHeading". But -1 is `wdGoToBookmark`; `wdGoToHeading` is 11. Without a bookmark name, Word either throws or leaves the selection where it was:
- if it throws, `RunAsync` gives null and Vox says "Not available while Word is busy";
- if it doesn't move, the start check says "No next heading".

Insert+Alt+H and Insert+Alt+Shift+H never reach a heading.
- [ ] Named constants for `WdGoToItem.wdGoToHeading` (11) and `WdGoToDirection` next (2) and previous (3).
- [ ] Test (`WordObjectModelSourceTests`, over a plain stand-in for Word's objects): the next heading goes through `GoTo(11, 2)` and is described with its level; the previous one uses `GoTo(11, 3)`.

### 2. PowerPoint never reads a shape's text
`PowerPointObjectModelSource.ReadSlide` (`PowerPointSource.cs:55`) reads `(bool)shape.HasTextFrame && (bool)shape.TextFrame.HasText`. In PowerPoint both are `MsoTriState`, which arrives as an `int` (-1 or 0). Casting a boxed `int` to `bool` through `dynamic` throws `RuntimeBinderException`, and `Try` turns that into null. As a result:
- every shape's text is null;
- `IsTitle` finds no title, so slide changes say only "Slide 3 of 10";
- a slide show reads only the pictures' alt text;
- the shape commands say no text.
- [ ] `ReadSlide` reads both through an `IsTrue` helper that accepts a `bool` or a non-zero number (msoTrue is -1, msoCTrue 1).
- [ ] Test (`PowerPointObjectModelSourceTests`): with `HasTextFrame` and `HasText` given as -1, the title and body text are read; with 0, the text is null.

### 3. In Excel, moving from cell to cell may say nothing (verify)
`ExcelModule` silences a cell's focus announcement and starts reading the cell from the object model (`ReadCell`, sequence n). `UIAEventSubscriber.FollowFocusForSelection` subscribes to `ElementSelected` on the grid that holds the focused cell. When an arrow key moves the active cell, Excel also selects the new cell, and that event can come after the focus event.

`OnEvent` then starts a selection-only read (sequence n + 1). The cell read that was still waiting is dropped as stale. The selection read says nothing for a single cell (`Selection` is null). The user hears nothing.
- [ ] A selection read isn't started while a full cell read is still waiting: that read already includes the selection.
- [ ] Test (`ExcelModuleTests`): a cell focus followed at once by `ElementSelectedEvent` still says the cell.
- [ ] (verify) the order in which Excel raises focus and ElementSelected events on an arrow key.

## P2 — Wrong behaviour in less common situations

### 4. A late cell, row or item reading interrupts the next control's announcement
The Excel, Outlook and File Explorer modules each do the same thing:
1. silence a focus announcement;
2. start an object-model or UIA read;
3. drop the answer only when another read of their own has started since (`sequence != _sequence`).

Focus moving to a control the module doesn't handle starts no read, so the waiting answer is still spoken. Examples:
- arrowing to a cell and pressing Ctrl+F;
- arrowing in the message list and tabbing to the reading pane;
- pressing F2 on a file.

In each case Vox announces the new control, and the old answer then arrives at Interrupt priority (Excel and Explorer after the first item, Outlook always) and cuts it off. In Outlook, auto-read then also starts reading the old message's body.

Going back to the application has the same problem: `OnActivated` doesn't start a new sequence, so an answer from before Alt+Tab is still current.
- [ ] Excel, Outlook and Explorer move to a new sequence when focus goes to a control they don't handle, and in `OnActivated`.
- [ ] Tests: with the object model held back, focus moving to another control drops the cell (Excel), message (Outlook) and row (Explorer) answers.

### 5. Braille display commands move the hidden page's cursor from other applications
`BrailleController.Run` hands a bound command to `BrailleRouter.RunCommand`, which posts it to the pipeline as a `NavigationCommandEvent`. Keys reach the pipeline through `KeyInputDispatcher`, which only resolves Browse-mode bindings while a web document has focus. Braille bindings have no mode, so their commands bypass that check:
- BRLTTY's `top`, `bottom`, `lineBegin`, `lineEnd` and paragraph keys (`brltty.json`) send `TopOfDocument`, `StartOfLine` and similar commands;
- the common chords (`common.json`) send `NextHeading` and `NextLink`.

`BrowseModeController.HandleCommand` keeps the last page's document and mode after focus has left it. Pressing one of those keys in Notepad or Word therefore moves the browser's hidden cursor and reads a line or heading from a page that isn't on screen.
- [ ] `HandleCommand` runs the browse-only commands (cursor and caret moves, selection, quick navigation, tables, the page summary and the other buffer commands after the mode check) only while the document is active. The keyboard never sends them otherwise, so typed keys behave as before.
- [ ] Test (`BrowseModeControllerTests`): after focus moves to Notepad, `TopOfDocument` and `NextHeading` neither speak nor move the cursor; back on the page, they work.

### 6. A timed-out Office call can leave the Office modules unusable (verify)
`OfficeObjectModel` caches each application's object in one dictionary shared by every Office STA thread. A generation counter clears the cache when a timeout replaces the thread.

The stuck call keeps running on the old thread. When it returns, it can still:
- write `_applications[app]` with a proxy belonging to the old thread's apartment;
- remove an entry while the new thread is reading the dictionary.

This is a data race on a non-thread-safe `Dictionary`. The new thread may also have reset its generation before the write, in which case it keeps the stale proxy. Every call then fails with `RPC_E_WRONG_THREAD`, which isn't one of the "disconnected" codes that drop the entry, and the Excel, Word, Outlook and PowerPoint modules give "busy" answers until the next timeout.

The stuck call is most likely inside the lookup itself (`AccessibleObjectFromWindow` or `GetActiveObject` on a busy Office), and that is exactly when timeouts happen.
- [ ] The cache is per thread (`ThreadLocal`): each STA thread only ever sees objects it made, and the generation counter goes.
- [ ] No unit test: the object model only runs on Windows with Office.
- [ ] (verify) after Office hangs past a timeout and recovers, Excel cells are read from the object model again.
