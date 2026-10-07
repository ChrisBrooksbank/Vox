# Vox: Road to Commercial Parity

What Vox needs before someone who uses JAWS or NVDA every day could switch to it for a full
working day and not go back.

**Compared against:** JAWS (Freedom Scientific, the commercial leader), NVDA (free, the
most-used reader on Windows per WebAIM), Windows Narrator (built in), and ZoomText Fusion /
Dolphin SuperNova where low vision matters.

**How this relates to the other plans:** `PLAN.md` is the original phased roadmap.
`IMPLEMENTATION_PLAN.md` is the loop's task list (Phase 1, all 39 tasks done). This document
is the gap analysis and the reordered roadmap after Phase 1. Each workstream below should get
its own `specs/<name>.md` and a task block in `IMPLEMENTATION_PLAN.md` when it starts.

---

## 1. Where Vox stands after Phase 1

**Already competitive (Chromium web only):**

- Browse mode virtual buffer with line/word/char/paragraph movement
- Quick nav: `H`, `1`–`6`, `K`, `D`, `F`, `T`, `Tab`, top/bottom of document
- Automatic focus/browse mode switching with earcons; Escape handling for popups
- Elements List (`Insert+F7`), Say All (`Insert+Down`)
- Live regions and UIA notifications, polite/assertive priorities
- Incremental buffer updates on dynamic pages; reading position remembered per document
- Typing echo that follows the keyboard layout; three verbosity presets; speech-only setup wizard

**Missing, roughly in order of how soon a user notices:**

| # | Gap | Effect on a user today |
|---|-----|------------------------|
| 1 | No caret tracking outside web documents (no UIA `TextPattern`) | Can't proofread an email, edit a file in Notepad, or use Word. Arrowing in a text box says nothing useful. |
| 2 | No protection against hung apps (one STA thread, no call timeouts) | One frozen app silences the reader. You have to reboot without sight. |
| 3 | Elevated windows and secure screens can't be read (needs admin; no `uiAccess`, no logon/UAC support) | Can't answer UAC prompts, sign in, or use Ctrl+Alt+Del screens. |
| 4 | No review cursor or object navigation | Can't read status bars, dialog text that has no focus, or controls you can't Tab to. |
| 5 | Desktop events are thin (no value/state/selection change speech, no "3 of 10", no dialog auto-read, no window title on switch) | Explorer, Settings and other dialogs are barely usable. |
| 6 | One synthesizer (SAPI5), no pitch/volume/punctuation/caps/dictionaries | Experienced users read at 400–700+ wpm and expect to fine-tune pronunciation. |
| 7 | No braille | About 38% of respondents use braille. Deafblind users can't use Vox at all. |
| 8 | Web gaps: Firefox, table cell navigation, lists, most quick-nav keys, find, selection/copy, PDF | Daily web tasks hit dead ends. |
| 9 | No settings dialog, help mode, tray, installer, auto-update, localization | Vox can't be installed, configured or supported the way a product is. |
| 10 | No Office, Outlook, Teams or terminal support | This is where JAWS earns its licence fee at work. |
| 11 | No OCR or image description | Inaccessible apps and images are dead ends. JAWS (Picture Smart AI) and Narrator now cover this. |
| 12 | No add-on API | Most of NVDA's long-tail app support comes from add-ons. |
| 13 | No CI, no end-to-end tests on a real desktop, no beta program with blind testers | Regressions ship without anyone noticing. |

---

## 2. Feature parity matrix

✅ = solid, 🟡 = partial, ❌ = missing

