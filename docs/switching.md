# Bringing your settings from NVDA or JAWS

Vox can read another screen reader's settings and bring over what it has a counterpart for:

- the first time Vox starts, the welcome wizard offers it when NVDA or JAWS settings are found
  (1 brings them over, 2 JAWS's when both are there, Enter sets up Vox without them); the full
  report opens when the wizard ends;
- from the Vox menu (Insert+N), Import settings from NVDA or JAWS shows the report first, with
  the screen reader to import from when both are found; nothing changes until you choose Import;
- unattended, for deployment: `Vox.App --import-settings nvda|jaws [--user-profile <path>]`
  imports the current user's settings (or those of the user whose profile folder is given, such
  as `C:\Users\sam`, into that profile's `AppData\Roaming\Vox`), prints only what was imported,
  and exits with 1 when there was nothing to import.

Never on the sign-in, lock and UAC screens. Each import has a report: what was brought over (with the value it replaced in Vox), and what was
skipped and why. Nothing of Vox's is replaced without a copy: a file the import changes is first
copied beside itself as `<name>.<date>-<time>.bak` (for example `keymap.json.20261012-202530.bak`).

## NVDA

Read from NVDA's settings folder (`%APPDATA%\nvda` for an installed NVDA):

| NVDA | Vox |
| --- | --- |
| `nvda.ini`: synthesizer, voice, rate, pitch, volume, punctuation (symbol level), capitals, typing echo, keyboard layout, NVDA key, browse-mode line length and screen layout, braille display, table and options | the matching settings |
| `gestures.ini`: keys you assigned to NVDA commands | `keymap.json` in Vox's user folder, for the commands Vox has |
| `speechDicts\default.dic` | `dictionaries\user.json` |
| `speechDicts\voiceDicts.v1\<synthesizer>\*.dic` | `dictionaries\voices\<voice>.json`, for voices installed for Vox |

Keys are added beside Vox's own keys for the command, in the same modes (a command that works
everywhere gets a key that works everywhere). A key that did something else in Vox now does what
it did in NVDA; the report names what it did before. Not brought over: keys for another keyboard
layout than the one Vox uses, keys with the Windows key, braille display and touch gestures, keys
you removed from NVDA, and keys of NVDA's application support and add-ons.

Dictionary entries keep their type (anywhere, whole word, regular expression) and case. NVDA's
regular expressions are Python's: those .NET reads the same way are converted (named groups, and
`\1` or `\g<name>` in the replacement); the others are listed as skipped. Vox's own entries are
kept; an imported entry with the same pattern replaces Vox's.

## JAWS

Read from the newest JAWS version's user settings folder
(`%APPDATA%\Freedom Scientific\JAWS\<version>\Settings\enu`, or the first language there):

| JAWS | Vox |
| --- | --- |
| `default.jcf`: `Rate` (percent, approximately), `Punctuation` (0 none to 3 all), `TypingEcho` (0 off, 1 characters, 2 words, 3 both) | speech rate, punctuation level, typing echo |
| `default.jkm`, and `chrome.jkm`, `msedge.jkm`, `firefox.jkm`: `[Common Keys]` and `[Desktop Keys]` or `[Laptop Keys]` (whichever layout Vox uses) | `keymap.json`, for scripts Vox has a command for (say all, say line/word/character, window title, time, the virtual cursor toggle, the links/headings/form fields lists, quick navigation keys, table navigation, find...) |
| `*.jdf` dictionaries: `.word.replacement.voice.` lines | `dictionaries\user.json` for every voice, `dictionaries\voices\<voice>.json` for a voice installed for Vox |

The JAWS key (Insert, or Caps Lock on a laptop) becomes Vox's screen reader key. Only key
assignments are brought over, never what a script does: scripts with no Vox command, other
applications' `.jkm` files, braille and touch keys and keys with the Windows key are listed as
skipped. A dictionary word with `*` at its start or end ("*ing") matches the rest of the word.
