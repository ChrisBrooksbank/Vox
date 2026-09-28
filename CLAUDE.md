# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Vox is a Windows 11 screen reader built in C#/.NET 9 (`net9.0-windows`). It uses UI Automation (UIA) for accessibility, SAPI5 (`System.Speech`) for speech, NAudio for earcons, and a virtual buffer model for web browsing. Phase 1 (MVP: browse-mode reading of web pages in Edge/Chrome) is in progress.

## Build, Run & Test

```bash
dotnet build                          # Build entire solution (Vox.sln)
dotnet run --project src/Vox.App      # Run the screen reader (needs admin for keyboard hooks)
dotnet test                           # Run all tests (xUnit + Moq)
dotnet build && dotnet test           # Validation gate — both must pass before committing

# Single test class / single test
dotnet test --filter "FullyQualifiedName~Vox.Core.Tests.Buffer.VBufferCursorTests"
dotnet test --filter "FullyQualifiedName~VBufferCursorTests.NextChar_AdvancesOffsetByOne"
```

All projects target `net9.0-windows` and `Vox.Core`/tests use WinForms, so building and running tests requires Windows. Tests needing live UIA COM are marked `[Fact(Skip = ...)]`.

The `Vox.App` build copies `assets/config/default-settings.json`, `default-keymap.json` and `assets/sounds/*.wav` into `bin/.../assets/` (MSBuild target in `Vox.App.csproj`); they are resolved relative to `AppContext.BaseDirectory` at runtime. The keymap is also embedded in Vox.Core as a fallback (`KeyMap.LoadBuiltIn`). User settings live in `%APPDATA%/Vox/settings.json`; Serilog logs go to `%APPDATA%/Vox/logs/`.

## Architecture