| Capability | JAWS | NVDA | Narrator | Vox | Workstream |
|---|:-:|:-:|:-:|:-:|---|
| Browse mode, Chromium | ✅ | ✅ | ✅ | ✅ | — |
| Browse mode, Firefox | ✅ | ✅ | 🟡 | ❌ | W5 |
| Full quick-nav key set (B, E, C, X, R, L, I, G, Q, M, N, V, U, S, O, …) | ✅ | ✅ | 🟡 | 🟡 | W5 |
| Table navigation (Ctrl+Alt+arrows, headers) | ✅ | ✅ | ✅ | ❌ | W5 |
| Find in page | ✅ | ✅ | 🟡 | ❌ | W5 |
| Select and copy in browse mode | ✅ | ✅ | 🟡 | ❌ | W5 |
| Caret and selection in native edit controls | ✅ | ✅ | ✅ | ❌ | W2 |
| Desktop focus, state and value announcements | ✅ | ✅ | ✅ | 🟡 | W2 |
| Review cursor / object navigation / screen review | ✅ | ✅ | ✅ | ❌ | W3 |
| Mouse tracking | ✅ | ✅ | ✅ | ❌ | W3 |
| Logon screen, UAC, elevated apps | ✅ | ✅ | ✅ | ❌ | W1 |
| Survives hung apps | ✅ | ✅ | ✅ | ❌ | W1 |
| Several synthesizers, OneCore/natural voices | ✅ | ✅ | ✅ | ❌ | W4 |
| Punctuation levels, symbols, pronunciation dictionaries | ✅ | ✅ | 🟡 | ❌ | W4 |
| Automatic language switching | ✅ | ✅ | 🟡 | ❌ | W4 |
| Braille output and input (contracted, routing) | ✅ | ✅ | ✅ | ❌ | W6 |
| Word, Excel, Outlook, PowerPoint | ✅✅ | ✅ | 🟡 | ❌ | W7 |
| Terminals (Windows Terminal, conhost) | ✅ | ✅ | ✅ | ❌ | W2 |
| PDF (Edge, Acrobat) | ✅ | ✅ | 🟡 | ❌ | W5 |
| Math (MathML) | ✅ | ✅ (MathCAT) | ❌ | ❌ | W5 |
| OCR | ✅ | ✅ | ✅ | ❌ | W9 |
| AI image or screen description | ✅ | 🟡 (add-ons) | ✅ | ❌ | W9 |
| Settings GUI and per-app profiles | ✅ | ✅ | ✅ | ❌ | W8 |
| Remap any key from the GUI | ✅ | ✅ | 🟡 | 🟡 (JSON) | W8 |
| Help mode, keyboard help, built-in guide | ✅ | ✅ | ✅ | ❌ | W8 |
| Installer, portable copy, auto-update | ✅ | ✅ | n/a | ❌ | W8 |
| Localized UI and speech | ✅ | ✅ | ✅ | ❌ | W8 |
| Scripting / add-ons | ✅ (scripts) | ✅ (add-on store) | ❌ | ❌ | W10 |
| Speech history and speech viewer | ✅ | ✅ | 🟡 | ❌ | W4 |
| Remote access (screen reader to screen reader) | ✅ (Tandem) | ✅ (Remote) | ❌ | ❌ | W10 |

---

## 3. Recommended order (changed from `PLAN.md`)

`PLAN.md` puts braille first in Phase 2. I recommend doing **robustness and desktop text
editing first**. Without them, a user can't write the email they just read or recover from a
frozen app, so nobody can use Vox for a whole day, braille user or not. Braille follows
straight after.

```
Milestone A  "Won't strand you"        W1 robustness/secure desktop  +  W2 desktop core
Milestone B  "Daily driver, home use"  W3 review/object nav  +  W4 speech  +  W5 web parity  +  W8a settings/help/installer
Milestone C  "Braille-ready"           W6 braille
Milestone D  "Workplace-ready"         W7 Office/Teams/Outlook  +  W8b profiles/localization/enterprise deploy
Milestone E  "Better than"             W9 OCR/AI  +  W10 add-ons/remote
Throughout:                            W11 quality engineering
```

Rough sizes for one experienced developer (with the Ralph loop speeding up the routine
parts): A ≈ 10–12 weeks, B ≈ 16–20, C ≈ 8–10, D ≈ 14–18, E ≈ 10+. That is about 15–18 months
to reach JAWS/NVDA parity for mainstream use. JAWS's 35 years of app-specific scripts are a
long tail that only an add-on ecosystem (W10) will close.

---

## 4. Workstreams

Each workstream lists what to build, where it goes, and an **exit test** written as something
a blind user can do with speech only.

### W1. Robustness, privileges and secure screens (Milestone A)

A commercial screen reader must never leave the user stranded.

