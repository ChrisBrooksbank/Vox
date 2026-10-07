# Robustness, Privileges & Secure Screens

## Overview

A screen reader must never leave its user without speech. This spec covers surviving hung and crashing apps, running without admin rights (signed `uiAccess` install), reading the sign-in, lock and UAC screens, audio output, diagnostics, and the keypress-to-speech latency budget. Workstream W1 of `COMMERCIAL_PARITY_PLAN.md` (Milestone A).

## User Stories

- As a blind user, I want Vox to keep talking when an app hangs so I can switch away from it
- As a blind user, I want to sign in to Windows, unlock the PC and answer UAC prompts without sighted help
- As a blind user, I want to read elevated apps (Task Manager, installers) without running Vox as admin
- As a blind user, I want Vox to restart itself if it crashes, and never leave my keyboard captured
- As a blind user, I want other audio to duck while Vox speaks, on the output device I choose

## Requirements

- [ ] `UIAThread.RunAsync` takes a timeout (default 2 s); on timeout the caller gets a `UIATimeoutException`, the stuck call is abandoned, and the provider's process is reported "not responding" (spoken once per process per 10 s)
- [ ] `UIAWatchdog`: when the STA thread stays blocked past a threshold (default 5 s), start a replacement STA thread, re-create `CUIAutomation`, re-subscribe events, and discard the old thread when it returns
- [ ] Set `IUIAutomation2.ConnectionTimeout` (2000 ms) and `TransactionTimeout` (1000 ms)
- [ ] Commands that don't need UIA (StopSpeech, Quit, speech settings ring, say time) keep working while UIA is blocked
- [ ] `Vox.Watchdog` process: starts Vox, restarts it after a crash (max 3 times per minute, then speaks a failure through SAPI directly), never holds a keyboard hook itself
- [ ] Unhandled-exception handler uninstalls the keyboard hook before the process exits
- [ ] App manifest with `uiAccess="true"`; build target that signs `Vox.App.exe` when a certificate is configured; documentation of the Program Files install requirement
- [ ] Detect missing `uiAccess` (unsigned/dev build) at startup and log it; keep working without elevated-window access
- [ ] `Vox.Service` Windows service: listens for session and desktop switches and launches Vox in secure mode on the Winlogon desktop (sign-in, lock, UAC, Ctrl+Alt+Del screens)
- [ ] Secure mode (`--secure` flag): no add-ons, no settings writes, no network, no AI, no user-profile access; reads settings copied to a system location by an explicit "use current settings on sign-in screens" command
- [ ] Start at logon option and installer-created global shortcut (Ctrl+Alt+V)
- [ ] Audio output through WASAPI with device selection; ducking of other apps while speaking (setting: Off / While speaking / Always)
- [ ] Keep the audio device warm (silent stream) so the first syllable after idle isn't clipped
- [ ] Speech viewer window: a non-focusable window listing recent utterances
- [ ] Insert+F1 speaks and copies developer info for the focused element (name, control type, ARIA role, framework, process, runtime id)
- [ ] Latency instrumentation: timestamps at hook, dispatcher, pipeline, queue and engine start; rolling 95th-percentile logged every minute

## Acceptance Criteria

- [ ] Unit tests: `RunAsync` timeout, watchdog thread replacement (with a fake blocking delegate), commands that bypass UIA still run while it is blocked
- [ ] A test UIA provider that sleeps for 30 s does not stop Alt+Tab focus speech in another window
- [ ] On a signed install, Task Manager (elevated) is readable
- [ ] Sign-in, lock and UAC screens are spoken
- [ ] Keypress-to-audio latency p95 ≤ 50 ms for cursor movement, ≤ 100 ms for focus changes on mid-range hardware

## Out of Scope

- Braille on secure screens (follows W6)
- Remote desktop / Citrix support (W8b)
