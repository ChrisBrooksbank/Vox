# Switching to Vox

## Overview

Make moving to Vox from NVDA, JAWS or Narrator cheap and safe, and give users reasons to stay that the others don't: their own settings and keys on day one, fixes for badly built pages kept per site, AI that quietly repairs unlabelled content, published speed figures, and the material an organisation needs to justify the move. Follows Milestone E of `COMMERCIAL_PARITY_PLAN.md` (Milestone F).

## User Stories

- As an NVDA or JAWS user, I want my voice, rate, verbosity, dictionaries and custom keys brought over so Vox feels like mine at once
- As a JAWS user, I want the keys I already know to work
- As a blind user, I want to name an unlabelled button once and hear that name every time I visit the site
- As a blind user, I want per-site settings (mode, verbosity, banners) without making a profile per browser
- As a blind user, I want unlabelled graphics and icon buttons described without asking, using my own computer by default
- As a blind user, I want to ask a question about a page and be taken to what the answer names
- As someone trying Vox, I want to get back to my old screen reader with one key, and never be left without speech if Vox fails
- As an IT manager, I want cost, deployment and support information to make the case for Vox

## Requirements

### Bring your settings

- [ ] `NvdaSettingsImporter`: reads `%APPDATA%\nvda\nvda.ini` (speech rate/pitch/volume, voice where a match exists, punctuation level, capitals, typing echo, verbosity-related report settings, braille display/table), `gestures.ini` (bindings for commands Vox has, written to the user `keymap.json`), and `speechDicts\default.dic` / voice dictionaries (to `dictionaries/user.json` and `voices/<voice>.json`; regex entries converted when Vox's rule supports them, else listed as skipped)
- [ ] `JawsSettingsImporter`: reads the newest JAWS version's user settings folder: `default.jkm` / app `.jkm` key assignments mapped to Vox commands where scripts have a counterpart, `.jdf` dictionary entries, and voice rate/punctuation from `default.jcf`
- [ ] An import report (what was brought over, what was skipped and why) shown as a reviewable document; nothing overwritten without saying so; the old Vox files kept as backups
- [ ] Offered in the first-run wizard when NVDA or JAWS settings are found, and from the Vox menu; never in secure mode

### JAWS keyboard layout

- [ ] `KeyboardLayout` gains `Jaws` (desktop and laptop variants): `assets/config/jaws-keymap.json` overlaid on the desktop bindings through `KeyMap.LoadLayout`, covering the common JAWS keys (virtual cursor toggle, links/headings/form fields lists, title, say all, read current line/word/character, top/bottom of page, window title, time) with conflicts resolved toward JAWS
- [ ] The keyboard reference, tutorial lessons and input help use the active layout's keys (already key-driven; tests prove it for the new layout)
- [ ] `docs/guide`: "Vox for JAWS users" and "Vox for NVDA users" pages mapping familiar keys and concepts to Vox's

### Custom labels and per-site settings

- [ ] Custom labels: a command labels the element at the browse cursor (or the focused element); stored per site (scheme + host, optionally path prefix) keyed by a stable element signature (automation id, else role + ancestor path + nearby text); applied in `VBufferBuilder` as the node's name and in focus announcements; edit/delete from a dialog; none in secure mode
- [ ] Per-site settings: browse/focus start mode, verbosity, screen layout, auto-press the overlay detector's reject/close button; keyed by site, layered above application profiles (`ProfileManager`), set from a dialog opened while browsing
- [ ] Label packs: export/import a site's labels as a file; publishable as an add-on package type through the add-on store (signed like other packages)

### AI that fixes pages

- [ ] Automatic labels: after a load, unlabelled graphics and icon-only buttons (no name, or a file-name-like name) are described in the background by the local describer only (never the cloud unless `AiCloudConsent` is "allow" and the site is not private), limited per page and per minute, cached by image hash and site; results applied as custom labels marked "described"; a setting turns it off; never in secure mode or with `DisableAI`
- [ ] Ask the page: a question prompt answered from the buffer text (local first, cloud with consent); an answer that names an element offers to move the browse cursor to it (element ids returned by the model checked against the buffer)

### Safe trial

- [ ] Trial mode: started from the wizard or `--trial`; detects a running NVDA/Narrator, pauses or stops it (NVDA through its controller client or by stopping the process; Narrator by its shortcut), and a key (and Vox menu item) quits Vox and restarts the previous screen reader
- [ ] Fallback: when Vox.Watchdog sees repeated crashes (its existing limit), it starts Narrator (or the screen reader recorded by trial mode) instead of leaving the user without speech, and says why on next start

### Proof of speed

- [ ] Latency and load-time measurement: E2E runs record keypress-to-speech latency (95th percentile) and time to first speech on large pages (the corpus plus a 10,000-row table page) for Vox; a documented manual method for measuring NVDA and JAWS the same way
- [ ] `docs/performance.md` publishes the figures with hardware, versions and method; CI tracks Vox's figures and fails on a regression beyond a set margin

### Organisations

- [ ] `docs/organisations.md`: cost comparison method (licences, SMAs, training), deployment checklist (MSI properties, Group Policy, profiles, add-on trust), conformance links (`vpat.md`, `security.md`), and how to get support
- [ ] Settings import available unattended for deployment (`Vox.App --import-settings nvda|jaws [--user-profile <path>]`), logging only what was imported

## Acceptance Criteria

- [ ] Unit tests: NVDA ini/gestures/dictionary and JAWS jkm/jdf parsing over sample files (malformed lines skipped, report lists them); JAWS layout resolves its keys and has no unintended conflicts; custom label signature matching survives buffer updates; per-site layering; automatic labels respect consent, limits and cache; trial-mode screen reader detection over fakes; watchdog fallback choice
- [ ] Manual: import a real NVDA and JAWS configuration; a JAWS user completes the web tutorial with the JAWS layout; label a button on a real site and revisit; trial mode returns to NVDA

## Out of Scope

- Running JAWS scripts or NVDA add-ons
- Importing JAWS script logic (only key assignments to commands Vox has)