- **Hung-app protection.** Today every UIA call runs on the single `UIAThread`, so one
  unresponsive provider blocks everything.
  - Per-call timeouts on `UIAThread.RunAsync` (cancel and report "not responding"), with a
    watchdog that replaces a stuck STA thread with a new one and rebuilds the `CUIAutomation`
    instance.
  - Set `IUIAutomation2.ConnectionTimeout` / `TransactionTimeout` low (e.g. 2 s / 1 s).
  - Keep the keyboard path independent: Vox's own commands (stop speech, quit, Alt+Tab
    echo) must still work while UIA is stuck.
  - Process-level watchdog (a small `Vox.Watchdog` exe) that restarts Vox if it crashes and
    always removes the hook.
- **Run without admin; read elevated windows.** Ship with `uiAccess="true"` in the app
  manifest, Authenticode-signed, installed under `Program Files`. This is how NVDA and
  Narrator reach elevated apps without running elevated, and it makes the "needs admin"
  requirement in `CLAUDE.md` unnecessary. It needs a code-signing certificate (EV
  recommended for SmartScreen).
- **Logon screen, lock screen and UAC (secure desktop).** Install a small Windows service
  that starts a restricted Vox instance on `Winlogon` desktop switches, the way NVDA's
  `nvda_service`/secure-mode does. Secure mode means no add-ons, no settings writes, no
  network or AI, and no access to the user's profile.
- **Start at logon**, plus a global start shortcut (Ctrl+Alt+V, created by the installer).
- **Audio:** WASAPI output with device selection; duck other audio while speaking
  (`AudioCategory_Communications` or audio ducking API); keep the output device alive so the
  first syllable isn't clipped.
- **Diagnostics:** speech viewer window, `Insert+F1`-style "report developer info",
  log level toggle, and a one-key "copy last log to clipboard" for bug reports.
- **Latency budget, measured:** from keypress to the start of audio in under 50 ms for
  cursor movement and under 100 ms for focus changes. Add timing instrumentation along the
  hook → dispatcher → pipeline → speech path and log the 95th percentile.

**Exit test:** Freeze a test app with `Thread.Sleep` in its UIA provider, then Alt+Tab away.
Vox keeps talking. Lock the PC, unlock it by voice, approve a UAC prompt, open Task Manager
(elevated) and read its process list. Restart Windows and hear Vox at the sign-in screen.

### W2. Desktop core: text editing and controls (Milestone A)

This is the biggest functional gap. Today Vox is a web reader.

- **`TextPattern` caret tracking** (new `Accessibility/TextCaretTracker.cs`). On caret
  movement in a focused edit control (`TextSelectionChanged` / `TextEditTextChanged`, with a
  fallback to polling the caret via `GetGUIThreadInfo`), read the character, word or line
  that matches the key pressed. Arrow keys read char/line, Ctrl+arrows read word/paragraph,
  Home/End and Ctrl+Home/End too.
  - Selection speech: "selected …" / "unselected …", Shift+arrows, Ctrl+A ("all selected").
  - Deletion echo: Backspace and Delete speak the deleted character.
  - Say All in edit controls (the existing `SayAllController` over a `TextRange` adapter).
  - Read the current line, selection, and formatting (`Insert+F`: font, size, bold,
    spelling error) via text attributes.
  - Spelling and grammar errors: report "misspelled" on caret entry; an earcon option.
  - A shared abstraction: introduce `ITextDocument` so `VBufferCursor` (browse mode) and
    UIA `TextRange` (native edits) share one cursor/command layer and one keymap.
- **Terminals:** Windows Terminal and conhost via UIA `TextPattern` and text-changed diffing
  (announce new output lines, with throttling for fast output; review of earlier output
  through W3).
- **Control events and announcements outside documents:**
  - Window title on foreground change; dialog auto-read (title plus static text that has no
    focus target), like NVDA's "report dialog text".
  - `PropertyChanged` for Toggle, ExpandCollapse, Value (sliders, spin boxes) and
    RangeValue (progress bars, with a beep or percentage every N % and throttling),
    `SelectionItem` / `Selection` changes, `IsEnabled`.
  - Position info: "3 of 10", tree level ("level 2"), list/menu/tab counts
    (`PositionInSet`, `SizeOfSet`, `Level`).
  - Menus: menu opened/closed, submenu entry, accelerator keys and shortcut text.
  - Tooltips and toast notifications (already partly in place), system tray icons
    (Win+B), Start menu search results, Alt+Tab switcher, Win+Tab, virtual desktop switches.
