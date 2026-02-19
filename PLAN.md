# Vox: Windows 11 Screen Reader — Improved Implementation Plan

## Context

The original plan is technically solid but has critical blind spots from the perspective of actual blind users. Based on WebAIM Screen Reader Survey #10 (1,539 respondents, 2024) and extensive UX research, this improved plan restructures priorities around what blind users truly need:

- **71.6%** use headings as primary navigation — heading nav must be flawless from day one
- **CAPTCHA** is the #1 frustration (severity score 2,172) — needs active mitigation
- **93%** of blind users customize settings — first-run setup and verbosity presets are essential, not optional
- **38%** use braille displays (up from 33.3%) — not a niche Phase 3 feature
- **ARIA live regions** are critical for SPAs (Gmail, Slack, Twitter) — moved from Phase 3 to Phase 1
- Over-verbosity causes cognitive overload; under-verbosity loses context — needs a preset system from day one

**Key changes from original plan:**
1. ARIA live regions: Phase 3 → Phase 1 (SPAs are unusable without them)
2. Verbosity presets (Beginner/Intermediate/Advanced): added to Phase 1
3. First-run accessible tutorial: added to Phase 1
4. Say All, typing echo, audio cues: added to Phase 1
5. Elements List dialog (Insert+F7): added to Phase 1
6. Context-aware announcements ("visited link", "required", heading levels, landmarks): Phase 1
7. Braille display support: moved up to early Phase 2
8. Cookie banner/overlay detection: Phase 2
9. AI image description + CAPTCHA assistance: Phase 3

**Target framework**: `net9.0-windows`

---

## Phase 1: MVP — "Read a Web Page, For Real" (Weeks 1-10)

**Goal**: A blind user launches Vox, gets guided through an accessible speech-only setup, customizes verbosity/rate, then browses the modern web with heading/link/landmark navigation, continuous reading, ARIA live region announcements, and mode-change audio cues.

### Week 1-2: Foundation + Speech Engine

**Files to create:**

- `Vox.sln` — Solution with Vox.Core, Vox.App, Vox.Core.Tests projects
- `src/Vox.Core/Vox.Core.csproj` — `net9.0-windows` class library. Packages: `Interop.UIAutomationClient`, `System.Speech`, `Vanara.PInvoke.User32`, `NAudio`, `Serilog`, `Microsoft.Extensions.Hosting.Abstractions`
- `src/Vox.App/Vox.App.csproj` — `net9.0-windows` worker service. Packages: `Microsoft.Extensions.Hosting`, `Serilog.Sinks.File`
- `tests/Vox.Core.Tests/Vox.Core.Tests.csproj` — xUnit test project

**`src/Vox.App/Program.cs`** — Host builder entry point, registers all services, starts `ScreenReaderService` as `IHostedService`

**`src/Vox.Core/Speech/ISpeechEngine.cs`** — Interface: `SpeakAsync(Utterance, CancellationToken)`, `Cancel()`, `SetRate(int wpm)`, `SetVoice(string)`, `GetAvailableVoices()`, `IsSpeaking`

**`src/Vox.Core/Speech/SapiSpeechEngine.cs`** — Wraps `System.Speech.Synthesis.SpeechSynthesizer`. Pre-inits at startup to avoid first-utterance delay. Cancel-before-speak pattern: every Interrupt-priority utterance calls `SpeakAsyncCancelAll()` first. Rate: user-facing WPM (150-450) maps to SAPI rate (-10 to +10). Selects OneCore voices by default.

**`src/Vox.Core/Speech/Utterance.cs`** — `record Utterance(string Text, SpeechPriority Priority, string? SoundCue)` with `enum SpeechPriority { Interrupt, High, Normal, Low }`

**`src/Vox.Core/Speech/SpeechQueue.cs`** — `Channel<Utterance>` backed priority queue. Interrupt cancels current speech immediately. Coalescing: multiple Normal-priority utterances within 50ms window get concatenated.

