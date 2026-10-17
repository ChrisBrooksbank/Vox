# Vox documentation

Each guide is a plain page with headings, so a screen reader's heading keys move through it.
The guides are grouped by who they are for. To install Vox, start with [Installing Vox](install.md).

The **user guide** is installed with Vox: open the Vox menu (Insert+N) and choose User guide. It
comes with a keyboard reference that lists every command and its keys, and pages for people
coming from [NVDA](guide/nvda.html) and [JAWS](guide/jaws.html). Its source is in
[guide/](guide/index.html).

## Using Vox

- [Which screen reader should you use?](choosing-a-screen-reader.md): JAWS, NVDA, Narrator, the
  magnifier suites and Vox compared, what users wish their screen reader did, and whether to
  switch.
- [Installing Vox](install.md): download, install, first start, updating, uninstalling and the
  portable copy, with the keys for every step.
- [Your own keys](keymap.md): change or remove any of Vox's key bindings.
- [Configuration profiles](profiles.md): different settings per application or during Say All.
- [Firefox](firefox.md): what works in Firefox, and what doesn't yet (IAccessible2 support is in progress).
- [Magnifier](magnifier.md): enlarge the screen up to 16 times, following the focus, caret, browse
  cursor and mouse, with colour filters and docked and lens views; also the screen curtain.
- [Touch](touch.md): explore the screen by finger and use gestures on a touch-screen device.
- [Text recognition (OCR)](ocr.md): read text in pictures, scanned PDFs and inaccessible
  applications.
- [AI descriptions](ai.md): optional image descriptions, questions about the screen and page
  summaries, with what is sent where.
- [Remote access](remote.md): let someone control your computer and hear what Vox says, or control
  theirs.
- [Remote Desktop and Citrix](remote-sessions.md): running Vox inside a remote session.
- [Privacy policy](privacy.md): what Vox sends over the network, and when.
- [Beta programme](beta.md): testing new versions, and how to send a useful bug report.

### Applications

- [Microsoft Word](apps/word.md)
- [Microsoft Excel](apps/excel.md)
- [Microsoft Outlook](apps/outlook.md)
- [Microsoft PowerPoint](apps/powerpoint.md)
- [Microsoft Teams](apps/teams.md)
- [File Explorer](apps/explorer.md)
- [Visual Studio Code, Visual Studio, Zoom and Acrobat](apps/developer-tools.md)

### Braille, speech and math

- [Braille](braille.md): displays, braille tables, display keys and typing in braille.
- [BRLTTY](brltty.md): optional, for displays Vox doesn't support directly.
- [eSpeak NG](espeak.md): an optional, very fast speech engine.
- [MathCAT](mathcat.md): optional math speech and exploring expressions.

## For organisations

- [Vox for organisations](organisations.md): comparing the cost, a deployment checklist,
  conformance and security material, and how support works.
- [Deploying Vox in an organization](enterprise.md): silent install, machine defaults, Group
  Policy, and turning off the network, add-ons or AI.
- [Updates](updates.md): how updates are checked, signed and installed.
- [Accessibility conformance (VPAT outline)](vpat.md): a self-assessed draft, not yet audited.

## For developers and maintainers

- [Technical documentation](technical/README.md): how Vox works inside, with diagrams: the
  event pipeline and threads, keyboard input, UI Automation, the virtual buffer, speech,
  braille, settings and policy, extensibility and testing.
- [Installer and portable copy](installer.md): building `Vox.msi` and the portable zip, and the
  Release workflow.
- [Signing Vox and UI access](signing.md): why signed builds can read elevated windows.
- [Add-ons](addons.md): the add-on API, packages, signing and the store catalogue.
- [Translating Vox](translating.md): string resources, symbol names and checking a translation.
- [End-to-end tests](e2e.md): Vox against Edge, Notepad, Explorer and Settings.
- [Benchmarks](benchmarks.md): measuring how quickly Vox responds.
- [Performance](performance.md): keypress-to-speech latency and time to first speech, the CI
  regression gate, and how to measure NVDA and JAWS the same way.
- [Security review](security.md): reporting problems privately, and the checklist every release
  is reviewed against.
- [Static review rounds](reviews/README.md): the bug-fix plans from earlier code reviews, kept
  for reference.
- The architecture is also summarised on one page in [CLAUDE.md](../CLAUDE.md). The roadmap is in
  [PLAN.md](../PLAN.md) and [COMMERCIAL_PARITY_PLAN.md](../COMMERCIAL_PARITY_PLAN.md).

## Background

- [A short history of computers that talk](history.md).