- **Where am I commands:** say title (`Insert+T`), say focus (`Insert+Tab`), status bar
  (`Insert+End`), time/date (`Insert+F12`), battery (`Insert+Shift+B`), current
  selection (`Insert+Shift+Down`), read window (`Insert+B`). Add these to
  `default-keymap.json`.
- **Typing echo** extended to native edits (it is currently document-focused).

**Exit test:** In Notepad, write, proofread and edit a paragraph with arrows, Ctrl+arrows,
Shift-selection and Backspace, hearing exactly what moved or changed. Rename a file in
Explorer, change a toggle and a slider in Windows Settings, and follow a long copy's
progress bar.

### W3. Review cursor, object navigation, mouse (Milestone B)

- **Object navigation** (`Navigation/ObjectNavigator.cs`): parent/child/next/previous in the
  UIA tree (NVDA `Insert+Numpad8/2/4/6`, plus laptop layout), move navigator to focus,
  activate or click the navigator object, report its location.
- **Review cursor** (`Navigation/ReviewCursor.cs`): read by line/word/char over the
  navigator object's text or the whole screen (screen review via UIA text plus OCR fallback,
  W9). Pressing twice spells; three times spells phonetically.
- **Mouse:** speak what is under the pointer (optional, throttled `ElementFromPoint`),
  route mouse to navigator, left/right click at the navigator, lock mouse.
