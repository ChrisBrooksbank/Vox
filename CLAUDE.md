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

The `Vox.App` build copies `assets/config/default-settings.json` and `default-keymap.json` into `bin/.../assets/config/` (MSBuild target in `Vox.App.csproj`); both are resolved relative to `AppContext.BaseDirectory` at runtime. User settings live in `%APPDATA%/Vox/settings.json`; Serilog logs go to `%APPDATA%/Vox/logs/`.

## Architecture

Startup: `Program.cs` → generic host → `ServiceRegistration.RegisterServices` (all components are DI singletons) → `ScreenReaderService` (the single `IHostedService`), which initializes UIA, subscribes to UIA events, installs the keyboard hook, runs the first-run wizard if `FirstRunCompleted` is false, and wires pipeline events to navigation components.

Event flow:

```
KeyboardHook (WH_KEYBOARD_LL) ─> bounded channel ─> KeyInputDispatcher ─┐  (KeyMap lookup → NavigationCommandEvent or RawKeyEvent)
UIAEventSubscriber (UIA callbacks) ──────────────────────────────────────┤
                                                                         v
                                       EventPipeline: Channel<ScreenReaderEvent>, single reader
                                       - coalesces FocusChangedEvents within 30ms (keeps last)
                                       - speaks focus / live-region / mode / typing-echo events directly
                                       - re-raises C# events: NavigationCommandReceived,
                                         RawKeyReceived, FocusChangedProcessed
                                                                         │
            ScreenReaderService handlers ─> NavigationManager (Browse/Focus mode state machine)
                                          ─> QuickNavHandler (H/K/D/F/T/1-6 over VBufferDocument indices)
                                          ─> SayAllController, TypingEchoHandler, AnnouncementBuilder
                                                                         v
                                       SpeechQueue: Channel<Utterance> ─> ISpeechEngine (SapiSpeechEngine)
```

- **Event types** are records deriving from `ScreenReaderEvent` in `Pipeline/ScreenReaderEvent.cs`. Producers post via `IEventSink.Post` (non-blocking `TryWrite`). To add a new event, add the record and a `case` in `EventPipeline.ProcessEventAsync`.
- **SpeechQueue priorities**: `Interrupt > High > Normal > Low`. Any `Interrupt` in a drained batch cancels current speech and drops everything else except the last interrupt; Normal utterances within 50ms are concatenated. User navigation and focus changes use `Interrupt`/`High`; polite live regions use `Low`.
- **UIA threading**: `UIAThread` owns a dedicated STA thread; every COM/UIA call must go through `UIAThread.RunAsync(...)` (bridged with `TaskCompletionSource`). UIA event handler callbacks in `UIAEventSubscriber` only post to the pipeline and return.
- **Virtual buffer** (`Buffer/`): `VBufferBuilder` walks an `IVBufferElement` tree (abstraction over UIA elements, mockable in tests) into a `VBufferDocument` with a flat text string plus prebuilt indices (Headings, Links, FormFields, Landmarks, FocusableElements). `VBufferCursor` moves by line/word/char over that; `IncrementalUpdater` splices rebuilt subtrees on `StructureChanged`. Note: no UIA-backed `IVBufferElement` adapter exists yet and nothing in `Vox.App` calls `QuickNavHandler.SetDocument`, so the buffer is not yet built from live pages.
- **Settings**: `SettingsManager` + `SettingsMonitor` expose `IOptionsMonitor<VoxSettings>`; read `CurrentValue` at use time for live reload. `VerbosityProfile` controls what `AnnouncementBuilder` includes per `VerbosityLevel`.
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
