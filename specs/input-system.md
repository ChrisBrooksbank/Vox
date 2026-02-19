# Input System

## Overview

Low-level keyboard hook with zero-allocation callback, configurable keymap, typing echo, and modifier key support.

## User Stories

- As a blind user, I want keyboard shortcuts (like NVDA conventions) so I can navigate without a mouse
- As a blind user, I want character and word echo while typing so I know what I've entered
- As a blind user, I want to choose Insert or CapsLock as my modifier key

## Requirements

- [ ] `IKeyboardHook` interface for abstracting keyboard hook
- [ ] `KeyboardHook` using P/Invoke `SetWindowsHookEx(WH_KEYBOARD_LL)` via Vanara
- [ ] Hook callback must be < 1ms: extract `vkCode`, post pre-allocated `KeyEvent` struct to bounded channel via `TryWrite`, return immediately
- [ ] Zero allocation, zero processing in callback
- [ ] `KeyEvent` as `readonly struct` (not class): `VkCode`, `Modifiers` (flags: Shift/Ctrl/Alt/Insert), `IsKeyDown`, `Timestamp`
- [ ] Channel consumer on separate thread handles modifier tracking, keymap lookup, command dispatch
- [ ] `KeyMap` loading from `assets/config/default-keymap.json`
- [ ] Maps `(Modifiers, VkCode, InteractionMode)` to `NavigationCommand`
- [ ] Default keymap matches NVDA conventions (Insert or CapsLock as modifier)
- [ ] `TypingEchoHandler`: character echo on key-up of printable chars, word echo on Space/Enter/punctuation
- [ ] Typing echo respects `TypingEchoMode` setting (None/Characters/Words/Both)
- [ ] Rolling buffer for word echo (speaks preceding word)

## Navigation Commands

`NextHeading`, `PrevHeading`, `NextLink`, `PrevLink`, `NextLandmark`, `PrevLandmark`, `HeadingLevel1`-`6`, `NextLine`, `PrevLine`, `NextWord`, `PrevWord`, `NextChar`, `PrevChar`, `ActivateElement`, `ToggleMode`, `SayAll`, `StopSpeech`, `ElementsList`, `ReadCurrentLine`, `ReadCurrentWord`

## Acceptance Criteria

- [ ] Keyboard hook installs and receives key events on Windows 11
- [ ] Hook callback completes in < 1ms (no blocking, no allocation)
- [ ] Keymap correctly resolves modifier+key combinations to commands
- [ ] Typing echo speaks characters and words according to settings
- [ ] Unit tests for KeyMap resolution and TypingEchoHandler

## Out of Scope

- Touch screen gestures (Phase 4)
- Braille display input routing keys (Phase 2)
- Scripting engine custom key bindings (Phase 4)
