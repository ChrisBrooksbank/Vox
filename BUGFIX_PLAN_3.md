# Bug Fix Plan — Round 3

This is a third static review of the branch at `fe086aa`, done on 2026-09-28 after the fixes in `BUGFIX_PLAN.md` and `BUGFIX_PLAN_2.md`. The baseline is unchanged: 503 tests pass on Linux, and the 14 that fail need Windows (STA/COM, WinForms). The review follows real user flows end to end: tabbing into forms, typing, reading pages that keep changing, leaving and returning to the browser, and the Elements List. Items marked **(verify)** depend on runtime behaviour of Windows, UIA, Chromium or SAPI and need confirming on Windows.

Tasks use the `IMPLEMENTATION_PLAN.md` checkbox format and are ordered by severity. Each fix needs a regression test in `tests/Vox.Core.Tests/` where the code is testable.

## P0 — Typing broken, unexpected page actions

### 1. Tabbing into an edit field leaves Browse mode on, so typed letters run quick-nav commands
`NavigationManager.HandleFocusChanged` (`NavigationManager.cs:76`) only switches Focus → Browse. Focus mode is entered only by Enter on an edit field or Insert+Space. When the user Tabs or clicks into a text box, combo box or list box, Vox stays in Browse mode. The hook then swallows `h`, `k`, `d`, `f`, `t`, `1`–`6` and the arrow keys and runs them as navigation. Typing "the" into a search box moves to the next table and heading, and the field receives only "e". NVDA's default is "automatic focus mode for focus changes".
- [ ] In `BrowseModeController.HandleFocusChanged`, when the focused node is in the active document and `FormControls.NeedsFocusMode` is true, call `SwitchTo(Focus, "focus moved to edit field")`.
- [ ] Don't auto-switch for the focus a page sets as it loads (`HandleDocumentChanged`, `BrowseModeController.cs:277`). Otherwise pages that autofocus a search box, such as Google, open in Focus mode and quick navigation doesn't work. NVDA also ignores load-time focus.
- [ ] Bind Escape in Focus mode, while a document is active, to switch back to Browse mode (NVDA convention). Pass it through when the focused control is an expanded combo box or menu, so Escape still closes the popup.
- [ ] Tests: a focus event for an Edit node in the document switches to Focus. A focus event for a Button node doesn't. The focus that arrives with `DocumentChangedEvent` doesn't switch.

### 2. Unbound letter keys in Browse mode reach the page and trigger site shortcuts
`KeyInputDispatcher.Decide` (`KeyInputDispatcher.cs:105`) swallows only bound keys. In Browse mode every other printable key goes to the page. Many sites have single-key shortcuts: in Gmail, `e` archives, `r` replies and `a` replies to all; on YouTube, `j`/`l` seek and `m` mutes. A user who presses an unassigned letter in Browse mode, expecting nothing to happen, can trigger an action on the page without knowing, and some of those actions are destructive. NVDA traps non-command keys in Browse mode.
- [ ] In Browse mode with a document active, swallow printable keys (letters, digits, OEM punctuation) that have no binding and no Ctrl, Alt or screen reader modifier. Play the `error` cue, or stay silent.
- [ ] Bind Space to `ActivateElement` in Browse mode (NVDA convention), so it doesn't scroll the page out from under the virtual cursor.
- [ ] Test: in Browse mode with a document active, `Decide` swallows `E` and `Shift+/`, and passes `Ctrl+L` and `F5`.

## P1 — Wrong behaviour in common use

### 3. A failed document capture leaves stale buffer and can loop full re-captures
`DetectDocument` sets `_documentRoot`/`_documentRuntimeId` (`BrowseDocumentTracker.cs:139`) before capturing. If `BuildUpdatedCache` or the build throws (busy page, UIA timeout), `RunOnUiaThread` only logs it at Debug level. The tracker now thinks the new page is loaded. The controller still holds the previous page's buffer, because no `DocumentChangedEvent` was posted, and quick navigation reads the old page. Later focus changes in the new page hit the "same document" early return (`BrowseDocumentTracker.cs:102`), so the page is never captured.
It can get worse. Any structure change in the new page posts a `SubtreeChangedEvent`. The controller can't find it in the old buffer and calls `RequestRecapture(null)` (`BrowseModeController.cs:305`). The tracker captures the new root, which again isn't in the old buffer. This repeats: a full-document capture every ~300 ms.
- [ ] Record the document root and id only after a successful capture. On failure, log a warning, post `DocumentChangedEvent(null)` and retry once after a short delay.
- [ ] Add the document runtime id to `SubtreeChangedEvent`. The controller drops updates for a document it doesn't hold, and treats a full re-capture (root id) as a document replacement that keeps the cursor position. The "not found → re-capture" fallback then can't loop.
- [ ] Test: a `SubtreeChangedEvent` whose root isn't in the current document never requests a full re-capture more than once.