- **Laptop keyboard layout:** a full alternative keymap without a numpad (CapsLock-based,
  like NVDA's laptop layout); the wizard asks which.

**Exit test:** Read a status bar that has no keyboard focus, click a toolbar button that has
no keyboard access, and review a terminal's scrollback, all on a laptop without a numpad.

### W4. Speech and audio (Milestone B)

- **Engines** behind `ISpeechEngine`, selectable at runtime:
  - **OneCore** (`Windows.Media.SpeechSynthesis`), including the Windows 11 "natural"
    voices. Default where available.
  - **eSpeak NG** (in-process, GPL: check license compatibility with MIT distribution or
    ship it as a separate optional component). Fast and responsive above 500 wpm, many
    languages.
  - SAPI5 (existing). Leave room for a third-party engine plug-in (e.g. Eloquence, which
    JAWS ships, is commercially licensed).
- **Rate boost** (up to around 800+ wpm), pitch, volume, inflection; a **settings ring**
  (`Insert+Ctrl+Up/Down/Left/Right`) to change them without opening a dialog.
- **Text processing pipeline** (`Speech/TextProcessor.cs`, before the queue):
  - Punctuation levels None/Some/Most/All and a symbol dictionary seeded from Unicode CLDR
    annotations (emoji, maths, currency).
  - Pronunciation dictionaries (default, per voice, user) with regex rules.
  - Capitals: pitch change, "cap" prefix, or beep (separately for character and
    word/line reading).
  - Number handling (digits vs. full numbers), repeated characters ("dash dash dash" →
    "4 dashes"), URL and email shortening.
  - Spell/phonetic spell on double/triple press of read-char/word.
- **Automatic language and dialect switching** from `lang` attributes (web) and text
  attributes (Word), mapped to an installed voice per language.
- **Speech history** (review recent utterances; copy the last one) and **sleep mode** per app
  (for self-voicing apps).
- **Audio cue scheme** expanded: entering/leaving lists, tables, landmarks and clickable
  items; errors; optional indentation tones (for Python/YAML); a "sound only" verbosity for
  state changes. Make the scheme user-replaceable.

**Exit test:** Read at 600 wpm with natural pausing on punctuation; hear an emoji, a
currency amount and "Ⅻ" read correctly; read a bilingual page with automatic voice switching;
add a pronunciation rule and hear it applied at once.

### W5. Web parity (Milestone B)

- **Firefox** (and Thunderbird): Gecko exposes IAccessible2. Add `IA2Bridge` (in-process
  IA2 needs the `Vox.NativeHelper` from `PLAN.md` Phase 3 for speed), or use Gecko's
  UIA support where it is now good enough. The buffer already abstracts elements behind
  `IVBufferElement`, so this is a second element provider.
- **Electron and WebView2 apps** (Teams, Slack, VS Code, Discord, Outlook new): confirm
  document detection, the use of `aria-application`/`role="application"` (default to focus
  mode), and per-app default modes.
- **Complete quick-nav set** in the keymap: `B` button, `E` edit, `C` combo box, `X` check
  box, `R` radio, `L` list, `I` list item, `G` graphic, `Q` block quote, `M` frame/iframe,
  `S` separator, `O` embedded object, `V`/`U` visited/unvisited link, `N` skip past links
  (next non-link text), `P` paragraph (JAWS), `J` not-yet-visited heading, plus `,`/`.`
  start/end of container. Add an Elements List tab for buttons and form fields of each kind.
- **Tables** (`Navigation/TableNavigator.cs`, `Buffer/TableModel.cs`): Ctrl+Alt+arrows to
  move cell to cell, row/column headers announced when they change, "row 3 column 2",
  dimensions, merged cells, layout-table heuristics, read whole row/column, set header
  row/column manually (JAWS `Insert+Alt+R/C`).
- **Lists:** "list with N items", nesting level, "out of list" while reading.
- **Find** (`Navigation/FindInBuffer.cs`): `Insert+Ctrl+F`, `Insert+F3` /
  `Insert+Shift+F3`, history, case options.
- **Selection and copy in browse mode:** Shift+arrow selection over the virtual buffer,
  `Ctrl+C` copies plain text and, if possible, HTML. `Insert+F9` "mark", select from mark.
- **ARIA and HTML detail:** `aria-describedby`, `aria-details`, `aria-errormessage`
  with `aria-invalid`, `aria-current`, `aria-pressed`, `aria-sort`, `aria-roledescription`,
  `aria-braillelabel`, `aria-keyshortcuts`, annotations (comments, insertions,
  deletions, highlights), `<abbr>` expansion option, `<figure>`/`<figcaption>`, `<dialog>`
  and `aria-modal` (restrict the buffer to the modal), clickable non-links ("clickable"),
  same-line layout of inline elements (NVDA's "screen layout" option).
- **Activation fallbacks:** when there is no `Invoke`/`Toggle`/`SelectionItem` pattern,
  simulate a click at the element's bounding rectangle; offer "route mouse and click" (W3).
- **Page summary** (`Insert+F1`-style: title, number of headings/links/landmarks/forms,
  language) and "say the current element's URL".
- **Overlay and cookie-banner handling** (as in `PLAN.md` W13–14): announce, offer
  dismissal, never auto-dismiss.
- **PDF** in Edge (Chromium tagged-PDF tree, works through UIA) and Adobe Acrobat
  (MSAA/IA2), with a warning for untagged PDFs and an OCR offer (W9).
- **Rich editors:** Google Docs/Sheets (they expect braille-support mode or focus mode),
  `contenteditable` editing through W2's caret tracking.
- **Math:** MathML via MathCAT (Rust, available as a library) for speech and Nemeth/UEB
  braille, with interactive exploration.
- **Big-page performance:** stay under 500 ms on a 10,000-node page (long Wikipedia article,
  large GitHub diff): build in stages (first screenful first), off-thread indexing, and the
  native helper if needed.

**Exit test:** On the W3C ARIA APG examples, every pattern reads correctly. A user can book a
flight, complete a long form with errors, read a data table with headers, and read a
tagged PDF, in both Chrome and Firefox.

### W6. Braille (Milestone C)

- **Translation:** liblouis via P/Invoke (`Braille/LibLouisTranslator.cs`); UEB grade 1/2,
  computer braille, and per-language tables. Contracted braille has to show the
  uncontracted word under the cursor.
- **Drivers:** the Windows 11 HID braille standard first (it covers most new displays),
  then BRLTTY's `brlapi` as a bridge for older serial/USB displays (Freedom Scientific
  Focus, HumanWare Brailliant, Handy Tech, Baum, Papenmeier). Writing native drivers for
  each costs a lot, so use BRLTTY where it already works.
- **Presentation:** focus context (show the control plus ancestors that changed), braille
  for browse mode and edit controls with a cursor shape, panning, routing keys (move the
  caret / activate), word wrap, flash messages with a timeout, "tether to focus or review".
- **Input:** a braille keyboard (contracted input with back-translation), chords for
  commands, per-display key maps in JSON.
- **Settings:** table, cursor blink/shape, message timeout, show selection (dots 7–8).

**Exit test:** With speech muted, a deafblind tester reads and replies to an email and
browses a news site on a 40-cell HID display, and on a Focus 40 through BRLTTY.

### W7. Office and productivity apps (Milestone D)

These are the main reason employers buy JAWS licences.

- **App module framework** (`AppModules/IAppModule.cs` from `PLAN.md`), keyed by process
  name, able to override keymaps, announcements and event handling.
- **Word:** UIA document reading (W2) plus the object model (COM, out-of-process) for richer
  data. Covers headings/lists/tables quick nav in documents, comments, tracked changes,
  footnotes, page/section info, spelling and grammar navigation, styles and formatting
  reports.
- **Excel:** cell coordinates and content on move, formulas (`Insert+F2`-style), column and
  row titles (named ranges like JAWS's "Title" feature), selection range size, sheet
  switching, filters, charts summary, comments and notes, validation input messages.
- **Outlook (classic and new):** message list columns read in a configurable order,
  unread/flagged/attachment status, reading pane autoread, calendar day/week views,
  meeting requests.
- **PowerPoint:** slide and shape navigation, notes, slideshow reading.
- **Teams:** chat/channel navigation, meeting controls, incoming notifications, call state.
- **Others:** File Explorer details view (column headers), Visual Studio Code (audio cues
  and accessible view already in VS Code; make sure Vox doesn't fight them), Visual Studio,
  Windows Terminal (W2), Zoom.

**Exit test:** In a scripted "first day at work" scenario, a blind tester triages Outlook
mail, accepts a meeting, edits a Word report with tracked changes, updates an Excel budget
with formulas and joins a Teams call, without sighted help.

### W8. Product: settings, help, install, languages (Milestone B: a; D: b)

**W8a**

- **Accessible settings dialog** (WinForms or WinUI, tested with Vox itself) for every
  `VoxSettings` field, plus speech, braille, keyboard, mouse, document formatting, audio
  cues and verbosity. Changes apply live (the `IOptionsMonitor` plumbing exists).
- **Input gestures dialog** to remap any command, with conflict detection (writes to a user
  keymap layered over `default-keymap.json`).
- **Vox menu** (`Insert+N`) and a **system tray icon**: settings, help, pause/resume,
  speech viewer, exit.
- **Help:** keyboard help mode (`Insert+1`: keys say what they do), a command search
  ("what is the key for…"), context help for the focused control, a user guide (HTML that
  reads well with Vox) and a short interactive tutorial (extends the first-run wizard).
- **Installer and updates:** signed MSIX or MSI (`uiAccess` needs a signed install under
  Program Files), portable copy on a USB stick, start on logon, an update check that speaks
  and installs, release channels (stable/beta), and an alternate download for offline
  installs.

**W8b**

- **Configuration profiles:** per app and triggered (Say All, specific window), like NVDA
  profiles and JAWS settings center per-app settings.
- **Localization:** `.resx` for every spoken string and UI string, translated symbol
  dictionaries and braille tables, RTL text in the buffer, and a translators' workflow.
  Start with high-demand languages (Spanish, French, German, Portuguese, Chinese, Japanese,
  Arabic, Hindi).
- **Enterprise:** silent install, MSI properties, group policy/ADMX for settings, roaming
  settings, no network calls unless enabled, accessibility-compliance documentation (VPAT),
  and compatibility with Citrix/RDP (remote UIA works poorly; plan for it).

### W9. OCR and AI (Milestone E)

- **OCR** (`Windows.Media.Ocr`, fully local): recognize the window, the navigator object or
  an image, then show the result as a reviewable buffer with click-on-word.
- **Image description:** local model first (Windows AI APIs on Copilot+ PCs), then an
  optional cloud provider the user configures (for example the Claude API), with clear
  consent per request. Cache by image hash. Never send password fields, secure-desktop
  content or anything from a profile marked private.
- **Screen/app explanation:** "What's on this screen and how do I reach X?", using the
  UIA tree plus a screenshot, with the answer read back and spoken controls listed.
- **CAPTCHA assistance:** detect and point the user to audio alternatives or accessibility
  bypass options (hCaptcha accessibility cookie, etc.) rather than solving them, which
  breaks most sites' terms.
- **Page summarization and "find the main content"** for cluttered pages.

### W10. Extensibility (Milestone E)

- **Add-on API**: a versioned, documented interface for app modules, commands,
  announcement overrides, speech/braille drivers and global plug-ins, loaded in a separate
  `AssemblyLoadContext`; signed add-ons; disabled on secure screens and in a safe mode.
- **Add-on store** with community review.
- **Remote access** between two Vox machines (like NVDA Remote / JAWS Tandem) for remote
  support and training.

### W11. Quality engineering (all the time)

- **CI on Windows** (GitHub Actions `windows-latest`): `dotnet build && dotnet test` on
  every PR. The repository has no CI today.
- **End-to-end "spoken output" tests:** start Vox with a recording `ISpeechEngine`, drive
  Edge/Chrome/Firefox through Playwright on a local test-page corpus (ARIA APG patterns,
  tables, forms, live regions), send keys, and compare transcripts with approved snapshots.
  Do the same for Notepad, Explorer and Settings through UI automation.
- **Performance benchmarks** in CI: buffer build time per node count, keypress-to-speech
  latency, memory over a 4-hour soak test.
- **Fault injection:** hung providers, crashing browsers, monitor unplugging, sleep/resume,
  fast user switching, RDP.
- **Blind tester programme:** recruit 10–20 daily JAWS/NVDA users for a closed beta per
  milestone. Track their switching blockers as the main backlog input.
- **Security review:** keyboard hook and `uiAccess` handling (a screen reader is a keylogger
  with privileges), update channel integrity (signed updates only), no secrets in logs
  (typed text must not reach Serilog), privacy policy for AI features.

---

## 5. Big risks and decisions to make early

| Risk / decision | Why it matters | Suggested approach |
|---|---|---|
| Code-signing certificate and `uiAccess` | Needed for elevated windows, secure desktop and SmartScreen trust | Get an EV certificate before Milestone A finishes; build signing into CI |
| Licenses (eSpeak NG is GPL, BRLTTY is LGPL, liblouis is LGPL, MathCAT is MIT) | Vox is MIT | Ship GPL parts as separate optional downloads; dynamic linking for LGPL |
| UIA performance on huge pages and in Office | JAWS and NVDA use in-process hooks for speed | Keep `IVBufferElement` so a native IA2 or in-process helper can replace UIA providers later |
| Single-threaded UIA design | Hung-app freezes are the most dangerous failure | Fix first (W1); test with fault injection |
| Scope versus 35 years of JAWS scripts | Endless app-specific work | Generic UIA quality first, then an add-on ecosystem for the long tail |
| Trust | Users depend on a screen reader to use the PC at all | Stable channel, blind testers before each release, a guaranteed way to quit and restart by keyboard |

---

## 6. Definition of "as good as commercial products"

Vox reaches parity when all of these are true:

1. A blind user can install it, sign in to Windows and handle UAC without sighted help.
2. A daily NVDA/JAWS user completes a full working day (web, email, documents, chat,
   files) using only Vox, and rates it at least as efficient (task timing study).
3. No hang or crash leaves the user without speech in a 4-hour soak test with fault
   injection.
4. All W3C ARIA APG patterns pass the spoken-output test suite in Chrome, Edge and Firefox.
5. Braille users can do (2) on a HID display with speech off.
6. Keypress-to-speech latency is ≤ 50 ms at the 95th percentile on mid-range hardware.
7. Vox is available in at least 8 languages, with signed installers and automatic updates.
