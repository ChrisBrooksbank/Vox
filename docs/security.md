# Security review

A screen reader sees everything on the screen and every key the user presses, can type into any
window, and in a signed install reads elevated windows and the sign-in screen. Bugs in it do more
harm than bugs in most apps. This page is the checklist a release, and every change to the areas
below, is reviewed against. It lists what Vox relies on, with the code that does it, and the known
weaknesses not yet fixed.

## Reporting a problem

Report security problems privately through the repository's
[security advisories](https://github.com/ChrisBrooksbank/Vox/security/advisories/new), not as a
public issue. Include the Vox version, what an attacker needs (another program running as the same
user? physical access to the lock screen? a web page?), and what they get.

## How to use this checklist

- **Every pull request** that touches one of the areas below: the author ticks the items for that
  area in the PR description, or says why one no longer applies and updates this page.
- **Every release**: a maintainer walks the whole list against the release's changes, and re-reads
  [Known weaknesses](#known-weaknesses) to see whether one has been fixed or has got worse.
- An item that can be enforced by a test should be: several below already are, and a new one is
  better than a new line here.

## What Vox protects

| What | From whom |
| --- | --- |
| What the user types, field values, passwords | logs, add-ons (passwords), the cloud, other computers |
| Elevated windows | ordinary programs using Vox's UI access to reach them |
| The sign-in, lock and UAC screens | the person at the keyboard, and the signed-in user |
| What Vox installs and runs | anyone who can change the update feed or an add-on on its way |
| The network | Vox itself, when policy says the network is off |

Out of scope: a signed-in user attacking their own account (they can run a modified copy of Vox),
and administrators (they control the install).

## Checklist

### Keyboard hook and input

- [ ] The low-level hook callback only decides and posts; it never blocks, allocates heavily, calls
      UIA or waits on a lock another thread can hold (`Input/KeyboardHook.cs`; budget under 1 ms).
      Windows removes a slow hook without telling anyone.
- [ ] Keys Vox injects itself (`SendInput`, marked with Vox's extra-info value) are let through, so
      Vox can't loop on its own output.
- [ ] If the suppression filter throws, the key passes: a bug must never eat the user's keyboard.
- [ ] On a crash or exit the hook is removed first (`HookSafetyNet`, `Program.cs`).
- [ ] No key is swallowed that the user can't get back: the screen reader key passes when tapped
      twice, sleep mode passes everything but its toggle, remote control passes the toggle.
- [ ] Only keys bound to commands are logged, and only at Debug (`KeyInputDispatcher`). Typed
      characters and words never reach the log (`TypingEchoHandler`; `SensitiveTextLoggingTests`).
- [ ] In a password field typing echo says "star" and never builds a word.
- [ ] Anything new that takes over the keyboard (`CaptureNextKey`, input help, `KeyForwarder`) has a
      way out the user can press, and ends when its dialog or session ends.

### UI access and the installed files

- [ ] Only builds signed with the release certificate embed `app.uiaccess.manifest`; development
      builds use `app.manifest` (`uiAccess="false"`) (`docs/signing.md`).
- [ ] The installer puts Vox under Program Files, per machine, and sets no write permission for users
      on it (`installer/Vox.Installer/Package.wxs`).
- [ ] Every process Vox starts is started with a full path or an `ArgumentList` (no command line
      built from strings a page, setting or add-on controls): the user guide, `msiexec`, the restart,
      the secure-screen instance.
- [ ] Vox with UI access loads code only from its install folder — see weakness 1 for where it
      doesn't yet.
- [ ] Signing passwords never reach a build log (`Vox.App.csproj` passes `/p` to signtool: use the
      store thumbprint form in CI, or mask the value).

### Secure screens (`--secure`, `Vox.Service`)

- [ ] `RunPolicy.IsSecure` turns off: settings writes, user profile files (keymap, dictionaries,
      profiles, AI key), add-ons and optional components (eSpeak NG, MathCAT, liblouis), the network
      (updates, add-on store, remote access, AI), screen capture (OCR), braille, the setup wizard and
      the user guide. Each new feature says which of these it is, and checks the policy itself.
- [ ] Nothing reachable on the secure desktop opens a file dialog, browser, shell or help window:
      any of them would give the person at the keyboard a SYSTEM process on the sign-in screen. Check
      every new dialog and Vox menu entry.
- [ ] The secure instance reads only `%ProgramData%\Vox\secure-settings.json` and HKLM policy, and
      writes only `%ProgramData%\Vox\secure-logs`.
- [ ] The secure instance runs only while the input desktop is Winlogon (`SecureDesktopActivation`).
- [ ] Recognised, typed and spoken text from the secure screens is never logged.

### Update channel (`Updates/`, `docs/updates.md`)

- [ ] The feed is trusted only when its RSA-SHA256 signature verifies against
      `assets/config/update-key.pem`; with no key file Vox never checks.
- [ ] The release build includes `update-key.pem` (copied by `Vox.App.csproj`) and the published
      folder that becomes the MSI has it (see weakness 6).
- [ ] Installer addresses in the feed are HTTPS; the downloaded MSI's SHA-256 must be the feed's, or
      it is deleted.
- [ ] Only a newer version is offered (no downgrade), and installing always asks first.
- [ ] Policy (`DisableUpdates`, `DisableNetwork`, `UpdateChannel`) and secure mode turn checks off.
- [ ] The release key (`release-key.pem`) is kept offline, never in the repository or CI logs.

### Add-ons (`AddOns/`, `docs/addons.md`)

- [ ] A package is installed only if every file's SHA-256 matches its manifest, no file is unlisted
      or listed twice, and no path leaves the add-on's folder (`AddOnPackage`; extraction re-checks
      the resolved path and re-hashes what it wrote).
- [ ] A present but invalid signature is refused; an unsigned or unknown-publisher package is
      installed only after a trust warning the user confirms.
- [ ] The store installs only a package whose SHA-256 is in the signed catalogue, from HTTPS.
- [ ] Add-ons never get keys, typed text from a password field, or a password's value
      (`AddOnAdapters`: `FocusInfo.Value` is null for passwords; `VoxEvent`s carry no keys).
- [ ] `--safe`, the Vox menu's restart and the `DisableAddOns` policy load no add-on; secure mode
      never does.
- [ ] Nobody, in the docs or the UI, describes the per-add-on `AssemblyLoadContext` or `AddOnGuard`
      as isolation: an add-on is full-trust code in Vox's process, with Vox's UI access.

### Network, remote access and AI

- [ ] Every connection goes through `NetworkGate` (`NetworkGateTests.EveryNetworkUser_IsKnownAndGated`).
- [ ] Remote access: codes come from `RandomNumberGenerator` (60 bits); the channel is ECDH P-256 with
      the code mixed in, AES-GCM with a counter per direction, and a frame cap (`SecureChannel`);
      wrong codes are delayed and limited; both computers say when control starts and ends.
- [ ] AI: the key is stored with DPAPI and never logged; consent rules refuse password fields,
      `AiPrivateApps`, secure screens, `DisableAI` and the network being off (`AiConsent`); the local
      describer talks to loopback addresses only.

### Logs and privacy

- [ ] Never logged: typed text, field values, utterance text (length and priority only), recognised
      text, page and notification text, the AI key, the remote code. `SensitiveTextLoggingTests`
      covers typing echo, the speech queue and property changes; extend it when adding a log line
      that is near user text.
- [ ] Log arguments are counts, types, ids and process names, never `ToString()` of a buffer node,
      element or event (they hold names, which are page text).
- [ ] No telemetry. Nothing is sent anywhere without the user's action (`specs/quality-engineering.md`).

## Known weaknesses

Found in the review that wrote this page (October 2026). Each is a task to fix, or a decision to
record here with its reason.

1. **UI access can be borrowed through user-writable code.** A signed Vox has UI access and loads
   add-on assemblies and native libraries from `%APPDATA%\Vox\addons`, and eSpeak NG and MathCAT from
   `%LOCALAPPDATA%\Vox\components`. At load time the add-on's signature isn't re-checked
   (`AddOnManager.LoadFolder`); managed assemblies are hashed against the `addon.json` beside them,
   which is just as writable, and native libraries aren't hashed. Any program running as the user
   can therefore get code into a process that types into elevated windows. Fix: with UI access,
   load only signed add-ons whose signature is checked at load, and components from a
   per-machine folder only administrators can write.
2. **`%ProgramData%\Vox` has default permissions.** Users can create files there, so any user can
   write the settings the SYSTEM instance reads on every user's lock screen, and the user who
   created the folder owns it (a junction for `secure-logs` would make SYSTEM write and rotate files
   elsewhere). Fix: create the folder in the installer, writable only by administrators and SYSTEM,
   and copy settings to the secure screens through the service.
3. **The secure instance runs with SYSTEM's full token** (`Win32SecureInstanceHost`). Fix: a
   restricted token without privileges it doesn't need.
4. **The downloaded MSI isn't held until `msiexec` has read it.** Vox exits right after starting it,
   which releases its lock while the UAC prompt is up, and the MSI's Authenticode signature isn't
   checked. Fix: check the signature, and download to a folder only the installing process can
   change, or keep a handle open until `msiexec` has started reading.
5. **A dropped keyboard hook isn't noticed.** If Windows removes the hook (a callback over
   `LowLevelHooksTimeout`), nothing reinstalls it, and Vox stops hearing keys until restarted.
6. **The published folder may lack Vox's assets.** `Vox.App.csproj` copies `assets/config` and the
   sounds into the build output only (`AfterTargets="Build"`), and the release workflow builds the
   MSI from `dotnet publish` output. Check the MSI contains `assets\config` (keymaps fall back to
   embedded copies, but the update and add-on keys don't) before the first release.
7. **A custom `AiEndpoint` may be plain HTTP**, which would send the API key and screenshots in the
   clear (`CloudImageDescriber`). Fix: require HTTPS except for loopback.
8. **A `file:` add-on catalogue may be a UNC path** (`\\server\share`), which reaches the network
   without `NetworkGate`. HTTP redirects are also followed below the gate. Fix: refuse UNC paths
   when the network is off, and turn redirects off in the gated client or check each hop.
9. **Add-ons see what Vox says.** Announcement filters and add-on speech engines receive every
   utterance, including typing echo outside password fields and live-region text. That they must not
   log or send it is a rule in the SDK docs only. Users should be told this when installing an
   unsigned add-on.
10. **The remote-access relay has no limits per address**: 1,000 waiting channels can be filled by
    one client. It only ever sees ciphertext and a hash of the code. The code is also copied to the
    clipboard, where clipboard history may keep it.
11. **The secure instance's single-instance mutex is in the session namespace**
    (`Local\Vox.ScreenReader.Secure`), so a program in the session could create it first and stop
    Vox starting on the sign-in screen.

Fixed in the same review: a failed browse-mode activation logged the node, including its page text,
at Warning; notification text was logged at Debug. Both now log only the control type or length.
