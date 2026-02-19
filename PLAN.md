# Vox: Windows 11 Screen Reader — Implementation Plan

## Context

Building a new open-source screen reader for Windows 11 in C#/.NET, prioritizing **web browsing excellence** and **performance**. The current landscape has NVDA (Python, free, dominant at 65.6% usage), JAWS (proprietary, $95-$1,475/yr, best enterprise support), and Narrator (built-in, weak web browsing). There's an opportunity for a modern, high-performance C#/.NET screen reader that leverages Windows 11's improved UIA support and modern .NET performance characteristics.

**Target framework**: `net9.0-windows`

---

## High-Level Architecture

Event-driven, layered system with six subsystems. Out-of-process model using UIA (with future native helper DLL for in-process IAccessible2 performance):

```
  Input Manager (keyboard hooks) ──┐
  UIA Provider (focus/events)   ───┤──> Event Pipeline ──> Output Manager (speech/braille)
  Virtual Buffer (web content)  ───┤        │
  App Modules (per-app logic)   ───┘        v
                                    Navigation Manager (browse/focus mode)
```

**Core flow**: Keystroke captured → matched against current mode keymap → navigation command → query virtual buffer or UIA → event pushed to pipeline → coalesced → speech output.

---

## Project Structure

```
Vox.sln
├── src/
│   ├── Vox.Core/                    # Core library
│   │   ├── Accessibility/           # UIA/MSAA providers, event subscribers
│   │   ├── VirtualBuffer/           # Document model, builder, cursor
│   │   ├── Speech/                  # SAPI engine, priority queue, utterances
│   │   ├── Input/                   # Keyboard hook, keymaps, gesture recognition
│   │   ├── Navigation/              # Browse/focus mode, quick nav
│   │   ├── Pipeline/                # Event queue, coalescing, dispatch
│   │   ├── Braille/                 # LibLouis wrapper (Phase 2)
│   │   ├── Configuration/           # JSON settings
│   │   └── AppModules/              # Per-app behavior (Chrome, Edge, Explorer)
│   ├── Vox.App/                     # Entry point, DI, system tray, hosted service
│   └── Vox.NativeHelper/            # C++/CLI DLL for in-process IA2 (Phase 3)
├── tests/
│   └── Vox.Core.Tests/
└── assets/
    ├── sounds/                      # Earcon WAV files
    └── config/                      # Default keymap + settings JSON
```

---

## Key NuGet Packages

| Package | Purpose |
|---------|---------|
| `Interop.UIAutomationClient` | COM interop for UIA3 |
| `System.Speech` | SAPI5 speech synthesis |
| `FlaUI.UIA3` | Higher-level UIA wrapper (reference) |
| `Microsoft.Extensions.Hosting` | App lifecycle, DI, logging |
| `Vanara.PInvoke.User32` | Clean P/Invoke for keyboard hooks |
| `NAudio` | Low-latency audio for earcons |
| `Serilog` | Structured logging |

---

## Phased Implementation

### Phase 1: MVP — "Read a Web Page" (Weeks 1-8)

**Goal**: Working screen reader that can read web pages in Edge/Chrome with browse mode navigation.

**Week 1-2: Foundation**
- Create solution structure, projects, DI setup
- COM interop wrappers for `IUIAutomation` (create `CUIAutomation`, tree walkers, cache requests)
- `SapiSpeechEngine`: init `SpeechSynthesizer`, implement `SpeakAsync` with cancellation, select OneCore voice
- `Program.cs` with `Microsoft.Extensions.Hosting`, `ScreenReaderService` as `IHostedService`

**Week 3-4: Input & Focus Tracking**
- `KeyboardHook`: P/Invoke `SetWindowsHookEx(WH_KEYBOARD_LL)`, post events to channel (hook callback must be < 1ms)
- `UIAEventSubscriber`: subscribe to `FocusChanged` events
- `UIAProvider`: on focus change, read `Name`, `ControlType`, `Value`, `State` → speak "[Name] [ControlType]"
- `EventPipeline`: `Channel<ScreenReaderEvent>` main loop with coalescing

