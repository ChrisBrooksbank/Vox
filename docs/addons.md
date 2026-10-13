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
pipeline thread, 10 ms for an announcement filter on the speech thread, 500 ms for `Initialize` —
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
its engine and display), disposes it, and unloads its context, without restarting Vox.

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

## Manual test

1. Build Vox and the sample; pack the sample.
2. Make a key, sign a local catalogue listing the package, put the public key in
   `%APPDATA%\Vox\addons\trusted-keys`, and set `AddOnCatalogueUrl` to the catalogue's `file:` path.
3. Vox menu, Add-on store: the sample is listed; Install. Insert+Ctrl+F12 says hello; in Notepad,
   Insert+Alt+F12 counts words; "e.g." is said "for example".
4. Manage add-ons: turn it off (the keys stop working), on, remove. Restart with add-ons disabled:
   Vox says "Safe mode" and the sample doesn't load.