### 4. Page updates snap the virtual cursor back to the start of the current text
`HandleSubtreeChanged` restores the cursor with `node?.TextRange.Start ?? offset` (`BrowseModeController.cs:319`). After each caret move, `CurrentNode` is the text leaf under the cursor (`BrowseModeController.cs:476`). So any update anywhere on the page (clock, ticker, ad, typing indicator) moves the cursor from mid-paragraph back to the start of that text node, and the next Right-arrow or Ctrl+Down reads from the wrong place. When no node is kept, the raw offset is kept even though text before it changed length, so the cursor lands in different text.
- [ ] Keep the offset within the node: `newStart + min(oldOffset - oldStart, newLength - 1)`.
- [ ] With no node, shift the offset by the splice's text delta when the change was before the cursor. For example, have `IncrementalUpdater` report the old span and delta.
- [ ] Tests: cursor at node start + 5, unrelated earlier subtree grows by 10 → cursor stays on the same character. Same with the current node replaced by a longer version → offset inside the node is kept.

### 5. Returning to a page loses the reading position and the mode
Leaving web content (Alt+Tab, the address bar, another app) unloads the document (`UnloadDocument`). Returning re-captures it from scratch, resets the mode to Browse and moves the cursor to the focused element. That is usually the document root, which puts the cursor at the top of the page. Switching browser tabs does the same. Someone who reads half an article, checks an email and comes back has to find their place again.
- [ ] Keep a small LRU (for example, eight entries) of per-document state on the controller, keyed by document runtime id: text offset, current node runtime id and mode.
- [ ] When a `DocumentChangedEvent` arrives for a remembered document and the focused element is the document root (not a specific control), restore the saved position instead of moving to the top.
- [ ] (verify) Chromium keeps a tab's Document runtime id stable while the page stays loaded, and gives a new page a new id.
- [ ] Test: load A, move the cursor, load B, load A again with the root focused → cursor is restored.

### 6. Focus announcements that the controller ignores are still spoken
`EventPipeline.HandleFocusChangedAsync` (`EventPipeline.cs:253-258`) speaks every focus change as `Interrupt`, whatever `BrowseModeController` decided.
- After an Elements List jump, `HandleElementsListClosed` announces the chosen element. Then the focus event for the browser element that regains focus, which the controller deliberately ignores, interrupts it and announces the *old* element. The user hears where they were, not where they jumped to. **(verify the event order.)**
- A focus change with nothing to say (for example an unnamed Group or Pane) enqueues an empty `Interrupt`. That silently cuts off whatever was being spoken.
- [ ] Have the focus handler return (or set on an event-args object) whether to announce. Skip the announcement for the ignored focus-return event.
- [ ] Don't enqueue an empty or whitespace focus announcement.
- [ ] Test: dialog focus, close with a selection, then the returning focus event → only the selected element is spoken.

### 7. Typing echo uses the modifiers at key-up time
`TypingEchoHandler` echoes on key-up and reads `evt.Modifiers` from the key-up event (`TypingEchoHandler.cs:114`). For key-ups, `KeyStateTracker.Process` reports the modifiers *after* the release (`KeyStateTracker.cs:93`). Fast typists often release Shift before the letter, so capitals are echoed and buffered as lower case. If Ctrl is released before `S` in Ctrl+S, the shortcut is echoed as "s" and `s` goes into the word buffer.
- [ ] Record the modifiers and Caps Lock state from each key's key-down (per VK, `KeyModifiers[256]`), and use them when the matching key-up is echoed. Echoing on key-up is what the input spec asks for, so keep it.
- [ ] Tests: Shift down, H down, Shift up, H up → "H". Ctrl down, S down, Ctrl up, S up → nothing echoed or buffered.

