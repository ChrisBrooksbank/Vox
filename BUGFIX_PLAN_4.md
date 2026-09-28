# Bug Fix Plan — Round 4

This is a fourth static review, of `master` at `e0b2b88`, done on 2026-09-28 after `BUGFIX_PLAN.md`, `BUGFIX_PLAN_2.md` and `BUGFIX_PLAN_3.md` were merged. It concentrates on how the round-3 changes interact with each other and with real browser timing: automatic focus mode, mode announcements, returning to pages, and speech priorities. The baseline is 554 tests passing on Linux, plus 14 that need Windows. Items marked **(verify)** depend on Windows, UIA, Chromium or SAPI behaviour and need confirming on Windows.

Tasks use the `IMPLEMENTATION_PLAN.md` checkbox format and are ordered by severity. Each fix needs a regression test in `tests/Vox.Core.Tests/` where the code is testable.

## P0 — Common flows broken

### 1. "Focus mode" cuts off the name of the field the user just tabbed into
When focus moves to an edit field, the pipeline raises `FocusChangedProcessed` and then enqueues the focus announcement ("Search, edit") as `Interrupt`. Inside that handler, `BrowseModeController` calls `SwitchTo(Focus)` (`BrowseModeController.cs:251`), which *posts* a `ModeChangedEvent`. That event is processed next and speaks "Focus mode" as another `Interrupt` (`EventPipeline.cs:336`), cancelling the field announcement. Every Tab into a form field is heard as just "Focus mode". Escape and the Enter-on-edit switch hit the same problem when a focus change follows. NVDA only plays a sound for automatic switches.
- [ ] Add `bool Announce = true` to `ModeChangedEvent`, and an `announce` parameter to `NavigationManager.SwitchTo`. Automatic switches (focus moved to or from a field, Enter on an edit field) play the cue only. User toggles (Insert+Space, Escape) are also spoken.
- [ ] When the mode is spoken, use `High` rather than `Interrupt`, so it queues after a focus announcement already in progress.
- [ ] Test: a focus event for an edit field in the document produces one focus announcement and a mode cue, and no "Focus mode" speech.

### 2. Returning to a page with focus in an edit field lands in Browse mode, so typing runs quick-nav commands
After Alt+Tab or a tab switch, the focus event for the edit field arrives before the document is re-captured, so `HandleFocusChanged` doesn't find it in any buffer and doesn't switch modes. `HandleDocumentChanged` then resets to Browse (`BrowseModeController.cs:340`) and applies the "no automatic focus mode on load" rule. The user, who was typing in that field before switching away, is now in Browse mode: letters are swallowed or run quick-nav commands.
- [ ] In `HandleDocumentChanged`, treat a document found in the remembered-position LRU as a *return*, not a load. If its focused node needs Focus mode, switch to Focus (cue only, see task 1).
- [ ] Also remember the mode per document, and restore Focus mode on return when the focused element is the one that had focus.
- [ ] Test: load document A with an edit field focused, load B, load A again with the same edit focused → Focus mode.

### 3. Repeated focus events for the same field flip Browse mode back to Focus
Chromium often raises focus events again for an element that already has focus (value changes, re-renders, page scripts calling `focus()`), and they can be more than the pipeline's 30 ms coalescing window apart. `HandleFocusChanged` switches to Focus whenever the focused node needs it, even when it is the element that already had focus. A user who pressed Escape to read the page around a search box is thrown back into Focus mode on the next duplicate event, and their browse keys start typing into the box.
- [ ] Only switch automatically when focus has *moved*, i.e. the runtime id differs from `_lastFocusedRuntimeId` (`BrowseModeController.cs:240`) before it is updated.
- [ ] Test: focus the Edit, press Escape (Browse), send the same focus event again → still Browse.

## P1 — Wrong behaviour in common use

### 4. State changes of the newly focused item cut off its focus announcement
`HandlePropertyChanged` and `HandleElementSelected` speak through `Speak`, which uses `Interrupt` (`BrowseModeController.cs:717`). When arrowing through a list box or radio group, Chromium raises focus and then `SelectionItemIsSelected` (or toggle/expand) changes on the newly focused item. So "Banana, list item" is cut off by "selected" (`BrowseModeController.cs:298`), and "Small, radio button" by "checked".
- [ ] Speak state changes (expanded/collapsed, checked, selected) at `High`, so they queue after the focus announcement. Value changes of combo boxes and sliders can stay `Interrupt`, so rapid arrowing only speaks the latest value.
- [ ] Skip a state announcement that repeats what the focus announcement just said, e.g. an `IsSelected=true` change for an element focused less than about 300 ms ago whose focus event already reported it as selected.
- [ ] Test: focus event for a selected list item, then an `IsSelected` change for it → both are spoken, in that order, and the item's name is not cancelled.

### 5. Live regions from background apps and tabs are spoken
The live-region handler is desktop-wide (`UIAEventSubscriber.cs:306`) with no foreground check. A background chat app, a hidden browser tab or a minimised window with an `aria-live` region talks over whatever the user is doing. Notifications got a foreground filter in round 3; live regions didn't.
- [ ] Add `ProcessId` to `LiveRegionCacheRequest`, and drop live-region changes from processes that don't own the foreground window, as for notifications.
- [ ] (verify) Background tabs of the foreground browser share its process. Consider also dropping regions outside the active document, using the document runtime id the tracker already holds.

