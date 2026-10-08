# Implementation Plan

## Status

- Planning iterations: 2
- Build iterations: 116
- Last updated: 2026-10-08

## Tasks

### Configuration & Settings (spec: first-run-experience.md)

- [x] Add `VoxSettings` record with all config fields: VerbosityLevel, SpeechRateWpm, VoiceName, TypingEchoMode, AudioCuesEnabled, AnnounceVisitedLinks, ModifierKey, FirstRunCompleted (spec: first-run-experience.md)
- [x] Add `VerbosityLevel` enum (Beginner/Intermediate/Advanced) and `TypingEchoMode` enum (None/Characters/Words/Both) and `ModifierKey` enum (Insert/CapsLock) (spec: first-run-experience.md)
- [x] Add `VerbosityProfile` class with three built-in profiles defining what fields are announced at each level (spec: first-run-experience.md)
- [x] Add `SettingsManager` loading from %APPDATA%/Vox/settings.json with fallback to assets/config/default-settings.json; use IOptionsMonitor for live reload; register in DI (spec: first-run-experience.md)
- [x] Create assets/config/default-settings.json with sensible defaults (200 WPM, Beginner verbosity, Insert modifier, Both echo) (spec: first-run-experience.md)

### Input System (spec: input-system.md)

- [x] Add `KeyEvent` as `readonly struct`: VkCode, Modifiers (Shift/Ctrl/Alt/Insert flags enum), IsKeyDown, Timestamp (spec: input-system.md)
- [x] Add `NavigationCommand` enum: NextHeading, PrevHeading, NextLink, PrevLink, NextLandmark, PrevLandmark, HeadingLevel1-6, NextLine, PrevLine, NextWord, PrevWord, NextChar, PrevChar, ActivateElement, ToggleMode, SayAll, StopSpeech, ElementsList, ReadCurrentLine, ReadCurrentWord (spec: input-system.md)
- [x] Add `IKeyboardHook` interface with Install/Uninstall methods and KeyPressed event (spec: input-system.md)
- [x] Add `KeyMap` class loading from assets/config/default-keymap.json; maps (Modifiers, VkCode, InteractionMode) to NavigationCommand (spec: input-system.md)
- [x] Create assets/config/default-keymap.json with NVDA-convention keybindings (Insert as modifier) (spec: input-system.md)
- [x] Add `KeyboardHook` implementing IKeyboardHook via P/Invoke SetWindowsHookEx(WH_KEYBOARD_LL); callback extracts vkCode, posts pre-allocated KeyEvent to bounded channel via TryWrite, returns immediately (spec: input-system.md)
- [x] Add channel consumer on separate thread: tracks modifier state, looks up KeyMap, dispatches NavigationCommand or posts KeyEvent to pipeline (spec: input-system.md)
- [x] Add `TypingEchoHandler`: character echo on key-up of printable chars; word echo on Space/Enter/punctuation using rolling buffer; respects TypingEchoMode setting (spec: input-system.md)
- [x] Wire KeyboardHook into ScreenReaderService.StartAsync/StopAsync; register in DI (spec: input-system.md)
- [x] Add unit tests for KeyMap resolution (modifier+key -> command) and TypingEchoHandler (char echo, word echo, None mode) (spec: input-system.md)

### UIA Accessibility (spec: uia-accessibility.md)

- [x] Add `UIAThread` class: starts a dedicated STA thread, provides RunAsync<T>(Func<T>) that marshals via TaskCompletionSource; all UIA COM operations must use this (spec: uia-accessibility.md)
- [x] Add `UIAProvider` creating CUIAutomation on the STA thread; builds IUIAutomationCacheRequest with: Name, ControlType, AriaRole, AriaProperties, IsEnabled, HasKeyboardFocus, ItemStatus, LiveSetting, ClassName (spec: uia-accessibility.md)
- [x] Add `UIAEventSubscriber` implementing UIA event handler interfaces (IUIAutomationFocusChangedEventHandler, IUIAutomationStructureChangedEventHandler, IUIAutomationPropertyChangedEventHandler, IUIAutomationEventHandler for LiveRegionChanged/Notification); handlers only post to channel and return immediately (spec: uia-accessibility.md)
- [x] Subscribe UIAEventSubscriber to: FocusChanged, StructureChanged, PropertyChanged (Name/ExpandCollapseState), LiveRegionChanged (event 20024), Notification (IUIAutomation5) (spec: uia-accessibility.md)
- [x] Add `LiveRegionMonitor`: Dictionary<runtimeId, lastKnownText> for diffing; polite regions throttled to 1 per 500ms per source; assertive immediate (spec: uia-accessibility.md)
- [x] Wire UIAThread, UIAProvider, UIAEventSubscriber into ScreenReaderService; register in DI (spec: uia-accessibility.md)
- [x] Add unit tests for LiveRegionMonitor: diff detection (new text vs unchanged), polite throttling (1 per 500ms), assertive bypass (spec: uia-accessibility.md)

### Virtual Buffer (spec: virtual-buffer.md)

