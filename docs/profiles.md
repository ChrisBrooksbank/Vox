# Configuration profiles

A profile is a named set of settings that replace the normal configuration's while it is active,
like NVDA's configuration profiles and JAWS's application settings.

## When a profile is active

Profiles are layered over the normal configuration in this order, a later one winning for each
setting it has:

1. the normal configuration (`settings.json`);
2. the foreground application's profile (the first whose `apps` has its process name);
3. the triggered profile: the one marked `sayAll`, while Say All is reading;
4. the profile activated by hand (Insert+Ctrl+P, Activate).

A profile only holds the settings it changes; everything else comes from the layers below it.

## Changing settings while a profile is active

Changes made while a profile is active (in the settings dialog, with the voice settings ring,
anywhere) go to the top active profile, not to the normal configuration. A few settings apply to
Vox as a whole and always go to the normal configuration: sleep mode's applications, start at
logon, the update channel, whether profile switches are announced, and the first-run flag.

When the application or a hand choice changes the top profile, Vox says "Profile Excel" or
"Normal configuration" (General page: *Say the configuration profile when it changes*). Say All
switching its profile on and off is never announced, so it doesn't talk over the reading.

## Files

Each profile is `%APPDATA%\Vox\profiles\<name>.json` (in a portable copy, the `userConfig`
folder beside Vox):

```json
{
  "name": "Excel",
  "apps": ["EXCEL"],
  "sayAll": false,
  "settings": { "SpeechRateWpm": 300, "PunctuationLevel": "All" }
}
```

A file that can't be read is logged and left out. On the secure screens there are no profiles.

## The dialog

Insert+Ctrl+P, or Configuration profiles in the Vox menu: the profiles (active ones marked), a name
box with New and Rename, Delete, Activate/Deactivate, *Use in* the application that was in the
foreground when the dialog opened, and *Use during Say All* (only one profile can be).
