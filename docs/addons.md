# Add-ons

Add-ons let anyone teach Vox about an application it doesn't know, change how it speaks, or add a
speech engine or braille driver, without changing Vox itself. They are .NET assemblies built
against **`Vox.Sdk`**, the public add-on API, packaged as a signed `.voxaddon` file, and installed
from the Vox menu (Manage add-ons, or the Add-on store). The sample in
[`samples/Vox.SampleAddOn`](../samples/Vox.SampleAddOn) uses every part of the API.

## The API (`src/Vox.Sdk`)

`Vox.Sdk` is the only assembly an add-on references. It never refers to `Vox.Core`, so Vox's
internals can change without breaking add-ons. Its version is `VoxApi.Current` (major.minor):
a minor version only adds; a major version may change or remove.

The public API is tracked with `Microsoft.CodeAnalysis.PublicApiAnalyzers`: `src/Vox.Sdk/PublicAPI.Shipped.txt`
lists the released API and `PublicAPI.Unshipped.txt` what the next release adds, so an unintended
change fails the build (RS0016/RS0017/RS0050 are errors in CI). Within a major version, interfaces that
add-ons implement (`IAddOn`, `IAppModule`, `IGlobalPlugin`, …) grow only through default interface
members, and new record members are `init` properties, not positional parameters. When a release
ships, move the unshipped lines into the shipped file.

| Type | What it is |
|---|---|
| `IAddOn` | The entry point: one public class with a parameterless constructor. `Initialize(IAddOnHost)` is called once, on the pipeline thread; implement `IDisposable` to clean up when the add-on is turned off. |
| `IAddOnHost` | What Vox gives the add-on: `AddCommand`, `AddAppModule`, `AddGlobalPlugin`, `AddAnnouncementFilter`, `AddSpeechEngine`, `AddBrailleDriver` (only during `Initialize`), and `Speech`, `Logger`, `DataDirectory`, `ForegroundProcessName`, `RunOnPipeline`, `Stopping` (any time; `Stopping`, since API 1.1, is cancelled when the add-on is disabled, removed, updated, turned off or Vox closes, before it is disposed — pass it to background work). |
| `AddOnCommand` | A command with a name and description (said by input help and listed in the add-ons dialog) and `KeyGesture`s (`KeyGesture.Parse("VoxKey+Shift+J")`, with a `GestureMode` of Any, Browse or Focus). |
| `IAppModule` / `AppModule` | Support for an application, chosen by its process name. Its `Commands` have keys only while it is active; it can change or silence focus announcements (`FormatFocusAnnouncement`), drop events (`ShouldProcess`) and see every handled event (`OnEvent`). An add-on's module comes before Vox's own for the same application; turning it off in the Applications settings (`DisabledAppModules`, by its id) gives the application back to Vox's. |
| `IGlobalPlugin` / `GlobalPlugin` | Like an app module, for every application, after the active module. |
| `IAnnouncementFilter` | Changes text before it is spoken (after Vox's dictionaries, before symbols are named). Called on the speech thread for every utterance: be quick. |
| `ISpeechEngine` + `SpeechEngineRegistration` | A speech engine offered in the speech settings; started only when chosen. |
| `IBrailleDriver` | A braille display driver, tried after Vox's own when looking for a display. Vox's common braille key map applies to its keys. |
| `VoxEvent` | What add-ons see of Vox's events: `FocusEvent` (with `FocusInfo`), `ForegroundWindowEvent`, `NotificationEvent`, `LiveRegionEvent`, `CaretMovedEvent`, `TextChangedEvent`, and `OtherEvent` for the rest. |

What add-ons never get: the keys pressed and typed text (no key or typing-echo events), the value
of a password field (`FocusInfo.Value` is null), Vox's own commands (an add-on can't drop
them), and anything on the secure screens (add-ons never load there).

