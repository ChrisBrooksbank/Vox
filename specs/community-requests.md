# What users ask for

## Overview

Features screen reader users keep asking for, gathered while writing
`docs/choosing-a-screen-reader.md` (October 2026): NVDA's open feature requests on GitHub,
AppleVis forum threads on JAWS and Narrator, the NFB *Access On* podcast mailbag, WebAIM's 2026
survey and a 2026 interview study of how blind users seek help. Vox already answers several of
them (crash recovery, command search, speech history with copy, Group Policy, Teams and Zoom
call state, AltGr in typing echo). This spec covers the rest: a reader that stays quiet but
answers when asked, a magnifier for people who still use their sight, a screen curtain, meeting
and clipboard announcements, web markup other readers get wrong, Firefox through IAccessible2,
AI guidance through tasks, and touch. Follows Milestone F (Milestone G).

## User Stories

- As a blind user in a video call, I want Vox silent while I listen, but still to tell me the time, the window title or the current line when I ask
- As a user with some sight, I want to magnify the screen and have the view follow my focus, caret and browse cursor, with colour filters, without a second product
- As a blind user, I want to black out my screen so people around me can't read it
- As a blind user in meetings, I want one key to mute and unmute my microphone whatever app I'm in, and to hear the result
- As a blind user, I want to hear that a cut, copy or paste worked, and roughly what was copied
- As a blind user, I want definition lists, `aria-controls` and other markup read the way the author wrote it
- As a Firefox user, I want landmarks, ARIA states and live regions read as they are in Chrome
- As a blind user, I want AI to walk me through a task on the page in short numbered steps, with no visual-only instructions, saying when it isn't sure
- As a touch-screen user, I want to explore and navigate by touch

## Requirements

### Speech on demand

- [ ] A speech mode beside normal speech and Pause speech: in on-demand mode only utterances from commands that report something on request are spoken (time, window title, current line/word/character, focus, read window, Say All, status bar, navigator object, selection, page summary); everything else (focus changes, typing echo, live regions, navigation) is dropped. Braille is unchanged
- [ ] Commands are marked as reporting in `CommandCatalog` (one source; a test checks every command has a decision); utterances carry the mark (`Utterance.OnDemand`) and `SpeechQueue` gates them beside `IsMuted`
- [ ] A toggle command with a key in every layout, a Vox menu item and a setting that profiles can set, so it can turn on automatically in Teams or Zoom; a cue and a spoken state when it changes

### Magnifier

- [x] Full-screen magnification through the Windows Magnification API (`MagSetFullscreenTransform`), 1× to 16×, in steps; pure viewport maths (`MagnifierViewport`: zoom, offsets, clamping to the virtual screen, multiple monitors) unit-tested
- [x] Tracking: the view follows the focus, the caret, the browse cursor's element, the review cursor and the mouse, with a setting per source, a priority order (newest keyboard-driven move wins over the mouse for a short time) and smooth or instant movement
- [x] Colour filters through `MagSetFullscreenColorEffect`: inverted, greyscale, inverted greyscale and high-contrast yellow on black; smoothing on or off
- [x] Docked and lens views in a magnifier window (Magnification control), moved and sized from the keyboard
- [x] Commands and keys (on/off, zoom in/out, next filter, view, pan), a settings page, profile settings, policy to turn it off; refuses to start alongside Windows Magnifier and says so; not in secure mode at first
- [x] Offered in the first-run wizard ("Do you use magnification?"); `docs/magnifier.md`

### Screen curtain

- [x] A screen curtain that blacks out the display (full-screen colour effect to black) while Vox keeps reading; toggle key and Vox menu item; optional at start-up with a spoken warning; can't be on with the magnifier (the curtain wins, and Vox says so); turned off when Vox exits or crashes (the watchdog removes it)

### Meetings and the clipboard

- [ ] A mute-microphone command that works anywhere: in Teams, Zoom and Google Meet or Teams in a browser it presses the app's own mute button (`IWindowButtonSource`, page buttons through the buffer) and says the new state; elsewhere it toggles the default capture device's mute through Core Audio (`IAudioEndpointVolume`) and says "microphone muted" or "unmuted"
- [ ] Clipboard announcements: after Ctrl+X, Ctrl+C or Ctrl+V in text, browse mode and File Explorer, say "cut", "copied" or "pasted" with a short description (a few words of text, or "3 files"); a setting to turn off; nothing from password fields; only when the clipboard actually changed (sequence number)

### Web markup

- [ ] Definition lists (`dl`/`dt`/`dd`, or `role=term`/`definition`) read as lists of terms and definitions: "definition list with N items", term and definition announced, `L`/`I` move through them
- [x] `aria-controls` beyond combo boxes: a control that controls another region (tabs, accordions, disclosure buttons) offers a command to move to the controlled element, and says "controls" on focus at Intermediate verbosity
- [ ] Dialog content announced once: a dialog whose description repeats its text isn't read twice on open (focus announcement and overlay announcement merged)

### Firefox through IAccessible2

- [ ] `Vox.NativeHelper` (C++/CLI, `PLAN.md` Phase 3) with the IA2 proxy/stub and a COM-free snapshot of a Firefox document's IA2 tree (roles, `xml-roles`, `level`, ARIA states, relations, text attributes)
- [ ] An `IVBufferElement` provider over that snapshot, chosen for documents in a Firefox window, so landmarks, ARIA states, visited links, `aria-describedby`/`aria-errormessage` and `lang` work as in Chromium (`docs/firefox.md`)
- [ ] IA2 live-region and text-attribute events for Firefox, posted to the pipeline like UIA's

### AI guidance

- [ ] "Guide me": the user describes a task on the current page ("book the 9:30 appointment"), and the answer comes back as short numbered steps naming elements by their text and role (never colour, position or appearance), with each step offering to move the browse cursor to the element it names (as Ask the page does) and saying when the model isn't sure. Local model first, cloud with consent; the prompt and the answer checks are unit-tested over a fake describer
- [ ] Vox tells the model which screen reader keys and browser are in use, so steps name Vox's own keys

### Touch

- [ ] Touch exploration and gestures on touch screens: explore by finger, double-tap to activate, flicks for next/previous object and, in browse mode, for headings, links and form fields (rotor-like mode choice), two-finger tap to stop speech; a touch keymap like the keyboard's; design first in `docs/technical/`, then implementation

## Acceptance

- A user in a Teams call turns on speech on demand, hears nothing while people talk and focus moves, and hears the time and the window title when asked
- A low-vision user magnifies to 4×, Tabs through a form in Edge and the view follows each field; inverts colours; switches to a docked view
- With the screen curtain on, the display is black, Vox reads normally, and it is gone after Vox quits or crashes
- The mute key mutes Teams, a Meet call in Edge, and the microphone in an app Vox doesn't know, each time saying the new state
- Copying a sentence in Notepad says "copied" and its first words; pasting says "pasted"
- A definition list on a test page reads as terms and definitions; `L` and `I` move through it
- On a Firefox page, `D` moves between landmarks and an `aria-invalid` field is reported invalid
- "Guide me" returns numbered steps for a test page task, and choosing a step moves the browse cursor there