- [x] Add `VBufferNode` class: Id, UIARuntimeId, Name, ControlType, AriaRole, HeadingLevel(0-6), LandmarkType, IsLink, IsVisited, IsRequired, IsExpandable, IsExpanded, IsFocusable, TextRange, Parent, Children, NextInOrder, PrevInOrder (spec: virtual-buffer.md)
- [x] Add `VBufferDocument` class: FlatText, Root node, AllNodes in document order; pre-built index collections: Headings, Links, FormFields, Landmarks, FocusableElements; FindByRuntimeId(), FindNodeAtOffset() (spec: virtual-buffer.md)
- [x] Add `VBufferBuilder`: detects ControlType.Document, walks UIA tree depth-first with cached TreeWalker; parses AriaRole for heading levels/landmark types/link status; parses AriaProperties for required/expanded/visited/live; builds all indices; target <500ms for 1000-element page (spec: virtual-buffer.md)
- [x] Add `VBufferCursor`: position as (currentNode, textOffset); movement: NextLine, PrevLine, NextWord, PrevWord, NextChar, PrevChar; boundary detection posts boundary.wav cue; wrap with wrap.wav (spec: virtual-buffer.md)
- [x] Add `IncrementalUpdater`: on StructureChanged event, identifies changed subtree by RuntimeId; rebuilds only changed subtree; splices into existing document; recalculates text offsets (spec: virtual-buffer.md)
- [x] Add unit tests for VBufferBuilder (correct node tree + properties from mock UIA tree), VBufferCursor movement (all granularities), boundary/wrap behavior, IncrementalUpdater patching (spec: virtual-buffer.md)

### Navigation (spec: navigation.md)

- [x] Add `NavigationManager`: Browse/Focus mode state machine; Browse mode consumes single-letter nav keys; Focus mode passes keys through except Insert+Space; auto-switch: Enter on edit field -> Focus mode + focus_mode.wav, focus leaves form field -> Browse mode + browse_mode.wav (spec: navigation.md)
- [x] Add `QuickNavHandler` for Browse mode: H/Shift+H next/prev heading any level; 1-6/Shift+1-6 heading at level; K/Shift+K next/prev link; D/Shift+D next/prev landmark; F/Shift+F next/prev form field; T/Shift+T next/prev table; Tab/Shift+Tab next/prev focusable element (spec: navigation.md)
- [x] Add `AnnouncementBuilder`: translates VBufferNode + VerbosityProfile into spoken text; concatenates heading level, landmark type, name, control type, visited, required, expanded/collapsed; filtered by verbosity (Beginner=all, Intermediate=control+essential state, Advanced=minimal) (spec: navigation.md)
- [x] Add `SayAllController`: Insert+Down triggers continuous reading from current position; speaks one line at a time, advances cursor; any keystroke cancels via CancellationTokenSource (spec: navigation.md)
- [x] Add `ElementsListDialog`: WinForms accessible dialog; Insert+F7 opens it; switchable tabs for Headings/Links/Landmarks/FormFields; type to filter list; Enter to jump to element; data from VBufferDocument indices (spec: navigation.md)
- [x] Wire NavigationManager, QuickNavHandler, SayAllController into ScreenReaderService; register in DI (spec: navigation.md)
- [x] Add unit tests for QuickNavHandler (next/prev finding, wrap), AnnouncementBuilder verbosity (Beginner vs Advanced output), SayAllController cancellation (spec: navigation.md)

### First-Run Wizard & Full Integration (spec: first-run-experience.md)

- [x] Add `FirstRunWizard`: speech-only, 7-step wizard; triggered when FirstRunCompleted==false; steps: welcome, rate(Up/Down live adjust+test sentence), voice(Up/Down cycle), verbosity(1/2/3), modifier(1/2), tutorial(H/K/Enter/Insert+Space practice), completion; saves settings at each step; re-runnable from settings (spec: first-run-experience.md)
- [x] Update ScreenReaderService.StartAsync: init UIAThread, start KeyboardHook, start EventPipeline, start SpeechEngine, load settings, check FirstRun -> run wizard if needed; StopAsync: cleanup all (spec: first-run-experience.md)
- [x] Add unit tests for EventPipeline coalescing (consecutive focus events within 30ms) and priority routing (assertive live -> High, mode change -> audio cue first) (spec: first-run-experience.md)

## Commercial Parity Tasks

Milestones A–E from `COMMERCIAL_PARITY_PLAN.md`, in build order. Each task is one build iteration. Quality-engineering tasks are spread through the milestones so tests land next to the features they cover.

Prerequisites that aren't code (the loop can't do these; track them outside the checklist):
- Code-signing certificate (EV recommended) for `uiAccess`, the installer and updates — needed before the Milestone A signing tasks can be verified on a real install
- A Windows machine or self-hosted runner with an interactive desktop for desktop E2E tests
- Licence decisions: eSpeak NG (GPL) and BRLTTY (LGPL) as optional separate downloads; liblouis (LGPL) loaded dynamically
- Recruit 10–20 daily JAWS/NVDA users for a closed beta at the end of each milestone

