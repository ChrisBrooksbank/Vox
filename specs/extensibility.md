# Extensibility

## Overview

An add-on API so the community can cover the long tail of apps, plus an add-on store and remote access between two Vox machines. Workstream W10 of `COMMERCIAL_PARITY_PLAN.md` (Milestone E).

## User Stories

- As a developer, I want to add support for an app Vox doesn't know
- As a blind user, I want to install add-ons from inside Vox, safely
- As a trainer, I want to control a student's Vox remotely and hear their screen
- As a blind user, I want a broken or slow add-on never to silence Vox, and to put an add-on's commands on keys I choose
- As a developer, I want my add-on to keep working across minor Vox versions, read the objects around the focus, and have settings and translations like Vox's own
- As an IT administrator, I want to allow only add-ons from publishers we trust

## Requirements

- [x] `Vox.Sdk` assembly: versioned public interfaces for app modules (office-apps.md `IAppModule`), global plug-ins, commands with descriptions, announcement filters, speech engines, braille drivers
- [x] Add-on package format (zip with manifest: id, version, min/max API version, author, signature)
- [x] `AddOnManager`: install, enable, disable, remove; loads each add-on in its own collectible `AssemblyLoadContext`; incompatible API version refused with a spoken reason
- [x] Safe mode (start with add-ons disabled, from a `--safe` start flag or the Vox menu "Restart with add-ons disabled") and add-ons always off in secure mode
- [x] Add-on exceptions are caught and logged; an add-on that throws repeatedly is disabled with a notice
- [x] Add-on store client: browse, search, install, update from a signed catalogue
- [x] Remote access: encrypted relay or direct connection; controlling side sends keys, controlled side streams speech (and braille) back; clear session start/stop announcements on both sides

### Add-on architecture hardening

Robustness:

- [x] Every call into an add-on is timed: one over its budget (pipeline thread ~50 ms, announcement filters on the speech thread ~10 ms) is logged by add-on id and counts as a failure; one still running after ~2 s stops the add-on (its pending calls are skipped) and a watchdog turns it off with a notice, so a hung add-on never freezes the pipeline or speech
- [ ] Native libraries an add-on ships are checked against the manifest's SHA-256 like its assemblies, and loaded from a per-load temporary copy so the add-on's folder is never locked and can be updated; since native code can't be unloaded, an add-on with native files says its update takes effect after a restart
- [x] Unloading is verified: an add-on whose load context is still alive after unloading and a collection is logged ("did not unload")
- [x] `IAddOnHost.Stopping` (a `CancellationToken`) is cancelled before an add-on is disposed (disable, remove, update, turn-off, shutdown), so its background work stops

API compatibility:

- [ ] `Vox.Sdk`'s public API is tracked (`PublicApiAnalyzers`); an unintended change fails the build
- [ ] Interfaces add-ons implement grow only through default interface members; types added to later are records with `init` properties, never new positional parameters; `docs/addons.md` states the rules
- [ ] Whether an assembly comes from Vox or the add-on is decided from Vox's own assembly list (trusted platform assemblies and Vox.App's), not from what happens to be loaded; a shared assembly the add-on ships in a newer version than Vox's is logged

API surface (`VoxApi` 1.1):

- [ ] Read-only objects (`IVoxObject`: name, role, value, bounds, parent, children, next/previous), the focused and navigator objects from the host; read on the UIA thread with its timeouts; never a password's value
- [x] `CaretMovedEvent` carries the line, word and character at the caret (none for password fields)
- [ ] Output beyond speech: play an earcon, flash a braille message, show a text under the review cursor, run a Vox command by name
- [ ] Add-on settings: fields (toggle, number, choice) shown as the add-on's page in the settings dialog, stored per add-on, layered by profiles and lockable by policy
- [ ] Add-on strings: a string table in the package, read in Vox's language with English as fallback, for command names, descriptions and settings labels
- [x] `IAddOnHost.RequestReload()` reloads the add-on so it can change what it adds (commands after a settings change)

Integration:

- [ ] Add-on commands appear in the input gestures dialog (under "Add-ons", by add-on name) and can be rebound; the user's keys are kept in the user keymap
- [ ] An add-on key that overrides a Vox key is logged as a conflict and shown in the add-ons dialog
- [ ] The manifest's optional `order` sets the order of plug-ins and announcement filters (ties by id); the add-ons dialog shows it
- [ ] An add-on's Interrupt and High priority speech is rate-limited so it can't drown out Vox

Isolation:

- [ ] `docs/security.md` and `docs/enterprise.md` say add-ons run in Vox's process with its rights and are not bound by `NetworkGate`
- [ ] A Group Policy setting allows only signed or catalogue add-ons, enforced at install and at load
- [ ] A design for running add-on speech engines and braille drivers in a separate process (app modules stay in-process for speed) is in `docs/addons.md`

## Acceptance Criteria

- [x] Unit tests: manifest validation, version compatibility, load/unload, fault isolation (throwing add-on disabled), safe mode
- [x] Sample add-on in `samples/` built in CI
- [ ] Manual: install a sample add-on from a local catalogue; run a remote session between two PCs
- [ ] Unit tests: call budgets and the hang watchdog (injectable clock), native library hash check, unload verification with the sample add-on, `Stopping` cancelled on unload, assembly sharing decision, object access and caret text over fakes, settings layering, string fallback, gesture rebinding, key conflicts, ordering, reload, speech rate limit, trust policy
- [ ] An add-on with a native library can be updated while Vox runs (Windows)
- [ ] The sample add-on uses the 1.1 API (objects, settings, strings, `Stopping`)

## Out of Scope

- A scripting language other than .NET add-ons