**`src/Vox.Core/Audio/IAudioCuePlayer.cs`** + **`AudioCuePlayer.cs`** — NAudio `WaveOutEvent` with pre-loaded `CachedSound` objects from `assets/sounds/`. Fire-and-forget playback. Phase 1 sounds: `browse_mode.wav`, `focus_mode.wav`, `boundary.wav`, `wrap.wav`, `error.wav`

**`src/Vox.Core/Pipeline/ScreenReaderEvent.cs`** — Event hierarchy: `FocusChangedEvent`, `NavigationEvent`, `LiveRegionChangedEvent`, `ModeChangedEvent`, `TypingEchoEvent`

**`src/Vox.Core/Pipeline/EventPipeline.cs`** — `Channel<ScreenReaderEvent>` main loop (`SingleReader = true`). Coalescing: consecutive focus events within 30ms keep only last. Routes events to speech queue with appropriate priority. LiveRegion assertive → High, polite → Low. ModeChanged → audio cue before speech.

### Week 3-4: Input + Verbosity + Configuration

**`src/Vox.Core/Input/KeyboardHook.cs`** — P/Invoke `SetWindowsHookEx(WH_KEYBOARD_LL)` via Vanara. **Critical**: hook callback must be < 1ms. Callback body: extract `vkCode`, post pre-allocated `KeyEvent` struct to bounded channel via `TryWrite`, return immediately. Zero allocation, zero processing in callback. Channel consumer on separate thread handles modifier tracking, keymap lookup, command dispatch.

**`src/Vox.Core/Input/KeyEvent.cs`** — `readonly struct` (not class) to avoid GC pressure: `VkCode`, `Modifiers` (flags: Shift/Ctrl/Alt/Insert), `IsKeyDown`, `Timestamp`

**`src/Vox.Core/Input/KeyMap.cs`** + **`assets/config/default-keymap.json`** — Maps `(Modifiers, VkCode, InteractionMode)` → `NavigationCommand`. Default keymap matches NVDA conventions (Insert or CapsLock as modifier). Commands: `NextHeading`, `PrevHeading`, `NextLink`, `PrevLink`, `NextLandmark`, `PrevLandmark`, `HeadingLevel1`-`6`, `NextLine`, `PrevLine`, `NextWord`, `PrevWord`, `NextChar`, `PrevChar`, `ActivateElement`, `ToggleMode`, `SayAll`, `StopSpeech`, `ElementsList`, `ReadCurrentLine`, `ReadCurrentWord`

**`src/Vox.Core/Configuration/VoxSettings.cs`** — `VerbosityLevel` (Beginner/Intermediate/Advanced), `SpeechRateWpm` (default 200), `VoiceName`, `TypingEchoMode` (None/Characters/Words/Both), `AudioCuesEnabled`, `AnnounceVisitedLinks`, `ModifierKey` (Insert/CapsLock), `FirstRunCompleted`

**`src/Vox.Core/Configuration/VerbosityProfile.cs`** — Three built-in profiles controlling what gets announced:
- **Beginner**: Everything. "heading level 2, navigation landmark, Products, link, visited"
- **Intermediate**: Control type + essential state. "heading level 2, Products, link, visited"
- **Advanced**: Minimal. "Products" — only role when ambiguous

**`src/Vox.Core/Configuration/SettingsManager.cs`** — Loads/saves from `%APPDATA%/Vox/settings.json`. Falls back to `assets/config/default-settings.json`. Uses `IOptionsMonitor<VoxSettings>` for live reload.

**`src/Vox.Core/Input/TypingEchoHandler.cs`** — In focus mode: character echo on key-up of printable chars, word echo on Space/Enter/punctuation (speaks preceding word from rolling buffer). Respects `TypingEchoMode` setting.

### Week 5-6: UIA Provider + Virtual Buffer + Live Regions

**`src/Vox.Core/Accessibility/UIAThread.cs`** — Dedicated STA thread for all UIA COM operations. `RunAsync<T>(Func<T>)` marshals via `TaskCompletionSource<T>`. All UIA calls route through this — never touch UIA objects from other threads.

