# UIA Accessibility

## Overview

UI Automation provider running on a dedicated STA thread, with event subscriptions for focus, structure changes, and ARIA live regions.

## User Stories

- As a blind user, I want Vox to detect what's on screen automatically so I hear content as it appears
- As a blind user, I want live region updates (toasts, chat messages, SPA changes) announced automatically
- As a blind user, I want focus changes announced so I know where I am

## Requirements

- [ ] `UIAThread` — Dedicated STA thread for all UIA COM operations
- [ ] `RunAsync<T>(Func<T>)` marshals via `TaskCompletionSource<T>`
- [ ] All UIA calls route through UIAThread — never touch UIA objects from other threads
- [ ] `UIAProvider` — Creates `CUIAutomation` on STA thread
- [ ] `IUIAutomationCacheRequest` with: Name, ControlType, AriaRole, AriaProperties, IsEnabled, HasKeyboardFocus, ItemStatus, LiveSetting, ClassName
- [ ] Subscribe to: FocusChanged, StructureChanged, PropertyChanged, LiveRegionChanged (event 20024), Notification (IUIAutomation5)
- [ ] `UIAEventSubscriber` — Implements UIA event handler interfaces
- [ ] All handlers fire on UIA's background thread; only post to channel and return immediately
- [ ] `LiveRegionMonitor` — Maintains `Dictionary<runtimeId, lastKnownText>`, diffs on each event
- [ ] Polite live regions throttled to 1 announcement per 500ms from same source
- [ ] Assertive live regions: immediate, no throttle

## Acceptance Criteria

- [ ] UIA runs entirely on dedicated STA thread with no cross-thread COM violations
- [ ] Focus changes produce events in the pipeline within 30ms
- [ ] Live region changes are detected via text diff (UIA doesn't report what changed)
- [ ] Polite regions throttled; assertive regions immediate
- [ ] Unit tests for LiveRegionMonitor diff detection and throttling

## Out of Scope

- IAccessible2 / native C++/CLI helper (Phase 3)
- MSAA bridge for legacy apps (Phase 2)
- Firefox-specific IA2 support (Phase 3)
