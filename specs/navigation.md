# Navigation

## Overview

Browse/focus mode state machine, quick navigation (single-letter keys), context-aware announcements, Say All, and Elements List dialog.

## User Stories

- As a blind user, I want to press H to jump to the next heading so I can scan page structure quickly
- As a blind user, I want context-aware announcements ("heading level 2, visited link") so I understand what I'm on
- As a blind user, I want continuous reading (Say All) so I can listen to an entire article
- As a blind user, I want an Elements List (Insert+F7) so I can see all headings/links at once

## Requirements

- [ ] `NavigationManager` — Browse/Focus mode state machine
- [ ] Browse mode: single-letter nav consumed by Vox
- [ ] Focus mode: keys pass through except Insert+Space
- [ ] Auto-switch: Enter on edit field -> Focus mode + `focus_mode.wav`; focus leaves form field -> Browse mode + `browse_mode.wav`
- [ ] `QuickNavHandler` — Browse mode quick navigation:
  - H / Shift+H: next/prev heading (any level)
  - 1-6 / Shift+1-6: heading at specific level
  - K / Shift+K: next/prev link
  - D / Shift+D: next/prev landmark
  - F / Shift+F: next/prev form field
  - T / Shift+T: next/prev table
  - Tab / Shift+Tab: next/prev focusable element
- [ ] `AnnouncementBuilder` — Translates VBufferNode + VerbosityProfile into spoken text
- [ ] Concatenates: heading level, landmark type, name, control type, visited, required, expanded/collapsed
- [ ] Filtered by current verbosity profile (Beginner/Intermediate/Advanced)
- [ ] `SayAllController` — Insert+Down triggers continuous reading from current position
- [ ] Speaks one line at a time, advances cursor after each utterance
- [ ] Any keystroke cancels via CancellationTokenSource
- [ ] `ElementsListDialog` — Insert+F7 opens accessible WinForms dialog
- [ ] Filterable list of Headings/Links/Landmarks/FormFields (switchable tabs)
- [ ] Type to filter, Enter to jump
- [ ] Data from pre-built VBufferDocument indices (instant even on large pages)

## Acceptance Criteria

- [ ] Mode switching works correctly with audio cues
- [ ] Quick nav finds correct next/prev elements of each type
- [ ] Announcement builder produces correct text at all verbosity levels
- [ ] Say All reads continuously and cancels on any keystroke
- [ ] Elements List dialog is keyboard-accessible and filters correctly
- [ ] Unit tests for QuickNavHandler, AnnouncementBuilder verbosity profiles

## Out of Scope

- Object navigation / review cursor (Phase 2)
- Find in buffer / Ctrl+F (Phase 2)
- Table cell navigation (Phase 3)