**`src/Vox.Core/Accessibility/UIAProvider.cs`** — Creates `CUIAutomation` on STA thread. Sets up `IUIAutomationCacheRequest` with: Name, ControlType, AriaRole, AriaProperties, IsEnabled, HasKeyboardFocus, ItemStatus, LiveSetting, ClassName. Subscribes to: `FocusChanged`, `StructureChanged`, `PropertyChanged`, `LiveRegionChanged` (event 20024), `Notification` (IUIAutomation5).

**`src/Vox.Core/Accessibility/UIAEventSubscriber.cs`** — Implements UIA event handler interfaces. All handlers fire on UIA's background thread, so they only post to the channel and return immediately.

**`src/Vox.Core/Accessibility/LiveRegionMonitor.cs`** — **Critical new component** (absent from original plan). UIA fires `LiveRegionChanged` but doesn't report what text changed. Solution: maintain `Dictionary<runtimeId, lastKnownText>`, diff on each event. Polite regions throttled to 1 announcement per 500ms from same source. Assertive: immediate, no throttle. Without this, SPAs (Gmail, Slack, Twitter) are unusable.

**`src/Vox.Core/VirtualBuffer/VBufferNode.cs`** — Tree node with: Id, UIARuntimeId, Name, ControlType, AriaRole, HeadingLevel (0-6), LandmarkType, IsLink, IsVisited, IsRequired, IsExpandable, IsExpanded, IsFocusable, TextRange, Parent, Children, NextInOrder, PrevInOrder

**`src/Vox.Core/VirtualBuffer/VBufferDocument.cs`** — FlatText (all text with \n separators), Root node, AllNodes in document order, plus **pre-built indices**: Headings, Links, FormFields, Landmarks, FocusableElements. Lookup: `FindByRuntimeId()`, `FindNodeAtOffset()`

**`src/Vox.Core/VirtualBuffer/VBufferBuilder.cs`** — Detects `ControlType.Document`, walks UIA tree depth-first with cached `TreeWalker`. Parses AriaRole for heading levels, landmark types, link status. Parses AriaProperties for `required`, `expanded`, `visited`, `live`. Builds navigation indices. Target: < 500ms for 1000-element page.

**`src/Vox.Core/VirtualBuffer/VBufferCursor.cs`** — Position as `(currentNode, textOffset)`. Movement: NextLine, PrevLine, NextWord, PrevWord, NextChar, PrevChar. Boundary detection plays `boundary.wav`. Optional wrap with `wrap.wav`.

**`src/Vox.Core/VirtualBuffer/IncrementalUpdater.cs`** — On `StructureChanged`, identifies changed subtree by RuntimeId, rebuilds only that subtree, splices into existing document, recalculates text offsets. Essential for SPAs with dynamic DOM updates.

### Week 7-8: Browse Mode + Context-Aware Announcements

**`src/Vox.Core/Navigation/NavigationManager.cs`** — Browse/Focus mode state machine. Browse: single-letter nav consumed by Vox. Focus: keys pass through except Insert+Space. **Auto-switch**: Enter on edit field → Focus mode + `focus_mode.wav`. Focus leaves form field → Browse mode + `browse_mode.wav`.

**`src/Vox.Core/Navigation/QuickNavHandler.cs`** — Browse mode quick navigation:
- H / Shift+H: next/prev heading (any level)
- 1-6 / Shift+1-6: heading at specific level
- K / Shift+K: next/prev link
- D / Shift+D: next/prev landmark
- F / Shift+F: next/prev form field
- T / Shift+T: next/prev table
- Tab / Shift+Tab: next/prev focusable element

**`src/Vox.Core/Navigation/AnnouncementBuilder.cs`** — **One of the most important classes.** Translates VBufferNode + VerbosityProfile into what the user hears. This is where "confusing wall of text" vs "clear, contextual announcement" is determined. Concatenates parts: heading level → landmark type → name → control type → visited → required → expanded/collapsed, filtered by current verbosity profile.