Startup: `Program.cs` → generic host → `ServiceRegistration.RegisterServices` (all components are DI singletons) → `ScreenReaderService` (the single `IHostedService`), which initializes UIA, applies settings (and re-applies changed values on change), installs the keyboard hook, runs the first-run wizard if `FirstRunCompleted` is false (before subscribing to UIA events, so focus speech can't interrupt it), subscribes to UIA events, and wires pipeline events to `BrowseModeController` and `BrowseDocumentTracker`. `ScreenReaderService` only connects components; navigation logic lives in `BrowseModeController` (Vox.Core, unit-tested).

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
                                       SpeechQueue: Channel<Utterance> ─> ISpeechEngine (SapiSpeechEngine)
```

- **Event types** are records deriving from `ScreenReaderEvent` in `Pipeline/ScreenReaderEvent.cs`. Producers post via `IEventSink.Post` (non-blocking `TryWrite`). To add a new event, add the record and a `case` in `EventPipeline.ProcessEventAsync`.
- **SpeechQueue priorities**: `Interrupt > High > Normal > Low`. Enqueuing an `Interrupt` (or calling `CancelAll`) starts a new epoch: the utterance being spoken is cancelled immediately and anything queued earlier is dropped. Normal utterances within 50ms are concatenated. `EnqueueAndWaitAsync` completes when an utterance has been spoken (Say All uses it). User navigation and focus changes use `Interrupt`; polite live regions use `Low`.
- **Key handling**: `KeyboardHook` tracks modifiers itself (the LL hook reports left/right VK codes; missed key-ups are reconciled with `GetAsyncKeyState` and reset on session switch) and asks `IKeyboardHook.SuppressionFilter` (set by `KeyInputDispatcher`) for a `KeyDecision` on each key-down; bound keys are swallowed unless the keymap entry has `"passThrough": true`. The decision (including the mode it was resolved in) travels on `KeyEvent.Decision`, so the dispatcher acts on the key exactly as decided at press time. Key-ups of command keys are not posted as `RawKeyEvent`s. `KeyModifiers.Insert` means the screen reader modifier (Insert or CapsLock per settings). Browse-mode bindings only apply while a web document has focus (`KeyInputDispatcher.EffectiveMode`); elsewhere keys resolve as Focus mode.
- **UIA threading**: `UIAThread` owns a dedicated STA thread; every COM/UIA call must go through `UIAThread.RunAsync(...)` (bridged with `TaskCompletionSource`). UIA event handler callbacks in `UIAEventSubscriber` only post to the pipeline and return.
- **Virtual buffer** (`Buffer/`): `VBufferBuilder` walks an `IVBufferElement` tree (abstraction over UIA elements, mockable in tests) into a `VBufferDocument` with a flat text string plus prebuilt indices (Headings, Links, FormFields, Landmarks, FocusableElements). A node emits its Name only if no descendant emitted text (avoids Chromium's duplicate link/heading text). `VBufferCursor` moves by line/word/char over that; `IncrementalUpdater` returns a new document (never mutates the old snapshot).
- **Live pages**: on each focus change `BrowseDocumentTracker` (UIA thread) finds the outermost Chromium/Gecko `Document` ancestor, captures it with one cached `BuildUpdatedCache` call into a COM-free `UIAElementSnapshot` tree, builds the buffer and posts `DocumentChangedEvent`. StructureChanged events are scoped to that document, debounced, and re-captured as `SubtreeChangedEvent`s. Cross-thread results always come back as pipeline events so buffer/cursor state is only touched on the pipeline thread.
- **Settings**: `SettingsManager` + `SettingsMonitor` expose `IOptionsMonitor<VoxSettings>`; read `CurrentValue` at use time for live reload. Saves are atomic (temp file + move); a file that fails to parse on reload leaves the current settings in place.
- **Classification helpers**: `FormControls` (form field / needs-Focus-mode, by control type and ARIA role — plain `<ul>`/`<li>` are not form fields) and `ControlTypeNames` (spoken names; structural types like Text/Group are not spoken) are shared by the buffer, `NavigationManager` and `AnnouncementBuilder`. `VerbosityProfile` controls what `AnnouncementBuilder` includes per `VerbosityLevel`.
- **Testability seams**: `ISpeechEngine`, `IKeyboardHook`, `IEventSink`, `IAudioCuePlayer`, `IVBufferElement`, and injectable clocks (e.g. `LiveRegionMonitor(Func<DateTimeOffset>)`). Tests mirror the `src/Vox.Core` folder layout under `tests/Vox.Core.Tests/`.

## Key Conventions

- Target framework: `net9.0-windows` (all projects); nullable and implicit usings enabled
- Use `Microsoft.Extensions.Hosting` for DI and app lifecycle; register new components in `ServiceRegistration.cs`
- Use `System.Threading.Channels` for inter-component communication
- Prefer `IUIAutomationCacheRequest` for batching UIA property reads
- All UIA interop goes in `Vox.Core/Accessibility/`
- Speech engine abstracted behind `ISpeechEngine`; input behind `IKeyboardHook`
- Keybindings belong in `assets/config/default-keymap.json` (NVDA conventions, Insert as modifier), not hardcoded

## Critical Constraints

- **Keyboard hook callback must be < 1ms**: only post to the channel, never process — Windows silently unhooks slow hooks
- COM objects are apartment-threaded; never share UIA objects across threads without marshaling
- Requires Windows 11 and admin privileges for keyboard hooks; Chrome 126+ / Edge for full UIA web content trees
- Pre-initialize the speech engine at startup to avoid first-utterance delay

## Planning Workflow

Development is driven by an autonomous "Ralph loop" (`loop.sh` / `loop.ps1`, prompts in `PROMPT_plan.md` / `PROMPT_build.md`):

- `specs/*.md` — per-subsystem requirements (input, UIA, virtual buffer, navigation, speech, first-run)
- `IMPLEMENTATION_PLAN.md` — task checklist; each task references its spec. Build mode implements the first unchecked `- [ ]` task, validates with `dotnet build && dotnet test`, marks it `- [x]`, and bumps the iteration count / date
- `AGENTS.md` — short operational guide loaded every loop iteration (keep under 60 lines; keep it consistent with this file)
- `PLAN.md` — long-term phased roadmap (Phase 3 adds a C++/CLI `Vox.NativeHelper` for IAccessible2, not yet present)
