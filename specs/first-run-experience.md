# First-Run Experience

## Overview

Speech-only first-run wizard, verbosity presets, settings management, and event pipeline wiring.

## User Stories

- As a new blind user, I want a guided setup experience using only speech so I can configure Vox without sighted help
- As a blind user, I want verbosity presets (Beginner/Intermediate/Advanced) so announcements match my experience level
- As a blind user, I want my settings persisted so Vox remembers my preferences

## Requirements

### Configuration
- [ ] `VoxSettings` — `VerbosityLevel` (Beginner/Intermediate/Advanced), `SpeechRateWpm` (default 200), `VoiceName`, `TypingEchoMode` (None/Characters/Words/Both), `AudioCuesEnabled`, `AnnounceVisitedLinks`, `ModifierKey` (Insert/CapsLock), `FirstRunCompleted`
- [ ] `VerbosityProfile` — Three built-in profiles:
  - Beginner: Everything. "heading level 2, navigation landmark, Products, link, visited"
  - Intermediate: Control type + essential state. "heading level 2, Products, link, visited"
  - Advanced: Minimal. "Products" — only role when ambiguous
- [ ] `SettingsManager` — Loads/saves from `%APPDATA%/Vox/settings.json`
- [ ] Falls back to `assets/config/default-settings.json`
- [ ] Uses `IOptionsMonitor<VoxSettings>` for live reload

### First-Run Wizard
- [ ] `FirstRunWizard` — Speech-only, no visual dependency
- [ ] Triggered when `FirstRunCompleted == false`
- [ ] Steps:
  1. Welcome message + Enter to continue / Escape to skip
  2. Speech rate: Up/Down arrows adjust live, speaks test sentence
  3. Voice selection: Up/Down to cycle voices
  4. Verbosity: 1=Beginner (recommended), 2=Intermediate, 3=Advanced
  5. Modifier key: 1=Insert, 2=CapsLock
  6. Quick tutorial: practice H for headings, K for links, Enter to activate, Insert+Space for mode toggle
  7. Completion: "Press Insert+F1 for help anytime"
- [ ] Settings saved at each step
- [ ] Re-runnable from settings

### Event Pipeline
- [ ] `ScreenReaderEvent` hierarchy: FocusChangedEvent, NavigationEvent, LiveRegionChangedEvent, ModeChangedEvent, TypingEchoEvent
- [ ] `EventPipeline` — `Channel<ScreenReaderEvent>` main loop (SingleReader = true)
- [ ] Coalescing: consecutive focus events within 30ms keep only last
- [ ] Routes events to speech queue with appropriate priority
- [ ] LiveRegion assertive -> High, polite -> Low
- [ ] ModeChanged -> audio cue before speech

### App Entry Point
- [ ] `ScreenReaderService` as `IHostedService`
- [ ] StartAsync: init UIA on STA thread, start KeyboardHook, start EventPipeline, start SpeechEngine, load settings, check FirstRun
- [ ] StopAsync: cleanup everything
- [ ] `ServiceRegistration` — DI registration for all services
- [ ] `Program.cs` — Host builder entry point

## Acceptance Criteria

- [ ] First-run wizard completes entirely via speech (no visual dependency)
- [ ] Settings persist to %APPDATA%/Vox/settings.json and reload correctly
- [ ] Verbosity profiles produce different announcement detail levels
- [ ] Event pipeline coalesces rapid focus changes correctly
- [ ] Unit tests for EventPipeline coalescing and priority routing

## Out of Scope

- Settings dialog GUI (Phase 2)
- System tray icon (Phase 2)
- Localization / i18n (Phase 4)
