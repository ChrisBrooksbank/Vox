# Translating Vox

Everything Vox says or shows in its own words comes from string resources, so translating Vox
needs no code: only resource and data files.

## What to translate

| What | File | Notes |
| --- | --- | --- |
| Spoken and UI strings | `src/Vox.Core/Localization/Strings.<language>.resx` | The English is `Strings.resx`. A string you leave out is said in English. |
| Symbol names | `assets/speech/symbols-<language>.json` | Names only: the punctuation levels stay English's. Add symbols only your language uses (with a `level`). |
| Braille | `VoxLanguages.BrailleTableFor` and `BrailleTables.All` (code) | Which liblouis table a language starts with; table names are `BrailleTable.<file>` strings. |
| Key names | `Key.*` strings | How input help and the keyboard reference name keys. |
| The user guide | `docs/guide/` | Not translated yet. |

`<language>` is a two-letter code (`fr`), or a culture (`pt-BR`) for a regional variant; .NET falls
back from `pt-BR` to `pt` to English.

## Strings

Each string has a name (`ControlType.Button`, `State.Checked`, `Command.NextHeading.Name`) and a
comment in `Strings.resx` where its use isn't obvious. Keep:

- placeholders: `{0}`, `{1}` (a test checks every translation has the same ones);
- `&` before the access-key letter in labels (`&Language`), choosing letters that are unique on
  the same page or menu;
- HTML in strings whose comment says so (the keyboard reference's introduction).

Prefer words a screen reader user in your language is used to (NVDA's and JAWS's translations
are good references), and short ones: they are heard thousands of times a day.

## Checking

1. `dotnet test --filter FullyQualifiedName~Localization` checks that every name used in the code
   exists in English, that translations only have English names with the same placeholders, and
   the pseudo-locale test: with every string pseudo-localized, `AnnouncementBuilder` produces no
   English, which proves nothing is hard-coded.
2. Run Vox with the language set (Settings, General, Language; or `"Language": "fr"` in
   `settings.json`) and go through a page, the settings dialog and the Vox menu.
3. Arabic and Hebrew: Vox's dialogs are laid out right to left; text stays in reading order in
   speech and braille ([Braille](braille.md)), with invisible direction marks left out.

## The first translations

Spanish, French, German, Portuguese, Chinese, Japanese, Arabic and Hindi have their control types,
states, landmarks, announcement patterns, command categories, settings page and menu names, and
symbol names. **They were machine-made and must be reviewed by native speakers** before a
release; the wizard, lessons, command names and settings labels are still English in them.