### Milestone A — "Won't strand you"

#### Quality foundation (spec: quality-engineering.md)

- [x] Add GitHub Actions workflow `.github/workflows/ci.yml` on `windows-latest`: setup .NET 9, restore, `dotnet build`, `dotnet test` on push and pull request; upload test results (spec: quality-engineering.md)
- [x] Add `RecordingSpeechEngine` (test helper implementing `ISpeechEngine`) that records text, priority and timestamps; use it in one existing EventPipeline test (spec: quality-engineering.md)
- [x] Add a log-sink test proving typed characters, password field values and `TypingEchoEvent` text never reach Serilog; fix any leak found (spec: quality-engineering.md)

#### Robustness (spec: robustness-secure-desktop.md)

- [x] Add a timeout parameter (default 2 s) to `UIAThread.RunAsync`; throw `UIATimeoutException` on expiry and abandon the call; unit tests with a blocking delegate (spec: robustness-secure-desktop.md)
- [x] Add `UIAWatchdog`: detect an STA thread blocked past 5 s, start a replacement `UIAThread`, raise a `UIAThreadReplaced` event; unit tests with a fake blocking call (spec: robustness-secure-desktop.md)
- [x] On `UIAThreadReplaced`, re-create `CUIAutomation` and the cache request in `UIAProvider` and re-subscribe `UIAEventSubscriber` and `BrowseDocumentTracker` (spec: robustness-secure-desktop.md)
- [x] Set `IUIAutomation2.ConnectionTimeout` (2000 ms) and `TransactionTimeout` (1000 ms) when creating the automation object (spec: robustness-secure-desktop.md) — already in `UIAProvider.InitializeAsync` (2000 ms connection, 4000 ms transaction, 20 s for document capture); kept the tuned 4000 ms
- [x] Speak "<app> not responding" once per process per 10 s when UIA calls to it time out; add `AppNotRespondingEvent` to the pipeline (spec: robustness-secure-desktop.md)
- [x] Make StopSpeech, Quit and other non-UIA commands run without awaiting the UIA thread; unit test that they complete while UIA is blocked (spec: robustness-secure-desktop.md)
- [x] Add an unhandled-exception and process-exit handler that uninstalls the keyboard hook first; test with a fake `IKeyboardHook` (spec: robustness-secure-desktop.md)
- [x] Add `Vox.Watchdog` console project: launches Vox.App, restarts it after a crash (max 3 per minute), then speaks a failure message through SAPI directly; restart policy unit-tested via an injectable process launcher (spec: robustness-secure-desktop.md)
- [x] Add fault-injection test helpers: a mock `IVBufferElement` provider that hangs or throws; tests that the buffer builder and tracker recover (spec: quality-engineering.md)

#### Desktop text editing (spec: desktop-text-and-controls.md)

- [x] Add `ITextDocument` / `ITextRange` abstractions (units char/word/line/paragraph, caret, selection, attributes) in `Vox.Core/Text/` (spec: desktop-text-and-controls.md)
- [x] Add `BufferTextDocument` adapter so the virtual buffer implements `ITextDocument`; existing `VBufferCursor` tests still pass (spec: desktop-text-and-controls.md)
- [x] Add `UIATextDocument` over `IUIAutomationTextPattern`/`TextPattern2` with all calls through `UIAThread`; snapshot-based fake for tests (spec: desktop-text-and-controls.md)
- [x] Subscribe to `TextSelectionChanged` and `TextEditTextChanged` for the focused element; add `CaretMovedEvent` and `TextEditedEvent` to the pipeline (spec: desktop-text-and-controls.md)
- [x] Add `TextCaretTracker`: after a caret key (arrows, Ctrl+arrows, Home/End, Ctrl+Home/End, PgUp/PgDn) speak the matching unit at the new caret; unit tests per key (spec: desktop-text-and-controls.md)
- [x] Add caret fallback for edits without `TextPattern`: `GetGUIThreadInfo` caret plus `ValuePattern` text (spec: desktop-text-and-controls.md)
- [x] Add selection speech in native edits: "selected X" / "unselected X", "all selected", "selection cleared"; unit tests with a fake `ITextDocument` (spec: desktop-text-and-controls.md)
- [x] Add deletion echo: Backspace/Delete (and Ctrl+Backspace/Delete) speak the removed text; unit tests (spec: desktop-text-and-controls.md)
- [x] Extend `TypingEchoHandler` to native edit controls (not only web documents); password edits still echo "star" (spec: desktop-text-and-controls.md): already the case (echo is only skipped in browse mode over a document; password mode follows every focus event); added a test for focus leaving the document for a native edit
- [x] Add commands (mode Any) ReadCurrentLine/Word/Char for native edits, ReadSelection (Insert+Shift+Up); keymap bindings (spec: desktop-text-and-controls.md)
- [x] Add ReadFormatting (Insert+F): font name, size, bold/italic/underline, colour, spelling error from text attributes; keymap binding (spec: desktop-text-and-controls.md)
- [x] Report "misspelled" when the caret enters a word with the spelling-error attribute (setting: speech / earcon / off) (spec: desktop-text-and-controls.md)
- [x] Make `SayAllController` work over any `ITextDocument`, so Insert+Down reads native edit controls (spec: desktop-text-and-controls.md)
- [x] Add `TerminalMonitor` for Windows Terminal and conhost: diff text on change, speak new lines, throttle to one utterance per 100 ms, collapse >20 lines, suppress echo of typed characters; unit tests (spec: desktop-text-and-controls.md)

