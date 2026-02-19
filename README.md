# Vox

A modern, high-performance screen reader for Windows 11, built in C#/.NET 9.

## Why Vox?

The screen reader landscape is dominated by NVDA (Python, 65.6% market share), JAWS (proprietary, up to $1,475/yr), and Narrator (limited web support). Vox aims to fill the gap with:

- **Web browsing excellence** — first-class support for Chrome and Edge via UIA
- **Performance** — C#/.NET 9 with native helpers where it counts
- **Modern architecture** — event-driven pipeline, priority-based speech, virtual buffer model
- **Open source** — MIT licensed, community-driven

## Status

**Phase 1 — MVP in progress.** Goal: read and navigate web pages in Edge/Chrome with browse mode.

## Architecture

```
Input Manager (keyboard hooks) ──┐
UIA Provider (focus/events)   ───┤──> Event Pipeline ──> Output Manager (speech/braille)
Virtual Buffer (web content)  ───┤        │
App Modules (per-app logic)   ───┘        v
                                  Navigation Manager (browse/focus mode)
```

See [PLAN.md](PLAN.md) for the full implementation plan.

## Requirements

- Windows 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Visual Studio 2022 17.8+ or VS Code with C# Dev Kit

## Building

```bash
dotnet build
```

## Running

```bash
dotnet run --project src/Vox.App
```

> **Note**: Vox requires administrator privileges to install global keyboard hooks.

## Key Bindings (Browse Mode)

| Key | Action |
|-----|--------|
| Arrow Up/Down | Previous/next line |
| Arrow Left/Right | Previous/next character |
| H / Shift+H | Next/previous heading |
| K / Shift+K | Next/previous link |
| T / Shift+T | Next/previous table |
| F / Shift+F | Next/previous form field |
| 1-6 | Next heading at level 1-6 |
| Enter | Activate current element |
| Insert+Space | Toggle browse/focus mode |
| Tab / Shift+Tab | Next/previous focusable element |

## Project Structure

```
src/
├── Vox.Core/          # Core library (accessibility, speech, input, navigation)
├── Vox.App/           # Entry point, DI, hosted service
└── Vox.NativeHelper/  # C++/CLI DLL for IAccessible2 (Phase 3)
tests/
└── Vox.Core.Tests/    # Unit and integration tests
assets/
├── sounds/            # Earcon audio files
└── config/            # Default keymaps and settings
```

## Contributing

Contributions welcome! Please read the implementation plan in [PLAN.md](PLAN.md) before starting work.

## License

MIT
