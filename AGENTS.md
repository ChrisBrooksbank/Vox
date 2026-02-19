# AGENTS.md - Operational Guide

Keep this file under 60 lines. It's loaded every iteration.

## Project

Vox is a Windows 11 screen reader in C#/.NET 9 (`net9.0-windows`). UIA for accessibility, SAPI5 for speech, virtual buffer for web browsing.

## Build Commands

```bash
dotnet build                          # Build entire solution
dotnet run --project src/Vox.App      # Run the screen reader
```

## Test Commands

```bash
dotnet test                           # Run all tests
dotnet test --verbosity normal        # Run with detailed output
```

## Validation (run before committing)

```bash
dotnet build && dotnet test           # Build + test = must both pass
```

## Key Conventions

- Target framework: `net9.0-windows` (all projects)
- Use `Microsoft.Extensions.Hosting` for DI and app lifecycle
- Use `System.Threading.Channels` for inter-component communication
- All UIA interop goes in `Vox.Core/Accessibility/`
- Speech engine abstracted behind `ISpeechEngine` interface
- Input handling abstracted behind `IKeyboardHook` interface
- Prefer `IUIAutomationCacheRequest` for batching UIA property reads

## Project Layout

- `src/Vox.Core/` - Core library (all subsystems)
- `src/Vox.App/` - Entry point, DI registration, ScreenReaderService
- `tests/Vox.Core.Tests/` - Unit tests (xUnit)
- `assets/config/` - Default keymap and settings JSON

## Important Notes

- Requires Windows 11 and admin privileges for keyboard hooks
- COM objects are apartment-threaded; never share UIA objects across threads
- Keyboard hook callback must be < 1ms - only post to channel, never process
- Pre-init speech engine at startup to avoid first-utterance delay