#### Desktop controls and events (spec: desktop-text-and-controls.md)

- [x] Add `ForegroundWindowChangedEvent` (WinEvent `EVENT_SYSTEM_FOREGROUND` or UIA window opened) and speak the window title (spec: desktop-text-and-controls.md)
- [x] Add dialog auto-read: on a dialog opening, speak title plus static text that has no focusable target; unit tests over a mock tree (spec: desktop-text-and-controls.md)
- [x] Speak focused-element property changes: ToggleState, ExpandCollapseState, Value, RangeValue, IsEnabled, Name; announcement text per verbosity; unit tests (spec: desktop-text-and-controls.md)
- [x] Add progress-bar reporting with setting (every 10 % / 25 % / beep / off) and throttling; unit tests (spec: desktop-text-and-controls.md)
- [x] Speak `SelectionItem`/`Selection` changes in lists, grids, tabs and trees (spec: desktop-text-and-controls.md)
- [x] Add position info ("3 of 10", "level 2") from PositionInSet/SizeOfSet/Level to `AnnouncementBuilder`, controlled by `VerbosityProfile`; unit tests (spec: desktop-text-and-controls.md)
- [x] Menus: "menu" on open, "leaving menu" on close, submenu entry, shortcut and accelerator text; unit tests (spec: desktop-text-and-controls.md)
- [x] Tooltips, Start menu search results, Alt+Tab and Win+Tab switchers and virtual-desktop switch announcements (spec: desktop-text-and-controls.md)
- [x] Add Where-am-I commands (mode Any): SayTitle Insert+T, SayFocus Insert+Tab, SayStatusBar Insert+End, SayTime Insert+F12 (twice: date), SayBattery Insert+Shift+B, ReadWindow Insert+B; keymap bindings and tests (spec: desktop-text-and-controls.md)

#### Privileges and secure screens (spec: robustness-secure-desktop.md)

- [x] Add app manifest with `uiAccess="true"` to Vox.App and an MSBuild signing target that runs only when a certificate is configured; detect and log missing `uiAccess` at startup (spec: robustness-secure-desktop.md)
- [x] Add `--secure` mode to Vox.App: no add-ons, no settings writes, no network/AI, settings read from a system location; unit tests of the mode switches (spec: robustness-secure-desktop.md)
- [x] Add "use current settings on sign-in screens" command that copies settings to the system location (spec: robustness-secure-desktop.md)
- [x] Add `Vox.Service` Windows service project: watch session/desktop switches and launch Vox in secure mode on the Winlogon desktop; switching logic unit-tested behind an interface (spec: robustness-secure-desktop.md)
- [x] Add start-at-logon setting (scheduled task or Run key) (spec: robustness-secure-desktop.md)

#### Audio and diagnostics (spec: robustness-secure-desktop.md)

- [x] Move speech and earcon output to WASAPI through NAudio with output device selection setting (spec: robustness-secure-desktop.md) — earcons and tones done (AudioOutputDevice setting); speech stays on the SAPI default device until the OneCore engine task, which picks the device for speech
- [x] Add audio ducking setting (Off / While speaking / Always) (spec: robustness-secure-desktop.md)
- [x] Keep the audio device warm with a silent stream so the first syllable after idle isn't clipped (spec: robustness-secure-desktop.md)
- [x] Add speech viewer window (non-focusable, lists recent utterances) toggled by a command (spec: robustness-secure-desktop.md)
- [x] Add developer info command (Insert+F1): speak and copy name, control type, ARIA role, framework, process and runtime id of the focused element (spec: robustness-secure-desktop.md)
- [x] Add latency instrumentation (timestamps at hook, dispatcher, pipeline, queue, engine) and log p95 every minute; unit test the percentile tracker (spec: robustness-secure-desktop.md)
- [x] Add latency test: injected key → first `SpeakAsync` call, using `RecordingSpeechEngine`; fail above budget (spec: quality-engineering.md)

### Milestone B — "Daily driver, home use"

#### Review cursor and object navigation (spec: review-object-navigation.md)