### 8. The word buffer spans focus changes and caret moves
The rolling word buffer is only cleared by word boundaries and by entering a password field. Typing "abc" in one field, tabbing to the next and typing "def " speaks "abcdef". The same thing happens after moving the caret with arrows, Home/End or the mouse.
- [ ] Clear the buffer on every focus change (`BrowseModeController.HandleFocusChanged`) and on key-down of Tab, arrows, Home, End, PageUp, PageDown and Escape.
- [ ] Ctrl+Backspace clears the buffer instead of removing one character.

### 9. Enter on a link reached with the arrow keys acts on its text child (verify)
After a caret command, `CurrentNode` is the node under the cursor (`BrowseModeController.cs:476`). In Chromium's tree that is the link's or button's `Text` child, not the link. `ActivateAsync` looks up that text element (`BrowseDocumentTracker.cs:289`). A Text node has no Invoke pattern, so activation falls back to `LegacyIAccessible.DoDefaultAction` on static text. Whether that works depends on Chromium's "click ancestor" default action. Otherwise the user hears the error cue. The same applies to Enter after Say All stops on a link.
- [ ] In `ActivateCurrentNode`, walk up from `CurrentNode` to the nearest ancestor that is a link, a form field, a `FocusableControlTypes` member or `IsFocusable`, and activate that. Use it for the edit-field check in `NavigationManager.HandleCommand` too.
- [ ] Test: cursor on the Text child of a Hyperlink → `IBrowseDocumentActions.ActivateAsync` gets the Hyperlink.

### 10. Collapsed, checked and selected states are never announced
- Expandability comes from the ARIA properties string: `haspopup` or `expanded=true` (`VBufferBuilder.cs:220`, `UIAEventSubscriber.cs:173`). `expanded=false` doesn't make a node expandable, so a collapsed disclosure button or accordion is never announced as "collapsed". **(verify)** Chromium may not put `expanded` in `AriaProperties` at all. It exposes the ExpandCollapse pattern instead.
- Toggle state isn't read anywhere. Check boxes, switches and toggle buttons are announced without "checked"/"not checked", and radio buttons without "selected". Checking one (Space in Focus mode, Enter in Browse mode) says nothing, because `ToggleToggleState` isn't subscribed.
- [ ] Add `ExpandCollapseExpandCollapseState` (30070), `ToggleToggleState` (30086) and `SelectionItemIsSelected` (30079) to the focus and subtree cache requests. Carry them on `FocusChangedEvent`, `IVBufferElement` and `VBufferNode`, and use them in `AnnouncementBuilder`. `LeafNode` means "has no expand/collapse pattern": treat it as not expandable.
- [ ] Subscribe to `ToggleToggleState` changes in `SetDocumentScope`. Announce "checked", "not checked" or "half checked" for the focused element in `HandlePropertyChanged`.
- [ ] Tests: announcements for collapsed, checked, not checked and selected nodes. A property change of the toggle state on the focused element is spoken.

### 11. The Elements List may open without focus, leaving browse keys disabled (verify)
The dialog runs on a background thread in a process that isn't in the foreground. `Activate()` in `Shown` (`ElementsListDialog.cs:182`) is subject to Windows' foreground lock and can just flash the taskbar button. While the dialog is open, `_modalOpen` disables browse-mode keys. If the dialog never got focus, the user hears nothing and the browser silently stops responding to quick navigation.
- [ ] Obtain the foreground right: for example `AllowSetForegroundWindow`, or briefly `AttachThreadInput` to the foreground thread, before `SetForegroundWindow`.
- [ ] Close the dialog as cancelled on `Deactivate`, so focus leaving it always re-enables Browse mode.

### 12. An Elements List jump can use a node from a replaced document
`HandleElementsListClosed` falls back to the dialog's own node when the runtime id isn't in the current document (`BrowseModeController.cs:335`). If the page changed or navigated while the dialog was open, the cursor moves to that node's offsets in a *different* document's text, and the stale element is announced.
- [ ] If the node isn't in the current document, play the `error` cue and say "Element no longer on page" instead of moving.

## P2 — Smaller issues

