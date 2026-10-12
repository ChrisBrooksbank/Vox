# Bringing your settings from NVDA

Vox can read another screen reader's settings and bring over what it has a counterpart for. Each
import ends with a report: what was brought over (with the value it replaced in Vox), and what was
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
