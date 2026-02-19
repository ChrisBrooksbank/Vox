# Implementation Plan

## Status

- Planning iterations: 1
- Build iterations: 12
- Last updated: 2026-02-19

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
- [ ] Wire KeyboardHook into ScreenReaderService.StartAsync/StopAsync; register in DI (spec: input-system.md)
- [ ] Add unit tests for KeyMap resolution (modifier+key -> command) and TypingEchoHandler (char echo, word echo, None mode) (spec: input-system.md)

### UIA Accessibility (spec: uia-accessibility.md)

- [ ] Add `UIAThread` class: starts a dedicated STA thread, provides RunAsync<T>(Func<T>) that marshals via TaskCompletionSource; all UIA COM operations must use this (spec: uia-accessibility.md)
- [ ] Add `UIAProvider` creating CUIAutomation on the STA thread; builds IUIAutomationCacheRequest with: Name, ControlType, AriaRole, AriaProperties, IsEnabled, HasKeyboardFocus, ItemStatus, LiveSetting, ClassName (spec: uia-accessibility.md)
- [ ] Add `UIAEventSubscriber` implementing UIA event handler interfaces (IUIAutomationFocusChangedEventHandler, IUIAutomationStructureChangedEventHandler, IUIAutomationPropertyChangedEventHandler, IUIAutomationEventHandler for LiveRegionChanged/Notification); handlers only post to channel and return immediately (spec: uia-accessibility.md)
- [ ] Subscribe UIAEventSubscriber to: FocusChanged, StructureChanged, PropertyChanged (Name/ExpandCollapseState), LiveRegionChanged (event 20024), Notification (IUIAutomation5) (spec: uia-accessibility.md)
- [ ] Add `LiveRegionMonitor`: Dictionary<runtimeId, lastKnownText> for diffing; polite regions throttled to 1 per 500ms per source; assertive immediate (spec: uia-accessibility.md)
- [ ] Wire UIAThread, UIAProvider, UIAEventSubscriber into ScreenReaderService; register in DI (spec: uia-accessibility.md)
- [ ] Add unit tests for LiveRegionMonitor: diff detection (new text vs unchanged), polite throttling (1 per 500ms), assertive bypass (spec: uia-accessibility.md)

### Virtual Buffer (spec: virtual-buffer.md)

- [ ] Add `VBufferNode` class: Id, UIARuntimeId, Name, ControlType, AriaRole, HeadingLevel(0-6), LandmarkType, IsLink, IsVisited, IsRequired, IsExpandable, IsExpanded, IsFocusable, TextRange, Parent, Children, NextInOrder, PrevInOrder (spec: virtual-buffer.md)
- [ ] Add `VBufferDocument` class: FlatText, Root node, AllNodes in document order; pre-built index collections: Headings, Links, FormFields, Landmarks, FocusableElements; FindByRuntimeId(), FindNodeAtOffset() (spec: virtual-buffer.md)
- [ ] Add `VBufferBuilder`: detects ControlType.Document, walks UIA tree depth-first with cached TreeWalker; parses AriaRole for heading levels/landmark types/link status; parses AriaProperties for required/expanded/visited/live; builds all indices; target <500ms for 1000-element page (spec: virtual-buffer.md)
- [ ] Add `VBufferCursor`: position as (currentNode, textOffset); movement: NextLine, PrevLine, NextWord, PrevWord, NextChar, PrevChar; boundary detection posts boundary.wav cue; wrap with wrap.wav (spec: virtual-buffer.md)
- [ ] Add `IncrementalUpdater`: on StructureChanged event, identifies changed subtree by RuntimeId; rebuilds only changed subtree; splices into existing document; recalculates text offsets (spec: virtual-buffer.md)
- [ ] Add unit tests for VBufferBuilder (correct node tree + properties from mock UIA tree), VBufferCursor movement (all granularities), boundary/wrap behavior, IncrementalUpdater patching (spec: virtual-buffer.md)