### 13. Unbound screen reader key combinations type into the application
The modifier key itself is swallowed, but Insert+Q (unbound) still delivers `q` to the focused application. Swallow every key pressed while the screen reader modifier is held (NVDA behaviour). Let the modifier's own "press twice" pass-through, still to be done, deal with Insert itself.

### 14. The first-run wizard can hold the keyboard indefinitely
After Enter on the welcome step, the wizard swallows Enter, Escape, arrows and 1–3 system-wide (`FirstRunWizard.cs:61`), with no timeout, until every step is finished. Keys pressed between prompts (while "Speech rate set to…" is spoken) are swallowed and dropped, because no key waiter is set.
- [ ] Only swallow wizard keys while a prompt is waiting.
- [ ] Add an inactivity timeout, for example two minutes, that saves what was chosen so far and exits.
- [ ] Let Escape skip the rest of the wizard from any step.

### 15. The SAPI synthesizer is used from several threads (verify)
`SpeechSynthesizer` is driven from the speech queue thread (`SpeakAsync`), the pipeline thread (`SpeechQueue.Prepare` → `_engine.Cancel()`), the settings watcher thread (`SetRate`/`SetVoice`) and the wizard. `System.Speech` doesn't document these calls as thread-safe. Marshal every synthesizer call onto one thread, or confirm on Windows that concurrent `SpeakAsyncCancelAll` and `SpeakAsync` calls are safe.

### 16. UIA notification events are never spoken
`EventPipeline` only logs `NotificationEvent` (`EventPipeline.cs:202`). Windows toasts, Edge's "download complete" and similar app announcements, which the UIA spec's user stories cover, are silent. Speak them: `ImportantAll`/`ImportantMostRecent` at High priority, the others at Low. Limit them to the foreground process, and let `MostRecent` kinds replace earlier ones with the same activity id.

### 17. A change inside a newly added element forces a full-document re-capture
If a StructureChanged sender was added after the last capture, its id isn't in the buffer, and `HandleSubtreeChanged` requests a full re-capture. On pages that insert content in bursts (infinite scroll, chat), each such change re-captures the whole page. Instead, have the tracker send the element's ancestor ids (collected on the UIA thread), so the controller can splice at the nearest ancestor it knows.

### 18. Inline lines split after an incremental update
`JoinInlineRuns` runs only inside `BuildSubtree` (`VBufferBuilder.cs:139`). When a link inside a paragraph is re-captured, its text again ends in `'\n'`, so "Read the docs first" becomes separate lines again. After splicing, re-run the join for the changed node's parent's children. The replacement is the same length, so no offsets move.

### 19. Typing echo assumes a US keyboard
`VkCodeToChar` hard-codes the US layout: on a UK layout Shift+2 is echoed as "@" but types `"`. Numpad operators (`*`, `+`, `-`, `.`, `/`) are not echoed. The OEM keys are always named by their unshifted character, so Shift+/ is echoed as "slash" instead of "question mark". Use `ToUnicodeEx` with the foreground window's keyboard layout and the `0x4` flag (don't change the keyboard state, Windows 10 1607+), so dead keys aren't disturbed. Fall back to the table in tests.

### 20. Smaller navigation issues
- Arrowing onto a link, button or heading line speaks only the text, not "link"/"button"/"heading level 2". When the line's first node, or the node entered by the char or word move, belongs to a link, button or heading, add the role, gated by verbosity.
- Shift+H from inside a heading's text finds that same heading, because its text child's Id is higher than the heading's. `IndexBeforeCurrent` should start from the enclosing heading.
- Say All plays the `boundary` cue when it reaches the end of the document. It shouldn't.

### 21. No single-instance guard
Starting Vox twice installs two keyboard hooks and two sets of UIA handlers. Every key is handled twice and everything is spoken twice. Take a named mutex in `Program.cs`. If it is already held, log an error and exit. Speaking a short message first would also help.

### 22. UIA on an STA thread without a message pump (verify)
`UIAThread` is STA, as the spec requires, but it waits in `BlockingCollection.GetConsumingEnumerable` rather than a message loop. Microsoft's UIA client guidance favours MTA threads for clients that handle events, because STA clients can deadlock when adding or removing handlers. Confirm on Windows that `SetDocumentScope`, which moves handlers on each document change, doesn't stall. If it does, switch `UIAThread` to MTA or pump messages, and update the spec.
