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

## The JAWS keyboard layout

Settings, Keyboard (or step 4 of the welcome wizard: 3 and 4) offers two more keyboard layouts,
JAWS desktop (`Jaws`) and JAWS laptop (`JawsLaptop`): Vox's desktop or laptop layout with the
common JAWS keys from `assets/config/jaws-keymap.json` on top, a JAWS key winning over Vox's on
the same key. Keys JAWS and Vox already share (say all, say line, title, focus, window, font,
time, links list, find, top and bottom of the page, keyboard help and most quick navigation
letters) stay as they are; the layouts add:

| Keys | Command |
| --- | --- |
| Insert+Z | Browse mode on or off (the virtual cursor) |
| Insert+F6, Insert+F5, Insert+Ctrl+Semicolon | Elements list (headings, form fields, landmarks are tabs in it) |
| Insert+J | Vox menu (the JAWS window) |
| Insert+V | Settings (quick settings) |
| Insert+F4 | Quit Vox (pressed twice) |
| Numpad 5, Insert+Numpad 5 (desktop); Insert+Comma, Insert+K, Insert+I (laptop) | Say character, word; line on the laptop |
| R, Semicolon (browse mode) | Next landmark (region); with Shift, previous |
| A (browse mode) | Next radio button; with Shift, previous |

Vox commands whose key a JAWS key takes move: report the navigator object to Insert+Shift+O,
review the current word to Insert+Ctrl+Numpad 5 (desktop), and the link's address to
Insert+Shift+K (laptop). Every command keeps a key. The keyboard reference, input help
(Insert+1) and the practice lessons follow the layout in use; the user guide's
`keyboard-jaws.html` lists every command's keys in the two JAWS layouts.

## Custom labels

A button, link or graphic with no name (or a file name) can be given one: Insert+Ctrl+Tab on it
in browse mode, or while it has focus, asks for the label (as JAWS's Ctrl+Insert+Tab does). The
label is kept in `%APPDATA%\Vox\labels.json` for the site (scheme and host, such as
`https://mail.example.com`; a label can also be limited to a path prefix such as `/mail`), and
used from then on in browse mode, by quick navigation and the elements list, and when the element
gets focus; the element's own content (the icon's file name) is left out. Custom labels in the Vox
menu changes or deletes them. None on secure screens.

An element is found again by its signature: its automation id (the HTML `id`) when it has one,
else its role, the roles of its nearest meaningful ancestors (plain `div`s and `span`s left out)
and the last three words of text before it. So a label survives reloads, updates of other parts
of the page and the element itself being redrawn, but not a page redesign that changes the
element's surroundings (label it again then). A label given with another label just before it
depends on that one's text, as the text before it is part of its signature.

Labels can be shared: Export site's labels in the custom labels dialog writes the selected
label's site's labels as a `.voxlabels` label pack, and Import label pack adds a pack's labels
(replacing yours for the same elements) when the dialog closes with OK. A pack can also be
published as a label-pack add-on in the add-on store ([add-ons](addons.md#label-packs)); its
labels apply while it is enabled, under your own.

Graphics and icon-only buttons with no name are also labelled automatically, in the background,
when AI is set up: such labels are marked "described" in the dialog, and your own label replaces
one ([automatic labels](ai.md#automatic-labels)).

## Settings for a web site

Instead of a profile per browser, a site can have settings of its own (Vox menu, Settings for this
site, while browsing it): the mode its pages start in, verbosity, screen layout and rejecting
cookie banners automatically. They are laid over the application's profile while the site's
pages have focus; see [configuration profiles](profiles.md#settings-for-a-web-site).
