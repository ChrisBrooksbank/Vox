# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Vox is a Windows 11 screen reader built in C#/.NET 9 (`net9.0-windows10.0.19041.0`). It uses UI Automation (UIA) for accessibility, SAPI5 (`System.Speech`) and OneCore (`Windows.Media.SpeechSynthesis`, `OneCoreSpeechEngine`, played through the earcon output) for speech, NAudio for earcons, and a virtual buffer model for web browsing. Phase 1 (browse-mode reading of web pages in Edge/Chrome) is done; the commercial-parity milestones in `COMMERCIAL_PARITY_PLAN.md` follow (Milestone A — robustness, desktop text and controls, secure screens, audio and diagnostics — is done).

## Build, Run & Test

```bash
dotnet build                          # Build entire solution (Vox.sln)
dotnet run --project src/Vox.App      # Run the screen reader (no admin needed; see docs/signing.md for reading elevated windows)
dotnet test                           # Run all tests (xUnit + Moq)
dotnet build && dotnet test           # Validation gate — both must pass before committing

# Single test class / single test
dotnet test --filter "FullyQualifiedName~Vox.Core.Tests.Buffer.VBufferCursorTests"
dotnet test --filter "FullyQualifiedName~VBufferCursorTests.NextChar_AdvancesOffsetByOne"
```

All projects target `net9.0-windows10.0.19041.0` (the versioned Windows TFM gives the WinRT projection, used for OneCore speech) and `Vox.Core`/tests use WinForms, so running tests requires Windows; CI (`.github/workflows/ci.yml`, `windows-latest`) builds and tests every push. On Linux, `dotnet build -p:EnableWindowsTargeting=true` builds the solution. Tests needing live UIA COM are marked `[Fact(Skip = ...)]`.

Projects: `Vox.Core` (everything testable), `Vox.App` (the screen reader; `--secure` for the sign-in/lock/UAC screens), `Vox.Watchdog` (restarts Vox.App after a crash), `Vox.Service` (Windows service that keeps a secure-mode Vox on the Winlogon desktop), `Vox.Core.Tests`. Signed builds ask for UI access (`docs/signing.md`).

The `Vox.App` build copies `assets/config/default-settings.json`, `default-keymap.json`, `laptop-keymap.json` and `assets/sounds/*.wav` into `bin/.../assets/` (MSBuild target in `Vox.App.csproj`); they are resolved relative to `AppContext.BaseDirectory` at runtime. The keymaps are also embedded in Vox.Core as a fallback (`KeyMap.LoadBuiltIn`). The `KeyboardLayout` setting picks Desktop (`default-keymap.json`) or Laptop (the desktop bindings without keypad keys, overlaid by `laptop-keymap.json`; `KeyMap.LoadLayout`); a change is applied live with `KeyInputDispatcher.SetKeyMap`. User settings live in `%APPDATA%/Vox/settings.json`; Serilog logs go to `%APPDATA%/Vox/logs/`.

## Architecture

