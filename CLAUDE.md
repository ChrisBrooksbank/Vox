# CLAUDE.md — Vox Screen Reader

## Project Overview

Vox is a Windows 11 screen reader built in C#/.NET 9 (`net9.0-windows`). It uses UI Automation (UIA) for accessibility, SAPI5 for speech, and a virtual buffer model for web browsing.

## Build & Run

```bash
dotnet build                          # Build entire solution
dotnet run --project src/Vox.App      # Run the screen reader
dotnet test                           # Run all tests
```

## Architecture

- **Event-driven pipeline**: Keyboard hooks and UIA events feed into a `Channel<ScreenReaderEvent>` pipeline with coalescing
- **UIA on dedicated STA thread**: All COM/UIA calls happen on a single STA thread; bridged to async via `TaskCompletionSource`
- **Keyboard hook callback must be < 1ms**: Only post to channel in the hook callback, never do processing
- **Priority speech queue**: Interrupt > High > Normal > Low — user navigation always interrupts

## Key Conventions

- Target framework: `net9.0-windows` (all projects)
- Use `Microsoft.Extensions.Hosting` for DI and app lifecycle
- Use `System.Threading.Channels` for inter-component communication
- Prefer `IUIAutomationCacheRequest` for batching UIA property reads
- All UIA interop goes in `Vox.Core/Accessibility/`
- Speech engine abstracted behind `ISpeechEngine` interface
- Input handling abstracted behind `IKeyboardHook` interface

## Project Layout

- `src/Vox.Core/` — Core library (all subsystems)
- `src/Vox.App/` — Entry point, DI registration, `ScreenReaderService`
- `tests/Vox.Core.Tests/` — Unit tests
- `assets/config/` — Default keymap and settings JSON
- `PLAN.md` — Full phased implementation plan

## Testing

- Unit test virtual buffer, event pipeline, and speech queue
- Mock UIA interfaces for testability
- Integration tests launch the app against known test pages

## Important Notes

- Requires Windows 11 and admin privileges for keyboard hooks
- Chrome 126+ and Edge required for full UIA web content trees
- Do not block the keyboard hook thread — it will be unhooked by Windows
- COM objects are apartment-threaded; never share UIA objects across threads without marshaling