**`src/Vox.Core/Navigation/SayAllController.cs`** — Insert+Down triggers continuous reading from current position. Speaks one line at a time, advances cursor after each utterance. Any keystroke cancels via `CancellationTokenSource`.

**`src/Vox.Core/Navigation/ElementsListDialog.cs`** — Insert+F7 opens accessible WinForms dialog with filterable list of Headings/Links/Landmarks/FormFields (switchable tabs). Type to filter, Enter to jump. Data comes from pre-built `VBufferDocument` indices, so it's instant even on large pages.

### Week 9-10: First-Run Experience + Integration

**`src/Vox.Core/FirstRun/FirstRunWizard.cs`** — Speech-only, no visual dependency. Triggered when `FirstRunCompleted == false`. Steps:
1. Welcome message + Enter to continue / Escape to skip
2. Speech rate: Up/Down arrows adjust live, speaks test sentence at each rate
3. Voice selection: Up/Down to cycle voices
4. Verbosity: 1=Beginner (recommended for new users), 2=Intermediate, 3=Advanced
5. Modifier key: 1=Insert, 2=CapsLock
6. Quick tutorial: practice H for headings, K for links, Enter to activate, Insert+Space for mode toggle
7. Completion: "Press Insert+F1 for help anytime"

Settings saved at each step. Re-runnable from settings.

**`src/Vox.App/ScreenReaderService.cs`** — `IHostedService`. StartAsync: init UIA on STA thread, start KeyboardHook, start EventPipeline, start SpeechEngine, load settings, check FirstRun. StopAsync: cleanup everything.

**`src/Vox.App/ServiceRegistration.cs`** — DI registration for all services.

### Phase 1 Tests

- `tests/Vox.Core.Tests/Speech/SpeechQueueTests.cs` — priority ordering, interrupt, coalescing
- `tests/Vox.Core.Tests/VirtualBuffer/VBufferBuilderTests.cs` — mock UIA tree → node properties
- `tests/Vox.Core.Tests/VirtualBuffer/VBufferCursorTests.cs` — movement, boundaries
- `tests/Vox.Core.Tests/Navigation/QuickNavTests.cs` — heading/link/landmark nav through mock buffer
- `tests/Vox.Core.Tests/Navigation/AnnouncementBuilderTests.cs` — verbosity profiles produce correct output
- `tests/Vox.Core.Tests/Pipeline/EventPipelineTests.cs` — coalescing, priority routing
- `tests/Vox.Core.Tests/Accessibility/LiveRegionMonitorTests.cs` — diff detection, throttling

### Phase 1 delivers: what a blind user can DO

1. Launch Vox → guided through accessible speech-only setup wizard
2. Choose speech rate, voice, verbosity level, modifier key
3. Open Edge/Chrome → hear page title
4. Arrow through content line/word/character
5. H to jump headings, K for links, D for landmarks, F for form fields, 1-6 for heading levels
6. Insert+F7 → Elements List dialog for all headings/links
7. Enter to activate links/buttons
8. Auto-switch to focus mode in form fields (with audio cue)
9. Type with character and word echo
10. Insert+Space to toggle modes manually
11. Insert+Down for continuous reading (Say All)
12. Hear ARIA live region updates automatically (toasts, chat messages, SPA content changes)
13. Hear "visited" on links, "required" on fields, heading levels, landmark names
14. Three verbosity presets customizable in settings JSON

---

## Phase 2: Robust Desktop + Braille (Weeks 11-18)

**Goal**: Braille display support, desktop app navigation, real-world web annoyance handling, object navigation, clipboard, in-buffer search.

### Week 11-12: Braille Display Support