- [x] Add `ObjectNavigator` over the UIA control view (parent, first child, next/previous sibling) with boundary cue; unit tests over a mock tree (spec: review-object-navigation.md)
- [x] Add simple-review mode (skip unnamed Group/Pane layout objects); unit tests (spec: review-object-navigation.md)
- [x] Add object navigation commands and keymap bindings: Insert+Numpad8/2/4/6, report Insert+Numpad5, to focus Insert+Numpad-, focus to navigator Insert+Shift+Numpad-, activate Insert+NumpadEnter (spec: review-object-navigation.md)
- [x] Add `ReviewCursor` over `ITextDocument` for the navigator object, focused document or window; Numpad 7/8/9, 4/5/6, 1/2/3; unit tests (spec: review-object-navigation.md)
- [x] Add spell on double press and phonetic (NATO) spell on triple press for review char/word and read-current commands; unit tests (spec: review-object-navigation.md)
- [x] Review follows focus/caret setting and tether to virtual buffer in browse mode (spec: review-object-navigation.md)
- [x] Add mouse tracking setting: speak element under pointer, throttled `ElementFromPoint` (100 ms), unit text setting (spec: review-object-navigation.md)
- [x] Add mouse commands: route mouse to navigator, left click, right click, lock left button; keymap bindings (spec: review-object-navigation.md)
- [x] Add copy navigator text to clipboard (Insert+Ctrl+C) (spec: review-object-navigation.md)
- [x] Add laptop keymap layout (CapsLock modifier, no numpad); `KeyMap` loads a named layout; test that it covers every desktop-layout command (spec: review-object-navigation.md)
- [x] Add Desktop/Laptop layout step to the first-run wizard (spec: review-object-navigation.md)

#### Speech and audio (spec: speech-and-audio.md)

- [x] Extend `ISpeechEngine` with pitch, volume and capability flags; implement in `SapiSpeechEngine` (spec: speech-and-audio.md)
- [x] Add `OneCoreSpeechEngine` over `Windows.Media.SpeechSynthesis` with voice listing, rate, pitch, volume, playing through NAudio WASAPI on the `AudioOutputDevice` (so speech follows the output device setting) (spec: speech-and-audio.md)
- [x] Add `SpeechEngineRegistry`: list engines, switch at runtime, fall back to SAPI if an engine fails; prefer OneCore by default; unit tests (spec: speech-and-audio.md)
- [x] Add rate boost up to 900 wpm (engine boost or time-stretch) with `SpeechRateWpm` range update and wizard support (spec: speech-and-audio.md) — engine boost: OneCore speaks up to 900 wpm natively (speaking rate 5), SAPI up to its rate 10 (~540 wpm); engines report `MaxRateWpm` and the wizard follows it; no time-stretching
- [x] Add settings ring (Insert+Ctrl+Left/Right choose, Up/Down change: voice, rate, pitch, volume, punctuation, engine); saved and spoken; unit tests (spec: speech-and-audio.md)
- [x] Add `TextProcessor` stage before `SpeechQueue` with a pluggable rule chain; no-op by default; unit tests (spec: speech-and-audio.md)
- [x] Add punctuation levels None/Some/Most/All and `assets/speech/symbols-en.json` seeded from CLDR; unit tests per level (spec: speech-and-audio.md)
- [x] Add CLDR emoji names to the symbol dictionary; unit tests (spec: speech-and-audio.md)
- [x] Add pronunciation dictionaries (default, per-voice, user; plain and regex; case options) loaded from `%APPDATA%/Vox/dictionaries/`; unit tests (spec: speech-and-audio.md)
- [x] Add capital indication settings (pitch / "cap" / beep, separately for char and word/line); unit tests (spec: speech-and-audio.md)
- [x] Add repeated-character collapsing ("4 dashes") and number-as-digits setting; unit tests (spec: speech-and-audio.md)
- [x] Add language tag to `Utterance`, carry `lang` from buffer nodes, and `LanguageSwitcher` voice selection with user mapping; unit tests (spec: speech-and-audio.md)
- [x] Add speech history (last 100; Insert+Shift+F11/F12 step back/forward, press twice to copy); unit tests (spec: speech-and-audio.md)
- [x] Add sleep mode per app (Insert+Shift+S): no speech and no key swallowing while that app has focus; persisted; unit tests (spec: speech-and-audio.md)
- [x] Add eSpeak NG engine as an optional separately-downloaded component loaded through the registry (spec: speech-and-audio.md)
- [x] Add earcon scheme folder with manifest and new cues (list, table, landmark, clickable, error, progress); `AudioCuePlayer` loads the selected scheme; asset tests (spec: speech-and-audio.md)
- [x] Add optional indentation tones and "states as sound only" setting (spec: speech-and-audio.md)

#### Web parity (spec: web-parity.md)