### 6. The foreground filter silences system notifications (verify)
`IsFromForeground` (`UIAEventSubscriber.cs:394`, `EventPipeline.cs:360`) drops every notification whose process doesn't own the foreground window. System notifications (toasts from `ShellExperienceHost`, the volume or brightness flyouts, Windows updates) never come from the foreground app, so they are always dropped. The round-3 plan's aim, "Windows toasts … are silent", is still not met.
- [ ] Allow `ImportantAll`/`ImportantMostRecent` notifications from any process, and keep the foreground filter for the other kinds.
- [ ] Verify on Windows which process raises toast notifications, and whether toasts arrive as UIA notification events at all. Narrator uses window-opened events for toasts; a `WindowOpened` handler for toast windows may also be needed.

### 7. AltGr characters are never echoed
Typing echo treats any Ctrl or Alt as a shortcut (`TypingEchoHandler.cs:130`). AltGr is reported as Ctrl+Alt, so on German, French, Polish and many other layouts `@`, `€`, `{`, `[`, `\`, `|` and accented letters are never echoed, and never added to the word being typed.
- [ ] When *both* Ctrl and Alt are held, map the key with those modifiers set in the `ToUnicodeEx` key state. If it produces a printable character, treat it as typed rather than as a shortcut. The US fallback table produces nothing for Ctrl+Alt, so shortcuts stay silent.
- [ ] Test, with a fake mapper: Ctrl+Alt+Q mapping to '@' is echoed "at". Ctrl+Alt+Delete-style combinations that map to nothing are not echoed.

### 8. A remembered position uses an offset from an outdated document
`TryRestorePosition` puts the cursor at the stored offset (`BrowseModeController.cs:512`), but sets `CurrentNode` by runtime id. If the page changed while the user was away (common: feeds, news, live scores), the offset lands in different text from the node, so reading resumes somewhere else and quick navigation and line navigation disagree about where the user is.
- [ ] Store the offset *within* the current node. On restore, use the node's new start plus that offset, clamped to the node's length. Use the raw offset only when the node is gone.
- [ ] Test: remember a position 5 characters into node N, re-load the document with text inserted before N → cursor is 5 characters into N.

### 9. A failed full re-capture blocks all later ones for the document
`RecaptureUnknownElement` sets `_fullRecaptureRequested` (`BrowseModeController.cs:431`), and only clears it when a root replacement arrives or the document changes. If that re-capture fails (UIA timeout, busy page), no `SubtreeChangedEvent` comes back, so no unknown element can ever trigger a re-capture for this document again. New content stays missing until the user leaves and returns.
- [ ] Record when the request was made, and allow another after a few seconds, or have the tracker post a "re-capture failed" event that clears the flag.
- [ ] Test: request, no reply, a later unknown element after the retry interval → a second request is made.

## P2 — Smaller issues

### 10. The Escape decision can differ between hook and dispatcher
`TryResolve` reads `_escapeGoesToPage` (`KeyInputDispatcher.cs:166`) both on the hook thread (to decide whether to swallow) and later on the consumer thread (to decide the command). If a popup opens or closes in between, a swallowed Escape can become a `RawKeyEvent` and do nothing, or Escape can reach the page and also leave Focus mode. Carry the decision in `KeyDecision.Context`, as round 3 did for the mode, and resolve against it.

### 11. Typing echo ignores dead keys (verify)
`KeyboardLayoutMapper` uses `ToUnicodeEx` with "don't change keyboard state" (`KeyboardLayoutMapper.cs:54`), so it can't see a pending dead key. On layouts with dead keys (French `^`, Spanish `´`, US-International `'` and `"`), the accent key echoes nothing and the next letter echoes as the plain letter ("e" instead of "ê"). Track the last dead key in `TypingEchoHandler` (`ToUnicodeEx` returns -1 for it) and combine it with the next character, for example by calling `ToUnicodeEx` for the dead key and then the letter on a scratch key-state copy, or `string.Normalize` with the combining accent.

### 12. Switching the modifier to CapsLock while Caps Lock is on leaves it stuck on
Once CapsLock becomes the screen reader modifier, every CapsLock press is swallowed (`ScreenReaderService.cs:204` → `KeyStateTracker`). If Caps Lock was on at that moment, it can never be turned off again: everything typed stays in capitals until the setting is changed back. When the modifier changes to CapsLock and the toggle state is on, turn it off once (`SendInput` a CapsLock press/release that the hook passes through, or `keybd_event`), or tell the user.

### 13. Finding the document walks the whole ancestor chain on every focus change
`FindWebDocument` makes one cross-process `GetParentElementBuildCache` call per ancestor on every focus change, up to 64 (`BrowseDocumentTracker.cs:126`). This happens even when focus moves within the document that is already loaded, and pages with deep DOMs do this on every Tab. First check whether the focused element's runtime id is in the current buffer (the tracker can keep a `HashSet` of the ids it last captured). If it is, skip the walk.

### 14. Pressing Shift or the modifier key alone stops Say All
`HandleRawKey` stops Say All on any key-down (`BrowseModeController.cs:204`), including Shift and the Insert/CapsLock modifier on their own. The spec says a keystroke cancels it. But a user who presses Insert to begin a command (Insert+Up to hear the current line) loses the reading position as soon as they touch Insert, and NVDA treats Shift as "pause". Ignore bare modifier key-downs, and leave cancelling to the command that follows.
