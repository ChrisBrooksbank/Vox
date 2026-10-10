# Product: Settings, Help, Install & Languages

## Overview

Everything that turns Vox from a program into a product: an accessible settings dialog and input-gesture editor, the Vox menu and tray icon, help mode and a user guide, installer and updates (W8a, Milestone B), then configuration profiles, localization and enterprise deployment (W8b, Milestone D). Workstream W8 of `COMMERCIAL_PARITY_PLAN.md`.

## User Stories

- As a blind user, I want to change any setting in an accessible dialog and hear the effect at once
- As a blind user, I want to remap any command
- As a new user, I want a key that tells me what other keys do
- As a blind user, I want to install and update Vox myself, or carry it on a USB stick
- As an IT administrator, I want to deploy and configure Vox silently
- As a non-English speaker, I want Vox in my language

## Requirements

### W8a

- [ ] `SettingsDialog`: tabs for General, Speech, Braille, Keyboard, Mouse, Document formatting, Verbosity, Audio cues, Browse mode; every `VoxSettings` field; changes apply live; tested with Vox reading it
- [x] `VoxSettings` grows the fields other specs need; settings schema version and migration of older files
- [x] Input gestures dialog: commands grouped by category, add/remove key bindings, conflict detection, writes a user keymap layered over the default keymap
- [x] `KeyMap` loads user keymap overrides from `%APPDATA%/Vox/keymap.json`
- [ ] Vox menu (Insert+N) and system tray icon: Settings, Input gestures, Speech viewer, Help, Pause speech, Exit
- [x] Input help mode (Insert+1): keys speak their command name and description instead of running
- [ ] Command search: type part of a command name, hear its keys
- [x] Command descriptions stored with the commands (used by help mode, gestures dialog and the guide)
- [ ] User guide (HTML, with a keyboard reference generated from the keymap), opened from the Vox menu (Insert+F1 stays developer info, as in NVDA)
- [ ] Interactive tutorial extends the first-run wizard: practice lessons per topic, rerunnable
- [ ] Installer: signed MSI/MSIX installing to Program Files (needed for `uiAccess`), start menu and desktop shortcut with Ctrl+Alt+V, optional start at logon, installs `Vox.Service`
- [ ] Portable copy: create one on a USB stick from the Vox menu; settings stored beside the exe
- [ ] Update checker: checks a signed release feed (stable/beta), speaks "update available", downloads, verifies signature, installs on request

### W8b

- [ ] Configuration profiles: named sets of setting overrides; per-app activation (process name) and triggers (Say All); manual switching; merge order default < app < trigger < manual
- [ ] Localization: all spoken and UI strings in `.resx`; `IStringLocalizer` in `AnnouncementBuilder`, `ControlTypeNames`, wizard, dialogs; language setting; symbol dictionaries, braille tables and keyboard names per language
- [ ] RTL text order in the buffer and braille
- [ ] First translations: Spanish, French, German, Portuguese, Chinese, Japanese, Arabic, Hindi (translator workflow documented)
- [ ] Enterprise: silent install with MSI properties for key settings; ADMX templates for policy-controlled settings; roaming settings option; no network calls unless enabled; VPAT document
- [ ] Remote Desktop / Citrix: detect remote sessions and document limits; test against RDP with UIA

## Acceptance Criteria

- [ ] Unit tests for settings migration, user keymap layering and conflict detection, profile merge order and triggers, help-mode dispatch, update feed signature validation
- [ ] Every string in `AnnouncementBuilder` comes from resources (test: pseudo-locale produces no English)
- [ ] Manual: install, configure, update and uninstall Vox with speech only

## Out of Scope

- Add-on store (extensibility.md)