**Week 5-6: Virtual Buffer**
- `VBufferBuilder`: detect `ControlType.Document`, walk UIA tree depth-first via `TreeWalker`
- Build flat text + `VBufferNode` graph with `TextRange` offsets per node
- Use `IUIAutomationCacheRequest` to batch property retrieval (Name, ControlType, AriaRole, AriaProperties)
- `VBufferCursor`: position tracking, line/word/char movement

**Week 7-8: Browse Mode**
- `NavigationManager`: browse/focus mode state machine
- Quick nav: H (headings), K (links), T (tables), F (form fields), 1-6 (heading levels)
- Arrow keys: line/character navigation through flat text
- Enter: invoke UIA `Invoke` pattern on current element
- Insert+Space: toggle browse/focus mode
- Tab/Shift+Tab: move between focusable elements

**MVP delivers**: Launch app → open browser → hear page title → arrow through content → H to jump headings → K to jump links → Enter to activate → auto-switch to focus mode in form fields.

### Phase 2: Robust Desktop (Weeks 9-16)
- MSAA bridge for legacy Win32 apps
- App modules: File Explorer, Task Manager, Settings
- Braille display support (LibLouis via P/Invoke)
- Configuration system (JSON), preferences UI
- Object navigation (Insert+Arrow), Say All (Insert+Down)
- Audio cues (mode change, boundary sounds)
- System tray icon + basic settings dialog

### Phase 3: Performance & Web Excellence (Weeks 17-24)
- Native C++/CLI helper DLL for in-process IAccessible2 virtual buffer
- Incremental buffer updates via UIA `StructureChanged` events
- Table navigation (Ctrl+Alt+Arrow)
- ARIA live region support
- Form field auto-pass-through
- Firefox support via IAccessible2

### Phase 4: Extensibility & Polish (Weeks 25-32)
- Plugin architecture (MEF-based)
- Azure AI voices integration
- MSIX installer + auto-update
- Localization framework

---

## Key Technical Decisions

1. **UIA-first for web content** — Chrome 126+ and Edge expose full UIA trees; sufficient for MVP without IAccessible2 complexity
2. **Out-of-process start** — Pure C# initially; add native in-process DLL in Phase 3 for performance
3. **Priority-based speech queue** — Interrupt (user navigation), High (focus changes), Normal (automatic), Low (background)
4. **Dedicated STA thread for UIA** — COM objects require single-threaded apartment; bridge to async pipeline via `TaskCompletionSource`
5. **Keyboard hook must be < 1ms** — Post to `Channel<T>` immediately, process on separate thread; Windows unhooks slow callbacks

---

## Critical Technical Challenges

| Challenge | Mitigation |
|-----------|------------|
| UIA tree walk performance (thousands of elements) | `FindAll` with `TrueCondition` + `CacheRequest` to batch; native DLL in Phase 3 |
| Virtual buffer staleness (SPAs/AJAX) | Subscribe to `StructureChanged` events, rebuild affected subtree only |
| Speech latency (first utterance delay) | Pre-init engine at startup; `SpeakAsyncCancelAll` before each new utterance |
| COM threading (STA requirements) | Dedicated STA thread for all UIA; async bridges to pipeline |
| Hook timeout (Windows unhooks slow callbacks) | Hook callback only posts to channel; zero processing in callback |

---

## Verification Plan

1. **Unit tests**: Virtual buffer builder with mock UIA trees, event pipeline coalescing, speech queue priority
2. **Integration test**: Launch app → open Edge to a known test page → verify speech output matches expected announcements
3. **Manual testing**: Navigate Wikipedia, GitHub, Google — verify heading/link navigation, mode switching, form interaction
4. **Performance benchmark**: Measure time from keystroke to first speech byte on a page with 1000+ elements (target: < 100ms)
5. **Accessibility test pages**: Use W3C WAI-ARIA examples and NVDA's own test pages to validate ARIA role handling