### Navigation (spec: navigation.md)

- [ ] Add `NavigationManager`: Browse/Focus mode state machine; Browse mode consumes single-letter nav keys; Focus mode passes keys through except Insert+Space; auto-switch: Enter on edit field -> Focus mode + focus_mode.wav, focus leaves form field -> Browse mode + browse_mode.wav (spec: navigation.md)
- [ ] Add `QuickNavHandler` for Browse mode: H/Shift+H next/prev heading any level; 1-6/Shift+1-6 heading at level; K/Shift+K next/prev link; D/Shift+D next/prev landmark; F/Shift+F next/prev form field; T/Shift+T next/prev table; Tab/Shift+Tab next/prev focusable element (spec: navigation.md)
- [ ] Add `AnnouncementBuilder`: translates VBufferNode + VerbosityProfile into spoken text; concatenates heading level, landmark type, name, control type, visited, required, expanded/collapsed; filtered by verbosity (Beginner=all, Intermediate=control+essential state, Advanced=minimal) (spec: navigation.md)
- [ ] Add `SayAllController`: Insert+Down triggers continuous reading from current position; speaks one line at a time, advances cursor; any keystroke cancels via CancellationTokenSource (spec: navigation.md)
- [ ] Add `ElementsListDialog`: WinForms accessible dialog; Insert+F7 opens it; switchable tabs for Headings/Links/Landmarks/FormFields; type to filter list; Enter to jump to element; data from VBufferDocument indices (spec: navigation.md)
- [ ] Wire NavigationManager, QuickNavHandler, SayAllController into ScreenReaderService; register in DI (spec: navigation.md)
- [ ] Add unit tests for QuickNavHandler (next/prev finding, wrap), AnnouncementBuilder verbosity (Beginner vs Advanced output), SayAllController cancellation (spec: navigation.md)

### First-Run Wizard & Full Integration (spec: first-run-experience.md)

- [ ] Add `FirstRunWizard`: speech-only, 7-step wizard; triggered when FirstRunCompleted==false; steps: welcome, rate(Up/Down live adjust+test sentence), voice(Up/Down cycle), verbosity(1/2/3), modifier(1/2), tutorial(H/K/Enter/Insert+Space practice), completion; saves settings at each step; re-runnable from settings (spec: first-run-experience.md)
- [ ] Update ScreenReaderService.StartAsync: init UIAThread, start KeyboardHook, start EventPipeline, start SpeechEngine, load settings, check FirstRun -> run wizard if needed; StopAsync: cleanup all (spec: first-run-experience.md)
- [ ] Add unit tests for EventPipeline coalescing (consecutive focus events within 30ms) and priority routing (assertive live -> High, mode change -> audio cue first) (spec: first-run-experience.md)

## Completed

- [x] Week 1-2: Foundation + Speech Engine — Vox.sln, Vox.Core, Vox.App, Vox.Core.Tests projects; ISpeechEngine, SapiSpeechEngine, Utterance, SpeechQueue, IAudioCuePlayer, AudioCuePlayer, ScreenReaderEvent hierarchy, EventPipeline, Program.cs, ScreenReaderService, ServiceRegistration; 12 unit tests passing (spec: speech-engine.md)

## Notes

- Target: net9.0-windows for all projects
- UIA on dedicated STA thread; keyboard hook callback < 1ms
- Priority speech queue: Interrupt > High > Normal > Low
- EventPipeline and ScreenReaderEvent hierarchy already implemented — don't re-implement
- VoxSettings needs to integrate with EventPipeline verbosity-level routing
- All UIA interop goes in Vox.Core/Accessibility/
- KeyboardHook goes in Vox.Core/Input/
- Settings go in Vox.Core/Configuration/
- Navigation goes in Vox.Core/Navigation/
- Virtual buffer goes in Vox.Core/Buffer/