- [x] Add quick-nav indices and commands for buttons (B), edits (E), combo boxes (C), check boxes (X), radio buttons (R); keymap bindings; unit tests (spec: web-parity.md)
- [x] Add quick-nav for lists (L), list items (I), graphics (G), block quotes (Q), frames (M), separators (S), embedded objects (O); unit tests (spec: web-parity.md)
- [x] Add quick-nav for visited (V) / unvisited (U) links, skip past links (N), paragraph (P), unvisited heading on this page (J), container start/end (,); unit tests (spec: web-parity.md)
- [x] Add Buttons, form-field-kind and Tables tabs to the Elements List (spec: web-parity.md)
- [x] Add `TableModel` (cells, spans, header cells, dimensions, layout-table heuristic) built by `VBufferBuilder`; unit tests (spec: web-parity.md)
- [ ] Add `TableNavigator`: Ctrl+Alt+arrows/Home/End, row/column and changed-header announcements, boundary cue, dimensions on entry; unit tests (spec: web-parity.md)
- [ ] Add read current row / column commands and manual header row/column set, persisted per URL and table index (spec: web-parity.md)
- [ ] Add list entry ("list with N items"), nesting level and "out of list" announcements while reading; unit tests (spec: web-parity.md)
- [ ] Add `FindInBuffer` with accessible prompt (Insert+Ctrl+F), next/previous (Insert+F3 / Insert+Shift+F3), wrap cue, history; unit tests (spec: web-parity.md)
- [ ] Add browse-mode selection (Shift/Ctrl+Shift+arrows, Shift+Home/End) with "selected" speech and Ctrl+C copy; unit tests (spec: web-parity.md)
- [ ] Add mark (Insert+F9) and select-from-mark (Insert+F10); unit tests (spec: web-parity.md)
- [ ] Capture and announce descriptions (`aria-describedby`, `aria-description`) per verbosity; unit tests (spec: web-parity.md)
- [ ] Capture and announce `aria-invalid` + error message, `aria-current`, `aria-pressed`, `aria-sort`, `aria-roledescription`, `aria-keyshortcuts`, `aria-details`; unit tests (spec: web-parity.md)
- [ ] Announce annotations (comments, insertions, deletions, highlights) on entry/exit; unit tests (spec: web-parity.md)
- [ ] Restrict the buffer to an open `<dialog>`/`aria-modal` element; unit tests (spec: web-parity.md)
- [ ] Announce "clickable" for click-handling elements without link/button roles; figures with captions; `<abbr>` expansion setting (spec: web-parity.md)
- [ ] Add "screen layout" setting (inline elements on one line vs one per line) to `VBufferBuilder`; unit tests (spec: web-parity.md)
- [ ] Add activation fallback: simulate a click at the clickable point when no pattern applies (spec: web-parity.md)
- [ ] Add page summary command and say-link-URL command; unit tests (spec: web-parity.md)
- [ ] Add overlay/cookie-banner detection with Insert+Shift+D to activate Reject/Close; never auto-dismiss; unit tests (spec: web-parity.md)
- [ ] Add Firefox document detection and Gecko `IVBufferElement` provider over UIA; document what's missing for an IA2 provider (spec: web-parity.md)
- [ ] Verify Electron/WebView2 document detection; default `role="application"` documents to Focus mode; per-app default mode setting; unit tests (spec: web-parity.md)
- [ ] PDF in Edge: read tagged PDF tree in browse mode; announce "untagged document" when it has no structure (spec: web-parity.md)
- [ ] Use `TextCaretTracker` for `contenteditable` and rich editors (Google Docs) in Focus mode (spec: web-parity.md)
- [ ] Add staged buffer build (nodes around focus first, rest later) and a 10,000-node synthetic benchmark under 500 ms (spec: web-parity.md)
- [ ] Add MathML speech through MathCAT with interactive exploration inside an expression (spec: web-parity.md)

#### Spoken-output testing (spec: quality-engineering.md)

- [ ] Add `tests/pages/` corpus: ARIA APG patterns, tables, forms with errors, live regions, dialogs, a long page (spec: quality-engineering.md)
- [ ] Add `Vox.E2E.Tests` project: core services in-process with `RecordingSpeechEngine`, `SendInput` key injector, Playwright driving Edge against the corpus; one heading-navigation scenario (spec: quality-engineering.md)
- [ ] Add approved-transcript snapshot comparison and an approve tool for E2E tests (spec: quality-engineering.md)
- [ ] Add E2E scenarios for every corpus page in Chrome and Edge; Firefox once supported (spec: quality-engineering.md)
- [ ] Add BenchmarkDotNet project: buffer build at 1k/10k/50k nodes, cursor movement, `TextProcessor` throughput (spec: quality-engineering.md)

#### Settings, help and install — W8a (spec: product-settings-help-install.md)