Startup: `Program.cs` → generic host → `ServiceRegistration.RegisterServices` (all components are DI singletons) → `ScreenReaderService` (the single `IHostedService`), which initializes UIA, applies settings (and re-applies changed values on change), installs the keyboard hook, runs the first-run wizard if `FirstRunCompleted` is false (before subscribing to UIA events, so focus speech can't interrupt it), subscribes to UIA events, and wires pipeline events to `BrowseModeController` and `BrowseDocumentTracker`. It also handles the app-level commands the controller raises: Quit (Insert+Q pressed twice → `IHostApplicationLifetime.StopApplication`) and RunSetup (Insert+Ctrl+S → re-runs the wizard with `KeyInputDispatcher` stopped and `SpeechQueue` suspended). `ScreenReaderService` only connects components; navigation logic lives in `BrowseModeController` (Vox.Core, unit-tested).

Event flow:

```
KeyboardHook (WH_KEYBOARD_LL) ─> bounded channel ─> KeyInputDispatcher ─┐  (KeyMap lookup → NavigationCommandEvent or RawKeyEvent)
UIAEventSubscriber (UIA callbacks) ──────────────────────────────────────┤
BrowseDocumentTracker (UIA thread: DocumentChanged / SubtreeChanged) ────┤
                                                                         v
                                       EventPipeline: Channel<ScreenReaderEvent>, single reader
                                       - coalesces FocusChangedEvents within 30ms (keeps last)
                                       - speaks focus / live-region / mode / typing-echo events directly
                                       - re-raises C# events (NavigationCommandReceived, RawKeyReceived,
                                         FocusChangedProcessed, DocumentChangedProcessed, ...)
                                                                         │
            ScreenReaderService handlers ─> BrowseModeController ─> NavigationManager (Browse/Focus mode)
                                                                 ─> QuickNavHandler + shared VBufferCursor
                                                                 ─> SayAllController, TypingEchoHandler,
                                                                    AnnouncementBuilder, Elements List
                                                                         v
                                       SpeechQueue: Channel<Utterance> ─> ISpeechEngine (SpeechEngineRegistry ─> OneCore / SAPI)
```

- **Event types** are records deriving from `ScreenReaderEvent` in `Pipeline/ScreenReaderEvent.cs`. Producers post via `IEventSink.Post` (non-blocking `TryWrite`). To add a new event, add the record and a `case` in `EventPipeline.ProcessEventAsync`.
- **SpeechQueue priorities**: `Interrupt > High > Normal > Low`. Enqueuing an `Interrupt` (or calling `CancelAll`) starts a new epoch: the utterance being spoken is cancelled immediately and anything queued earlier is dropped. Normal utterances within 50ms are concatenated. `EnqueueAndWaitAsync` completes when an utterance has been spoken (Say All uses it). Just before the engine call each utterance's text goes through `TextProcessor` (an ordered chain of `ITextRule`s registered in `ServiceRegistration`; a throwing rule is skipped); history and the utterance events keep the original text. Rules may also change the utterance (`ITextRule.ApplyTo`: `PitchOffset`, a `SoundCue` the queue plays first). Rules: `CapitalsRule` (capital indication, separate settings for a character and for a word/line), `PronunciationRule` (user `%APPDATA%/Vox/dictionaries/user.json`, then `voices/<voice>.json`, then the built-in `assets/speech/dictionary-default.json`; reloaded on change; none in secure mode), then `PunctuationRule` (`symbols-en.json` + CLDR `emoji-en.json`, generated by `tools/cldr/generate_symbols.py`; embedded in Vox.Core; counts runs of 4+ of a symbol), then `NumbersRule` (digits setting). User navigation and focus changes use `Interrupt`; polite live regions use `Low`.
- **Speech engines**: `SpeechEngineRegistry` is the `ISpeechEngine` everything speaks through; it forwards to the engine named by the `SpeechEngine` setting (null: OneCore, then SAPI), switches at runtime, re-applies rate/pitch/volume/voice to a new engine, and falls back to the next engine when one fails to start or fails while speaking. `OneCoreSpeechEngine` plays its audio through `AudioCuePlayer` (`IAudioStreamPlayer`), so it uses the earcon output device.
- **Key handling**: `KeyboardHook` tracks modifiers itself (the LL hook reports left/right VK codes; missed key-ups are reconciled with `GetAsyncKeyState` and reset on session switch) and asks `IKeyboardHook.SuppressionFilter` (set by `KeyInputDispatcher`) for a `KeyDecision` on each key-down; bound keys are swallowed unless the keymap entry has `"passThrough": true`. The decision (including the mode it was resolved in) travels on `KeyEvent.Decision`, so the dispatcher acts on the key exactly as decided at press time. Key-ups of command keys are not posted as `RawKeyEvent`s. `KeyModifiers.Insert` means the screen reader modifier (Insert or CapsLock per settings); every key pressed with it is swallowed, and the modifier itself is swallowed unless tapped twice quickly on its own (`ModifierTapDetector`), which passes the key's own function through. Keypad keys with Num Lock off arrive as navigation keys; `KeyEvent.IsKeypad` marks them and `NumpadKeys.BindingCodes` resolves them as Numpad0–9 / Numpad Enter (269) first, then as the key they stand for (Num Lock digits are never bound). Browse- and Focus-mode bindings only apply while a web document has focus; outside documents only "Any" bindings apply (`KeyMap.TryResolveOutsideDocument`). A focus event for an element not in the buffer turns them off at once (so typing after Ctrl+L or Alt+Tab isn't swallowed); the tracker's `FocusInDocumentEvent` turns them back on when that element is in the current document after all. In Browse mode, unbound typing keys are swallowed so they can't trigger page shortcuts. Focus mode is entered automatically when focus *moves* to an edit-like control inside the document (not for repeated focus events on the same element, nor for load-time focus on a page not visited before; returning to a remembered page with focus in a field does resume Focus mode), left automatically when focus moves to a control that doesn't need it (link, button, check box, radio button; menus and tab lists do need it), and left with Escape (passed to the page while a popup is open — an expanded combo box or menu item, or an item inside a popup menu, not a menu bar item — as decided at press time). Automatic mode switches play only the mode cue (`ModeChangedEvent.Announce = false`); user toggles are also spoken, queued at High priority.
- **UIA threading**: `UIAThread` owns a dedicated STA thread; every COM/UIA call must go through `UIAThread.RunAsync(...)` (bridged with `TaskCompletionSource`). Every call has a timeout (`DefaultTimeout` 2 s, `SetupTimeout`, `DocumentTimeout` for whole-document capture); a timed-out call throws `UIATimeoutException`, and `NotRespondingReporter` says "<app> not responding". `UIAWatchdog` replaces a thread stuck past its call's timeout and `UIARecovery` re-creates the automation object, subscriptions and document on the new one. No command handler may await the UIA thread (StopSpeech and Quit must work while it is stuck). UIA event handler callbacks in `UIAEventSubscriber` only post to the pipeline and return.
- **Desktop text**: `Vox.Core/Text` models text like UIA's TextPattern (`ITextDocument`/`ITextRange`, implemented by `UIATextDocument`, `StringTextDocument` for value-only edits, and `BufferTextDocument`). `UIAEventSubscriber.FollowFocusForTextAsync` subscribes caret/text/property/selection events on the focused element; `FocusedTextMonitor` + `TextCaretTracker` read caret moves, selection changes, deletions and spelling errors; `TerminalMonitor` reads new terminal output. Window titles and menu context are said before the focus announcement through `EventPipeline.FocusContextProvider` (said separately they would be interrupted).
- **Run policy**: `RunPolicy` (`--secure`) says what an instance may do; secure mode never writes settings or touches the user profile, network, add-ons or AI. Never log typed text or field values (`SensitiveTextLoggingTests`).
- **Object navigation and review**: `ObjectNavigator` (pure, over `INavigatorObject`, with simple-review skipping of unnamed groups/panes) and `ReviewCursor` (over any `ITextDocument`) live on the UIA thread inside `ObjectNavigationCommands` / `ReviewCommands`, which speak from within the UIA work item so quick presses are said in order. Review modes: navigator object, focused text, screen (window); while browsing, review is tethered to a `ReviewTether` snapshot of the buffer taken on the pipeline thread. `MouseTracker` polls the pointer (one `ElementFromPoint` lookup per 100 ms at most, none in flight twice) and speaks the object or line/word under it when it changes (`MouseTracking`, `MouseTextUnit`; Insert+M toggles). `MouseCommands` routes the pointer to the navigator object and clicks or locks the left button through `IMouseInput` (released on shutdown).
- **Virtual buffer** (`Buffer/`): `VBufferBuilder` walks an `IVBufferElement` tree (abstraction over UIA elements, mockable in tests) into a `VBufferDocument` with a flat text string plus prebuilt indices (Headings, Links, FormFields, Landmarks, FocusableElements, Tables). A node emits its Name only if no descendant emitted text (avoids Chromium's duplicate link/heading text). Form fields read as label plus value (never a password's value; capped at 1,000 characters in the buffer, and only the first line, up to 100 characters, is spoken on focus). `VBufferCursor` moves by line/word/char/paragraph over that, splitting lines longer than `MaxLineLength` (default 100) at a word boundary; `IncrementalUpdater` returns a new document (never mutates the old snapshot).
- **Live pages**: on each focus change `BrowseDocumentTracker` (UIA thread) finds the outermost Chromium/Gecko `Document` ancestor, captures it with one cached `BuildUpdatedCache` call into a COM-free `UIAElementSnapshot` tree, builds the buffer and posts `DocumentChangedEvent`. StructureChanged events are scoped to that document, debounced, and re-captured as `SubtreeChangedEvent`s (tagged with the document id and the element's ancestor ids, so stale updates are dropped and unknown elements are spliced at their nearest known ancestor). A failed capture is not recorded as loaded and is retried once. `BrowseModeController` keeps the cursor on the same character across updates and remembers the reading position of recently visited documents. Cross-thread results always come back as pipeline events so buffer/cursor state is only touched on the pipeline thread.
- **Settings**: `SettingsManager` + `SettingsMonitor` expose `IOptionsMonitor<VoxSettings>`; read `CurrentValue` at use time for live reload. Saves are atomic (temp file + move); a file that fails to parse on reload leaves the current settings in place.
- **Classification helpers**: `FormControls` (form field / needs-Focus-mode, by control type and ARIA role — plain `<ul>`/`<li>` are not form fields) and `ControlTypeNames` (spoken names; structural types like Text/Group are not spoken) are shared by the buffer, `NavigationManager` and `AnnouncementBuilder`. `VerbosityProfile` controls what `AnnouncementBuilder` includes per `VerbosityLevel`.
- **Testability seams**: `ISpeechEngine`, `IKeyboardHook`, `IEventSink`, `IAudioCuePlayer`, `IVBufferElement`, `ITextDocument`, `IFocusedTextSource`, `IForegroundWindow`, and injectable clocks (e.g. `LiveRegionMonitor(Func<DateTimeOffset>)`). Test helpers live in `tests/Vox.Core.Tests/TestSupport/` (`RecordingSpeechEngine`, `CapturingLogger`, `FaultyElement`). Tests mirror the `src/Vox.Core` folder layout under `tests/Vox.Core.Tests/`.

## Key Conventions

- Target framework: `net9.0-windows10.0.19041.0` (all projects); nullable and implicit usings enabled
- Use `Microsoft.Extensions.Hosting` for DI and app lifecycle; register new components in `ServiceRegistration.cs`
- Use `System.Threading.Channels` for inter-component communication
- Prefer `IUIAutomationCacheRequest` for batching UIA property reads
- All UIA interop goes in `Vox.Core/Accessibility/`
- Speech engine abstracted behind `ISpeechEngine`; input behind `IKeyboardHook`
- Keybindings belong in `assets/config/default-keymap.json` (NVDA conventions, Insert as modifier), not hardcoded

## Critical Constraints

- **Keyboard hook callback must be < 1ms**: only post to the channel, never process — Windows silently unhooks slow hooks
- COM objects are apartment-threaded; never share UIA objects across threads without marshaling
- Requires Windows 11; Chrome 126+ / Edge for full UIA web content trees. Elevated windows are readable only with UI access (signed install) or when Vox runs elevated
- Pre-initialize the speech engine at startup to avoid first-utterance delay

## Planning Workflow

Development is driven by an autonomous "Ralph loop" (`loop.sh` / `loop.ps1`, prompts in `PROMPT_plan.md` / `PROMPT_build.md`):

- `specs/*.md` — per-subsystem requirements: Phase 1 (input, UIA, virtual buffer, navigation, speech, first-run) and the commercial-parity workstreams (robustness, desktop text, review/object nav, speech & audio, web parity, braille, Office apps, product/settings, OCR & AI, extensibility, quality engineering)
- `IMPLEMENTATION_PLAN.md` — task checklist; each task references its spec. Build mode implements the first unchecked `- [ ]` task, validates with `dotnet build && dotnet test`, marks it `- [x]`, and bumps the iteration count / date
- `AGENTS.md` — short operational guide loaded every loop iteration (keep under 60 lines; keep it consistent with this file)
- `COMMERCIAL_PARITY_PLAN.md` — gap analysis against JAWS/NVDA/Narrator and the milestone order (A–E) the parity tasks follow; it reorders `PLAN.md` Phases 2–4
- `PLAN.md` — long-term phased roadmap (Phase 3 adds a C++/CLI `Vox.NativeHelper` for IAccessible2, not yet present)
