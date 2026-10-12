# Deploying Vox in an organization

## Silent install

```
msiexec /i Vox.msi /qn STARTATLOGON=1 UPDATECHANNEL=Off KEYBOARDLAYOUT=Laptop
```

The MSI installs per machine to `Program Files\Vox` (needed for UI access, [Signing Vox and UI access](signing.md)).

| Property | Default | Meaning |
| --- | --- | --- |
| `STARTATLOGON` | 0 | 1: Vox starts when anyone signs in (all-users Startup shortcut) |
| `DESKTOPSHORTCUT` | 1 | 0: no desktop shortcut |
| `INSTALLSERVICE` | 1 | 0: no secure-screen service (Vox won't read sign-in, lock and UAC screens) |
| `UPDATECHANNEL` | | `Stable`, `Beta` or `Off` |
| `KEYBOARDLAYOUT` | | `Desktop` or `Laptop` |
| `MODIFIERKEY` | | `Insert` or `CapsLock` |
| `VERBOSITY` | | `Beginner`, `Intermediate` or `Advanced` |
| `SPEECHRATE` | | words per minute |
| `LANGUAGE` | | Vox's language: `en`, `es`, `fr`, `de`, `pt`, `zh`, `ja`, `ar`, `hi` (none: Windows' language) |

The settings properties become the computer's **default settings**, written to
`HKLM\Software\Vox\Defaults`. Users start with them and may change them.

## How settings are layered

From lowest to highest, a later layer winning for each setting it has:

1. Vox's built-in defaults (`assets/config/default-settings.json`);
2. the computer's defaults: `%ProgramData%\Vox\machine-settings.json` (any settings, same format
   as a user's `settings.json`), then `HKLM\Software\Vox\Defaults` (the installer's properties);
3. the user's settings (`settings.json`) and their configuration profiles ([Configuration profiles](profiles.md));
4. **Group Policy**, which always wins and can't be changed by the user.

Values that can't be used (an unknown setting, a bad value) are logged and left out.

## Group Policy

Copy `installer/policy/Vox.admx` to `%SystemRoot%\PolicyDefinitions` (or the domain's Central
Store) and `installer/policy/en-US/Vox.adml` to its `en-US` folder. The policies are under
*Vox screen reader*, for both the computer and the user (the computer's wins). They are stored in
`Software\Policies\Vox` and read when Vox starts.

| Policy | Value | Effect |
| --- | --- | --- |
| Turn off all network access | `DisableNetwork` = 1 | No network connection of any kind (see below); no update checks |
| Turn off update checks | `DisableUpdates` = 1 | Update channel forced to Off |
| Update channel | `UpdateChannel` = Stable/Beta/Off | Forced; users can't change it |
| Turn off optional components and add-ons | `DisableAddOns` = 1 | eSpeak NG, MathCAT and liblouis aren't loaded; no add-on is loaded, installed or offered by the store ([Add-ons](addons.md)) |
| Turn off AI features | `DisableAI` = 1 | No image descriptions or explanations |
| Start Vox at sign-in | `StartAtLogon` = 1/0 | Forced on or off |
| Lock all settings | `LockSettings` = 1 | No setting can be changed; the settings dialog shows them as set by the organization |
| Where user settings are kept | `SettingsLocation` = Roaming/Local | See below |

Any other setting can be enforced by a value named after it under
`Software\Policies\Vox\Settings` (REG_SZ, or REG_DWORD for numbers and on/off), e.g.
`PunctuationLevel` = `All`. An organization can run its own add-on catalogue this way:
`AddOnCatalogueUrl` = the address (or file path) of its signed `catalogue.json`, with its public
key deployed as `assets\config\addon-key.pem` beside Vox ([Add-ons](addons.md)). An enforced setting can't be changed in the settings dialog, by a
configuration profile, or by editing `settings.json`.

## Bringing over NVDA and JAWS settings

`Vox.App --import-settings nvda|jaws [--user-profile <path>]` brings a user's NVDA or JAWS speech
settings, keys and dictionaries over into Vox without asking (for example in a sign-in script, or
for each profile folder with `--user-profile`), keeping a copy of each Vox file it replaces and
printing only what was imported. See [Bringing your settings from NVDA or JAWS](switching.md).

## Roaming and local data

User settings, keys, dictionaries and profiles are kept in `%APPDATA%\Vox`, which follows users
with a roaming profile. Logs and the optional components are kept in `%LOCALAPPDATA%\Vox`, on
each computer. The `SettingsLocation` policy set to Local keeps settings in `%LOCALAPPDATA%\Vox`
too (for shared or kiosk computers). A portable copy keeps everything beside its executable.

## No network unless allowed

Vox only uses the network to check for updates, for the add-on store, for remote access
([Remote access](remote.md)) and for cloud AI the user set up (and to talk to BRLTTY or a local AI model on
the same computer). Every connection goes through one gate (`NetworkGate`): with `DisableNetwork`
set, or on the secure screens, it refuses every connection that leaves the computer, the update
checker never runs, the store only reads a catalogue on the computer itself, and remote access
neither connects nor listens. A unit test checks that every network user in Vox goes through the gate.

## Remote sessions

See [Remote Desktop and Citrix](remote-sessions.md). Install Vox on the session host and start it inside the session.