- [ ] Add settings schema version and migration of older settings files; unit tests (spec: product-settings-help-install.md)
- [ ] Add command descriptions and categories alongside `NavigationCommand` (one source used by help, gestures dialog and guide); unit test every command has one (spec: product-settings-help-install.md)
- [ ] Add user keymap layering from `%APPDATA%/Vox/keymap.json` with conflict detection; unit tests (spec: product-settings-help-install.md)
- [ ] Add input help mode (Insert+1): keys speak command name and description; unit tests (spec: product-settings-help-install.md)
- [ ] Add accessible `SettingsDialog` shell with General and Speech tabs, applying changes live (spec: product-settings-help-install.md)
- [ ] Add Verbosity, Browse mode, Document formatting, Audio cues, Keyboard and Mouse tabs to `SettingsDialog` (spec: product-settings-help-install.md)
- [ ] Add input gestures dialog (categories, add/remove bindings, conflicts) writing the user keymap (spec: product-settings-help-install.md)
- [ ] Add command search: type part of a command name, hear its keys (spec: product-settings-help-install.md)
- [ ] Add Vox menu (Insert+N) and system tray icon: Settings, Input gestures, Speech viewer, Help, Pause speech, Exit (spec: product-settings-help-install.md)
- [ ] Add user guide HTML in `docs/guide/` with a keyboard reference generated from the keymap at build time; open from the Vox menu (spec: product-settings-help-install.md)
- [ ] Extend the first-run wizard with rerunnable tutorial lessons per topic (web, desktop editing, review) (spec: product-settings-help-install.md)
- [ ] Add installer project (signed MSI/MSIX to Program Files, shortcut with Ctrl+Alt+V, optional start at logon, installs `Vox.Service` and `Vox.Watchdog`) (spec: product-settings-help-install.md)
- [ ] Add portable copy creation from the Vox menu with settings stored beside the exe (spec: product-settings-help-install.md)
- [ ] Add update checker: signed release feed (stable/beta), spoken "update available", download, signature check, install on request; unit tests for feed parsing and signature rejection (spec: product-settings-help-install.md)

### Milestone C — "Braille-ready"

- [ ] Add `IBrailleDisplay`, `IBrailleTranslator` and a fake display/translator for tests (spec: braille.md)
- [ ] Add `LibLouisTranslator` (dynamic P/Invoke, forward with cursor mapping, back-translation); integration test skipped when the DLL is absent (spec: braille.md)
- [ ] Add braille settings (table, cursor shape/blink, message timeout, focus context mode, tether, show selection) to `VoxSettings` (spec: braille.md)
- [ ] Add `BraillePresenter` for focused objects with focus-context modes and abbreviated role/state labels; unit tests (spec: braille.md)
- [ ] Present the caret line in native edits and the cursor line in browse mode, with cursor and selection dots 7–8; unit tests (spec: braille.md)
- [ ] Add panning, word wrap and tether to focus/review; unit tests (spec: braille.md)
- [ ] Add routing keys: move caret/virtual cursor, second press activates; unit tests (spec: braille.md)
- [ ] Add flash messages (announcements shown for N seconds); unit tests (spec: braille.md)
- [ ] Show the uncontracted word at the cursor in contracted braille (setting); unit tests (spec: braille.md)
- [ ] Add `HidBrailleDisplay` driver for the Windows HID braille usage page (spec: braille.md)
- [ ] Add `BrailleDisplayManager`: auto-detect, reconnect on plug/unplug, driver choice (spec: braille.md)
- [ ] Add braille input: chord → character through back-translation, chord commands, per-display key maps in `assets/braille/keymaps/`; unit tests (spec: braille.md)
- [ ] Add `BrlttyDisplay` (BrlAPI client) as an optional component (spec: braille.md)
- [ ] Add Braille tab to `SettingsDialog` (spec: braille.md)

### Milestone D — "Workplace-ready"

#### App modules (spec: office-apps.md)

- [ ] Add `IAppModule` and `AppModuleManager` (foreground-process selection, keymap layers, announcement overrides); unit tests (spec: office-apps.md)
- [ ] Add Office object-model helper on its own STA thread with timeouts; unit tests with a fake (spec: office-apps.md)
- [ ] Word: quick navigation in documents (headings, tables, lists, comments, revisions, spelling errors) (spec: office-apps.md)
- [ ] Word: comments and tracked changes announced on caret entry; comments tab in Elements List (spec: office-apps.md)
- [ ] Word: page/section/line/column report command; table cell navigation through `TableNavigator` (spec: office-apps.md)
- [ ] Excel: cell move announcement (address, value, formula indicator) and read-formula command; unit tests (spec: office-apps.md)
- [ ] Excel: user-defined title row/column per sheet spoken on move; unit tests (spec: office-apps.md)
- [ ] Excel: selection range size, sheet switch, filters, comments/notes, validation messages, chart summary (spec: office-apps.md)
- [ ] Outlook: message list column order and status position settings; unit tests (spec: office-apps.md)
- [ ] Outlook: reading pane auto-read, header fields command, calendar day/week summaries, meeting request actions (spec: office-apps.md)
- [ ] PowerPoint: slide number/title on change, shape navigation, speaker notes, slideshow reading (spec: office-apps.md)
- [ ] Teams: message navigation, filtered new-message announcements, meeting control and call state (spec: office-apps.md)
- [ ] File Explorer details view: column headers with values; unit tests (spec: office-apps.md)
- [ ] VS Code: avoid double-speaking its accessibility signals; Visual Studio, Zoom and Acrobat basics (spec: office-apps.md)

#### Profiles, languages, enterprise — W8b (spec: product-settings-help-install.md)