Every call Vox makes into an add-on is **guarded**: an exception is logged and that call skipped
(a filter that throws leaves the text as it was; a module that throws formatting an announcement
leaves Vox's). Every call is **timed** too: one that takes longer than its budget — 50 ms on the
pipeline thread, 10 ms for an announcement filter on the speech thread, 500 ms for `Initialize`,
1 second for a braille driver's `TryConnect` —
is logged and counts like an exception. An add-on that fails 5 times within a minute is **turned
off**: Vox unloads it, says "<name> was turned off because it kept failing", and remembers it
(`state.json`) until the user turns it on again (or installs a new version). A call still running
after 2 seconds means the add-on has **stopped responding**: a watchdog stops it at once (its
other calls are skipped and the result of that call is ignored) and it is turned off the same
way, with "<name> was turned off because it stopped responding", as soon as the call returns.
Don't block the thread you're called on: start slow work on a task and come back with
`host.RunOnPipeline`.

Key precedence: the active add-on module's keys, then every add-on's global commands, then Vox's
own app module and keymap. While input help is on, add-on keys are described like Vox's.

### Translating an add-on

Put flat JSON files of key to text in the package as `strings/<culture>.json` (`en.json`, `fr.json`,
`pt-BR.json`); they are listed in the manifest like any file. Write a command's name or description,
or a setting's label or a choice's text, as `"@key"`: Vox looks it up in its language (the full
culture, then the language), then in `en.json`, and shows the key itself when nothing has it
(`"@@text"` is the literal `@text`). `host.GetString("key")` does the same for your own text.

## Packages and the manifest

A package is a zip (`.voxaddon`) with `addon.json` at its root, every file it lists, and
optionally a signature:

```json
{
  "id": "com.example.notepadplusplus",
  "name": "Notepad++ support",
  "version": "1.2.0",
  "author": "Example Ltd",
  "description": "Reads the line number and selection in Notepad++.",
  "minApiVersion": "1.0",
  "maxApiVersion": "1.0",
  "entryAssembly": "Example.NotepadPlusPlus.dll",
  "url": "https://example.com/vox",
  "files": { "Example.NotepadPlusPlus.dll": "<sha-256, hex>" }
}
```

- `id`: 2–64 lower case letters, digits, `.`, `-`, `_`; the add-on's folder name.
- `version`: 2 to 4 numbers. `minApiVersion` / `maxApiVersion`: major.minor. The add-on works when
  Vox's API is at least `minApiVersion` and its major version is no newer than `maxApiVersion`'s
  (default: `minApiVersion`'s): built for 1.0, it runs on 1.0–1.x and is refused by 2.0 with a
  spoken reason ("… was made for an older Vox (add-on API 1) and does not work with this one (2.0)").
- `files`: every file of the package except `addon.json`, `addon.sig` and `addon.pem`, with its
  SHA-256. Paths use `/`, are relative, and may not contain `..`. Packing fills this in.
- `addon.sig`: base64 RSA-SHA256 signature of `addon.json`'s bytes; `addon.pem`: the signer's
  public key. Since the manifest lists every file's hash, the signature covers the whole add-on.

Checks before anything is installed: every listed file is there with its hash, nothing unlisted
is, no path leaves the add-on's folder, sizes are limited (200 MB, 2,000 files), and a signature,
if present, matches. Files are checked again as they are extracted, and each assembly's hash is
checked again every time it is loaded.

**Trust.** A package is *Catalogue* (its SHA-256 is the one in Vox's signed catalogue),
*TrustedPublisher* (signed by a key Vox trusts: `assets/config/addon-key.pem` beside Vox, or any
`*.pem` in `%APPDATA%\Vox\addons\trusted-keys`), *UnknownPublisher* (signed, by a key Vox doesn't
know: unchanged since signing, but by whom isn't known) or *Unsigned*. Installing from a file
always asks first and, for the last two, warns that add-ons run with the user's permissions.

### Building and packaging

Reference `Vox.Sdk` without copying it (it always comes from Vox):

```xml
<ProjectReference Include="path\to\src\Vox.Sdk\Vox.Sdk.csproj" Private="false" ExcludeAssets="runtime" />
```

Put `addon.json` (without `files`) in the output folder, then pack the folder, optionally signing:

```bash
dotnet build -c Release
Vox.App --pack-addon bin/Release/net9.0-windows10.0.19041.0 my-addon.voxaddon [private-key.pem]
openssl genrsa -out private-key.pem 3072      # a signing key, once
```

CI builds the sample and keeps `vox.sample.voxaddon` as a build artifact.

### Label packs

A label pack is an add-on with no code: custom labels for web sites ([custom labels](switching.md#custom-labels)).
Its `addon.json` has `"type": "labels"` and no `entryAssembly`, and the package holds
`labels.json` in the labels file's format:

```json
{ "sites": { "https://mail.example.com": [
  { "signature": "id:send-button", "label": "Send" },
  { "signature": "Button|toolbar>Document|compose", "label": "Attach file", "pathPrefix": "/mail" }
] } }
```

Make one by exporting a site's labels from the custom labels dialog (Vox menu, Custom labels,
Export site's labels: a `.voxlabels` file, which can also be imported there as it is), renaming it
`labels.json` beside the manifest, and packing the folder with `--pack-addon` as above. It is
signed, checked, listed in the catalogue (with `"type": "labels"`), installed, updated, disabled
and removed like any add-on. While it is enabled its labels apply on their sites under the user's
own (a label of the user's for the same element wins); disabling or removing it takes them back.
`labels.json` is checked against the manifest's hash each time it is read.

## Loading

Add-ons live in `%APPDATA%\Vox\addons\<id>\` (with `state.json` beside them: which are disabled
or were turned off, and why; and `.data\<id>\`, each add-on's `DataDirectory`, kept across updates
and removal). `AddOnManager` loads each enabled, compatible add-on in its own **collectible
`AssemblyLoadContext`**: assemblies are read into memory after their hash is checked, so files are
never locked and an update can replace them while the old version runs; `Vox.Sdk` and
`Microsoft.Extensions.Logging.Abstractions` always resolve to Vox's own copies. Disabling, removing
or updating an add-on takes everything it added back out of Vox (keys, modules, plug-ins, filters,
its engine and display), cancels its `Stopping` token, disposes it, and unloads its context,
without restarting Vox. Two seconds later Vox checks the context was collected and logs "add-on
<id> did not unload" when something still refers to the add-on's code (a thread it started, a
timer it never stopped, an event it subscribed to and never left): it then stays in memory,
doing nothing, until Vox restarts. Stop your work when `Stopping` is cancelled to avoid that.

**Safe mode.** `Vox.App --safe` (or the Vox menu's "Restart with add-ons disabled") starts Vox
with no add-on loaded; they can still be turned off or removed, to get rid of the one causing
trouble. In safe mode the same menu entry reads "Restart with add-ons enabled". Under
Vox.Watchdog the restart goes through exit codes 3 (safe) and 4 (normal), which never count as
crashes; without the watchdog Vox starts the next instance itself (`--restarted`: wait for the
old one to close).

**Never on the secure screens**, and not when the organization's `DisableAddOns` policy is set
([Deploying Vox in an organization](enterprise.md)): no add-on is listed, loaded or installed there.

## The add-on store

"Add-on store" in the Vox menu lists Vox's catalogue: search by name, description or author; each
add-on says whether it is installed, has an update, or doesn't work with this Vox; Install,
Update, and Update all. The catalogue is `catalogue.json` with `catalogue.json.sig` beside it
(base64 RSA-SHA256 of its bytes by a trusted key), at the `AddOnCatalogueUrl` setting (default
`https://raw.githubusercontent.com/ChrisBrooksbank/Vox/master/addons/catalogue.json`):

```json
{ "addons": [ {
  "id": "vox.sample", "name": "Vox sample add-on", "version": "1.0.0", "author": "The Vox project",
  "minApiVersion": "1.1", "maxApiVersion": "1.1", "description": "…",
  "url": "packages/vox.sample-1.0.0.voxaddon", "sha256": "<sha-256 of the package>"
} ] }
```

`url` may be relative to the catalogue. Remote catalogues and packages must be HTTPS and go
through `NetworkGate` (nothing with the network off); a catalogue on this computer (`file:`) may
list packages beside it and works offline, which is how an organization can run its own:

```bash
Vox.App --sign-catalogue catalogue.json private-key.pem    # writes catalogue.json.sig
```

and add the matching public key to `trusted-keys` (or ship it as `addon-key.pem`). A download is
installed only when its SHA-256 is the catalogue's. Without any trusted key there is no store
(the dialog says so).

## Design: hosting engines and drivers out of process

*A design only; nothing here is built.* Today every add-on runs in Vox's process with its rights
(see [Security](security.md)). A speech engine or braille driver is the add-on most likely to wrap
native code (a vendor's DLL, a serial or USB stack) that can crash, hang or leak, so these two kinds
could run in a separate **add-on host** process, while app modules, plug-ins, filters and commands stay
in-process: they sit on the keypress-to-speech path, where a hop between processes costs more than it
earns.

**Opting in.** The manifest gets `"host": "process"` (default `"inprocess"`; only `ISpeechEngine`
and `IBrailleDriver` registrations are allowed in a process-hosted add-on — one that also registers
a module, plug-in or command is refused with a spoken reason). An organization can force it with a
policy (`AddOnHostMode`), and a package that needs full trust says so (`"host": "inprocess"` and the
user is told when installing).

**The host.** `Vox.AddOnHost.exe` (a new small project referencing `Vox.Sdk` only) is started by
`AddOnManager` once per add-on with the add-on's folder, id and a pipe name. It loads the entry
assembly exactly as `AssemblyAddOnLoader` does (hash-checked, in memory, collectible context), runs
`IAddOn.Initialize` with a host whose `RegisterSpeechEngine`/`RegisterBrailleDriver` calls are
answered over the channel, and exits when the pipe closes or Vox stops it. It runs with a restricted
token (low integrity, no UI access, no elevation), as a job object member with a memory limit and
kill-on-close, so it can never outlive Vox. Everything else on `IAddOnHost` (logging, `DataDirectory`,
settings, `Stopping`) is served over the same channel; calls that make no sense there (commands, events)
throw `NotSupportedException` with a message naming the manifest key.

**The channel.** A named pipe with a random name and an ACL for the current user only, carrying
length-prefixed JSON messages (request id, method, arguments), the same shape as `RemoteMessage`, in
both directions. Vox holds a proxy implementing `ISpeechEngine` or `IBrailleDriver` that turns each
call into a message:

- Speech: `SpeakAsync` sends `speak` (text, options, utterance id) and completes on the host's
  `done`/`error` for that id; `Cancel` sends `cancel` at once and never waits (the speech queue
  needs it immediately); `SetRate/Pitch/Volume/Voice` are fire-and-forget and re-sent after a host
  restart; `GetVoices` is cached from the host's reply at start-up and on `voices-changed`;
  `IsSpeaking` is the last state the host sent. Audio is played by the host itself on the default
  output device; a later revision may stream PCM back for `AudioCuePlayer`, so the earcon output
  device is honoured (the v1 limitation is documented in the settings page).
- Braille: `Write` sends `cells` (latest wins: a pending write is replaced, so a slow display can
  never queue up a backlog); `TryConnect` is a request with a one-second timeout; `KeysPressed`,
  `Disconnected` and `CellCount` changes are events from the host.

**Failure.** Every request has a timeout (speech `speak` is bounded by the utterance's length plus
a margin; others one second). A timeout, a broken pipe or an exit of the host counts as a failure in
the add-on's `AddOnGuard` (five in a minute turns it off and says so, as now). One failure is
survivable: the proxy raises the same error a failing in-process engine would, so
`SpeechEngineRegistry` falls back to the next engine, and `AddOnManager` restarts the host (backing
off 1, 2, 4 s) and re-applies rate, pitch, volume and voice. A hung host is killed, not waited for.
Because the host is a separate process, a native crash no longer takes Vox down, and the watchdog
sees nothing.

**Cost and limits.** Expect a round trip of well under a millisecond on the pipe; the added latency
to first speech is dominated by the engine, not the hop. A process per add-on costs memory (a few
tens of MB with the runtime), which is why only these two kinds use it and why the host is started
lazily: a speech engine when it is chosen, a braille driver at the first rescan. Never on secure
screens (as for all add-ons). The host is *not* a security boundary against a malicious add-on of the
user's own choosing — it still runs as the user and can use the network — but the restricted token,
job object and the absence of UI access limit what a buggy or compromised one can do, and the
network is still the add-on's own to use, so `docs/security.md` keeps saying that add-ons bypass
`NetworkGate`.

**Testing.** The proxies are tested against a fake channel (timeouts, restarts, latest-write-wins,
cancel never blocking); a sample out-of-process engine in `samples/` is built in CI and loaded by an
integration test that kills the host mid-utterance and expects fallback and restart. `SdkTests`
checks every `ISpeechEngine`/`IBrailleDriver` member has a message, so a new member can't be
forgotten.

## Manual test

1. Build Vox and the sample; pack the sample.
2. Make a key, sign a local catalogue listing the package, put the public key in
   `%APPDATA%\Vox\addons\trusted-keys`, and set `AddOnCatalogueUrl` to the catalogue's `file:` path.
3. Vox menu, Add-on store: the sample is listed; Install. Insert+Ctrl+F12 says hello; in Notepad,
   Insert+Alt+F12 counts words; "e.g." is said "for example".
4. Manage add-ons: turn it off (the keys stop working), on, remove. Restart with add-ons disabled:
   Vox says "Safe mode" and the sample doesn't load.