- `src/Vox.Core/Braille/IBrailleDisplay.cs` — Interface: `IsConnected`, `CellCount`, `DisplayAsync(text)`, `InputReceived` event
- `src/Vox.Core/Braille/LibLouisTranslator.cs` — P/Invoke wrapper for `liblouis.dll`. Default table: `en-ueb-g2.ctb` (Unified English Braille Grade 2)
- `src/Vox.Core/Braille/BrailleDisplayManager.cs` — HID device connection, protocol layer for Freedom Scientific Focus + HumanWare Brailliant. Sends translated braille on every cursor/focus change. Maps routing key presses to navigation.

### Week 13-14: Object Navigation + Review Cursor + Overlay Detection

- `src/Vox.Core/Navigation/ObjectNavigator.cs` — Insert+Numpad arrows: parent/child/prev sibling/next sibling in UIA tree. Works in any app, not just browsers.
- `src/Vox.Core/Navigation/ReviewCursor.cs` — Independent review cursor: Numpad 7/8/9 (line), 4/5/6 (word), 1/2/3 (char). Double-press Numpad5 to spell word.
- `src/Vox.Core/Navigation/ClipboardManager.cs` — Insert+C: copy line. Insert+Shift+C: copy element. Ctrl+Insert+C: copy all.
- `src/Vox.Core/Navigation/OverlayDetector.cs` — Detects modal/dialog overlays (AriaRole="dialog", AriaModal, class names with "cookie"/"consent"/"banner"). Announces dialog, offers Insert+Shift+D to dismiss (looks for Accept/Reject/Close buttons). Never auto-dismisses without user consent.

### Week 15-16: App Modules + Find + Settings UI

- `src/Vox.Core/AppModules/IAppModule.cs` — Interface: `ProcessName`, `ShouldActivate()`, `OnActivated()`, `GetKeyMapOverride()`, `FormatAnnouncement()`
- `src/Vox.Core/AppModules/ChromeModule.cs` — Chrome-specific: address bar detection, tab navigation
- `src/Vox.Core/AppModules/EdgeModule.cs` — Edge-specific: Collections, Sidebar
- `src/Vox.Core/AppModules/ExplorerModule.cs` — File Explorer: tree/list view navigation
- `src/Vox.Core/Navigation/FindInBuffer.cs` — Ctrl+F search in virtual buffer. F3/Shift+F3 for next/prev. Wrap with audio cue.
- `src/Vox.Core/Configuration/SettingsDialog.cs` — Accessible WinForms dialog with tabs: Speech, Verbosity, Keyboard, Braille, Audio Cues
- `src/Vox.App/SystemTrayIcon.cs` — NotifyIcon with Settings/About/Exit. Insert+Shift+P to pause/resume.

### Week 17-18: MSAA Bridge + Audio Cues Expansion

- `src/Vox.Core/Accessibility/MSAABridge.cs` — Bridge for legacy Win32 apps that don't expose UIA
- Additional earcons: link activation, list entry/exit, table entry/exit, progress indication
- App modules: Task Manager, Windows Settings

### Phase 2 delivers

Everything from Phase 1, plus: braille display reading, object navigation in any app, review cursor, clipboard copy, cookie banner dismissal, Ctrl+F search, File Explorer/Chrome/Edge app modules, Settings dialog, system tray, MSAA for legacy apps.

---

## Phase 3: Performance, Tables, Firefox, AI (Weeks 19-26)

**Goal**: Native performance for large pages. Full table navigation. Firefox support. AI-powered image descriptions and CAPTCHA assistance.

### Week 19-21: Native C++/CLI Helper

- `src/Vox.NativeHelper/Vox.NativeHelper.vcxproj` — C++/CLI project
- `src/Vox.NativeHelper/IA2VirtualBuffer.cpp` — In-process IAccessible2 access for faster tree traversal
- `src/Vox.Core/Accessibility/IA2Bridge.cs` — Managed wrapper, falls back to pure UIA if native DLL unavailable

### Week 22-23: Table Navigation + Firefox

- `src/Vox.Core/Navigation/TableNavigator.cs` — Ctrl+Alt+Arrow: cell-by-cell with row/column header announcements. Insert+T: table dimensions.
- `src/Vox.Core/VirtualBuffer/TableModel.cs` — `VBufferTable` with cell grid, column/row headers
- `src/Vox.Core/AppModules/FirefoxModule.cs` — IA2-based Firefox support

