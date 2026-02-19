# Implementation Plan

## Status

- Planning iterations: 0
- Build iterations: 1
- Last updated: 2026-02-19

## Tasks

- [x] Week 1-2: Foundation + Speech Engine — solution setup, ISpeechEngine, SapiSpeechEngine, Utterance, SpeechQueue, AudioCuePlayer, EventPipeline, Program.cs, ScreenReaderService skeleton, SpeechQueue unit tests (spec: speech-engine.md)
- [ ] Week 3-4: Input + Verbosity + Configuration — KeyboardHook, KeyEvent, KeyMap, VoxSettings, VerbosityProfile, SettingsManager, TypingEchoHandler (spec: input-system.md)
- [ ] Week 5-6: UIA Provider + Virtual Buffer + Live Regions — UIAThread, UIAProvider, UIAEventSubscriber, LiveRegionMonitor, VBufferNode, VBufferDocument, VBufferBuilder, VBufferCursor, IncrementalUpdater (spec: uia-accessibility.md, virtual-buffer.md)
- [ ] Week 7-8: Browse Mode + Context-Aware Announcements — NavigationManager, QuickNavHandler, AnnouncementBuilder, SayAllController, ElementsListDialog (spec: navigation.md)
- [ ] Week 9-10: First-Run Experience + Integration — FirstRunWizard, full ScreenReaderService wiring, ServiceRegistration (spec: first-run-experience.md)

## Completed

- [x] Week 1-2: Foundation + Speech Engine — Vox.sln, Vox.Core, Vox.App, Vox.Core.Tests projects; ISpeechEngine, SapiSpeechEngine, Utterance, SpeechQueue, IAudioCuePlayer, AudioCuePlayer, ScreenReaderEvent hierarchy, EventPipeline, Program.cs, ScreenReaderService, ServiceRegistration; 12 unit tests passing (spec: speech-engine.md)

## Notes

- Target: net9.0-windows for all projects
- UIA on dedicated STA thread; keyboard hook callback < 1ms
- Priority speech queue: Interrupt > High > Normal > Low