- [ ] Add configuration profiles (named overrides, per-app activation, Say All trigger, manual switch, merge order default < app < trigger < manual); unit tests (spec: product-settings-help-install.md)
- [ ] Move all spoken and UI strings to `.resx` and use `IStringLocalizer` in `AnnouncementBuilder`, `ControlTypeNames`, wizard and dialogs; pseudo-locale test (spec: product-settings-help-install.md)
- [ ] Add language setting and per-language symbol dictionaries, braille tables and key names (spec: product-settings-help-install.md)
- [ ] Add RTL text order in the buffer and braille; unit tests (spec: product-settings-help-install.md)
- [ ] Document the translator workflow and add the first translations (es, fr, de, pt, zh, ja, ar, hi) (spec: product-settings-help-install.md)
- [ ] Add silent-install MSI properties, ADMX policy templates, roaming settings option, network-off guarantee (spec: product-settings-help-install.md)
- [ ] Detect RDP/Citrix sessions, document limits, and test UIA over RDP (spec: product-settings-help-install.md)
- [ ] Add desktop E2E scenarios for Notepad, Explorer and Settings (interactive runner) (spec: quality-engineering.md)
- [ ] Add fault-injection E2E runs (hung provider, browser crash, sleep/resume, session switch) and a 4-hour soak test checking memory and handles (spec: quality-engineering.md)

### Milestone E — "Better than"

#### OCR and AI (spec: ocr-ai.md)

- [ ] Add `IOcrEngine` and `WindowsOcrEngine`; screen-capture helper for window, navigator and image bounds (blocked in secure mode) (spec: ocr-ai.md)
- [ ] Present OCR results as a reviewable `ITextDocument` with word positions and click-on-word; Insert+R recognises the navigator object; unit tests (spec: ocr-ai.md)
- [ ] Offer OCR for untagged PDFs (spec: ocr-ai.md)
- [ ] Add `IImageDescriber`, consent rules (ask/allow/never; never password fields, secure desktop or private apps) and description cache; unit tests (spec: ocr-ai.md)
- [ ] Add `LocalImageDescriber` (Windows AI APIs) and `CloudImageDescriber` (user-configured provider and key) (spec: ocr-ai.md)
- [ ] Add Insert+G describe image/object with progress cue and cancellation (spec: ocr-ai.md)
- [ ] Add screen explanation question prompt with answer as a reviewable buffer (spec: ocr-ai.md)
- [ ] Add CAPTCHA detection and guidance to audio alternatives/accessibility options (no solving) (spec: ocr-ai.md)
- [ ] Add page summary and main-content suggestions (local first, cloud with consent) (spec: ocr-ai.md)

#### Extensibility (spec: extensibility.md)

- [ ] Add `Vox.Sdk` project with versioned public interfaces (app modules, global plug-ins, commands, announcement filters, speech engines, braille drivers) (spec: extensibility.md)
- [ ] Add add-on manifest format and validation (id, version, API range, author, signature); unit tests (spec: extensibility.md)
- [ ] Add `AddOnManager` (install, enable, disable, remove; collectible `AssemblyLoadContext` per add-on; version refusal with spoken reason); unit tests (spec: extensibility.md)
- [ ] Add add-on fault isolation (disable after repeated exceptions) and safe mode (`--safe`, Vox menu restart); always off in secure mode; unit tests (spec: extensibility.md)
- [ ] Add a sample add-on in `samples/` built in CI (spec: extensibility.md)
- [ ] Add add-on store client over a signed catalogue (spec: extensibility.md)
- [ ] Add remote access (encrypted connection, keys one way, speech/braille back, start/stop announcements) (spec: extensibility.md)

#### Release quality (spec: quality-engineering.md)

- [ ] Add `docs/security.md` review checklist (keyboard hook, `uiAccess`, secure mode, update channel, add-ons) (spec: quality-engineering.md)
- [ ] Add beta programme docs and a screen-reader-user issue template asking for speech viewer output and developer info (spec: quality-engineering.md)

## Completed

- [x] Week 1-2: Foundation + Speech Engine — Vox.sln, Vox.Core, Vox.App, Vox.Core.Tests projects; ISpeechEngine, SapiSpeechEngine, Utterance, SpeechQueue, IAudioCuePlayer, AudioCuePlayer, ScreenReaderEvent hierarchy, EventPipeline, Program.cs, ScreenReaderService, ServiceRegistration; 12 unit tests passing (spec: speech-engine.md)

## Notes

- Target: net9.0-windows10.0.19041.0 for all projects
- UIA on dedicated STA thread; keyboard hook callback < 1ms
- Priority speech queue: Interrupt > High > Normal > Low
- EventPipeline and ScreenReaderEvent hierarchy already implemented — don't re-implement
- VoxSettings needs to integrate with EventPipeline verbosity-level routing
- All UIA interop goes in Vox.Core/Accessibility/
- KeyboardHook goes in Vox.Core/Input/
- Settings go in Vox.Core/Configuration/
- Navigation goes in Vox.Core/Navigation/
- Virtual buffer goes in Vox.Core/Buffer/