### Week 24-26: AI Image Description + CAPTCHA Assistance

- `src/Vox.Core/AI/IImageDescriber.cs` — Interface for image description
- `src/Vox.Core/AI/WindowsAIImageDescriber.cs` — Windows Copilot Runtime local models (Win11 24H2+)
- `src/Vox.Core/AI/AzureAIImageDescriber.cs` — Cloud fallback with user-provided API key
- `src/Vox.Core/AI/CaptchaAssistant.cs` — Detects CAPTCHAs, offers: AI OCR attempt (Insert+Shift+G), audio alternative detection (Insert+Shift+A). The #1 user frustration — even partial help is valuable.

Insert+G on any image element: "Describing image..." → speaks AI-generated description. Cached per URL.

### Phase 3 delivers

Native IA2 performance for large pages, cell-by-cell table navigation with headers, Firefox support, AI image descriptions, CAPTCHA assistance.

---

## Phase 4: Extensibility & Polish (Weeks 27-34)

- `src/Vox.Core/Plugins/IVoxPlugin.cs` — MEF-based plugin system. Plugins can add commands, app modules, hook speech pipeline.
- MSIX installer + auto-update via AppInstaller
- Localization framework (`IStringLocalizer<T>`, `.resx` files)
- Azure AI premium voices
- Touch screen gesture support
- Scripting engine for power users

---

## Key Technical Decisions

1. **UIA-first for web** — Chrome 126+ and Edge expose full UIA trees; sufficient for Phase 1 without IAccessible2
2. **ARIA live regions in Phase 1** — SPAs are the modern web; delaying this makes Vox unusable for Gmail, Slack, Twitter
3. **Verbosity presets from day one** — 93% of blind users customize; three presets (Beginner/Intermediate/Advanced) cover most needs immediately
4. **Priority speech queue** — Interrupt (user nav, always cancels) > High (focus/assertive live) > Normal (automatic) > Low (polite live/background)
5. **Dedicated STA thread for UIA** — All COM on one thread, bridged to async via `TaskCompletionSource`
6. **Keyboard hook < 1ms** — Pre-allocated struct, bounded channel `TryWrite`, zero processing in callback
7. **First-run wizard is speech-only** — A blind user cannot see a visual setup screen

## Critical Technical Challenges

| Challenge | Mitigation |
|-----------|------------|
| UIA tree walk performance (1000+ elements) | `FindAll` + `CacheRequest` batch; native DLL in Phase 3 |
| Live region change detection (UIA doesn't report what changed) | RuntimeId→text dictionary, diff on each event, throttle polite regions |
| Virtual buffer staleness (SPAs) | `StructureChanged` events → incremental subtree rebuild |
| Speech first-utterance delay | Pre-init engine at startup; `SpeakAsyncCancelAll` before each new utterance |
| COM threading | Dedicated STA thread; never share UIA objects across threads |
| Hook timeout | Hook callback only posts to channel; zero processing |

## Verification Plan

1. **Unit tests**: SpeechQueue priority/interrupt, VBufferBuilder with mock UIA trees, AnnouncementBuilder verbosity profiles, LiveRegionMonitor diff/throttle, EventPipeline coalescing
2. **Integration test**: Launch → open Edge to test page → verify speech output matches expected announcements
3. **Manual testing matrix**: Wikipedia, GitHub, Google, Gmail (SPA), Twitter (live regions) — verify heading/link/landmark nav, mode switching, form interaction, live updates
4. **Performance**: Keystroke → first speech byte < 100ms on 1000-element page
5. **Accessibility test pages**: W3C WAI-ARIA examples, NVDA test pages, Deque aXe test pages
6. **First-run test**: Fresh install, verify entire wizard works speech-only with no visual dependency
7. **Verbosity test**: Same page at all three verbosity levels, verify appropriate detail at each
