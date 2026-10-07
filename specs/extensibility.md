# Extensibility

## Overview

An add-on API so the community can cover the long tail of apps, plus an add-on store and remote access between two Vox machines. Workstream W10 of `COMMERCIAL_PARITY_PLAN.md` (Milestone E).

## User Stories

- As a developer, I want to add support for an app Vox doesn't know
- As a blind user, I want to install add-ons from inside Vox, safely
- As a trainer, I want to control a student's Vox remotely and hear their screen

## Requirements

- [ ] `Vox.Sdk` assembly: versioned public interfaces for app modules (office-apps.md `IAppModule`), global plug-ins, commands with descriptions, announcement filters, speech engines, braille drivers
- [ ] Add-on package format (zip with manifest: id, version, min/max API version, author, signature)
- [ ] `AddOnManager`: install, enable, disable, remove; loads each add-on in its own collectible `AssemblyLoadContext`; incompatible API version refused with a spoken reason
- [ ] Safe mode (start with add-ons disabled, from a `--safe` start flag or the Vox menu "Restart with add-ons disabled") and add-ons always off in secure mode
- [ ] Add-on exceptions are caught and logged; an add-on that throws repeatedly is disabled with a notice
- [ ] Add-on store client: browse, search, install, update from a signed catalogue
- [ ] Remote access: encrypted relay or direct connection; controlling side sends keys, controlled side streams speech (and braille) back; clear session start/stop announcements on both sides

## Acceptance Criteria

- [ ] Unit tests: manifest validation, version compatibility, load/unload, fault isolation (throwing add-on disabled), safe mode
- [ ] Sample add-on in `samples/` built in CI
- [ ] Manual: install a sample add-on from a local catalogue; run a remote session between two PCs

## Out of Scope

- A scripting language other than .NET add-ons
