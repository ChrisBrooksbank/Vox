# Speech & Audio

## Overview

Bring speech up to what experienced users expect: several synthesizers, very high rates, pitch/volume/inflection, punctuation levels and symbols, pronunciation dictionaries, capital indication, automatic language switching, speech history, sleep mode, and a richer, replaceable earcon scheme. Builds on speech-engine.md. Workstream W4 of `COMMERCIAL_PARITY_PLAN.md` (Milestone B).

## User Stories

- As an experienced user, I want to read at 600+ wpm with a responsive voice
- As a blind user, I want to change rate, pitch, volume or voice without opening a dialog
- As a blind user, I want punctuation, symbols, emoji and capitals read the way I choose
- As a blind user, I want to fix how a word is pronounced
- As a bilingual user, I want Vox to switch voices when a page changes language
- As a blind user, I want to hear what Vox said a moment ago

## Requirements

### Engines

- [ ] `ISpeechEngine` gains pitch, volume and capability flags (supports pitch, supports SSML-like commands, languages)
- [ ] `OneCoreSpeechEngine` over `Windows.Media.SpeechSynthesis` (including natural voices where installed); default engine when available
- [ ] `SpeechEngineRegistry`: lists engines, switches at runtime without restarting; falls back to SAPI if an engine fails to start
- [ ] eSpeak NG engine as a separately downloaded optional component (GPL) loaded through the engine registry
- [ ] Rate boost: rates beyond the engine's native max via audio time-stretching or engine-specific boost, up to 900 wpm

### Settings ring

- [ ] Insert+Ctrl+Left/Right picks a setting (voice, rate, pitch, volume, punctuation, engine); Insert+Ctrl+Up/Down changes it; changes are spoken and saved

### Text processing (before the queue)

- [ ] `TextProcessor` pipeline stage applied to every utterance: symbols, dictionaries, numbers, repeated characters
- [ ] Punctuation level setting None / Some / Most / All, with a symbol dictionary file (`assets/speech/symbols-en.json`) seeded from Unicode CLDR annotations, each symbol tagged with the level it is spoken at
- [ ] Emoji names from CLDR
- [ ] Pronunciation dictionaries: default, per voice, user; entries are plain or regex with case options; user dictionary editable from settings
- [ ] Capitals: separate settings for character and word/line reading — raise pitch, say "cap", beep
- [ ] Repeated characters: runs of 4+ identical symbols read as "N dashes"
- [ ] Number setting: as words / as digits
- [ ] Spell and phonetic spell on double/triple press of read-char and read-word

### Language

- [ ] `LanguageSwitcher`: utterances carry an optional language tag (from `lang` in the buffer, text attributes in documents); pick the installed voice for that language (user mapping in settings), fall back to the default voice

### History and modes

- [ ] Speech history: last 100 utterances; Insert+Shift+F11 / Insert+Shift+F12 step back/forward, pressing twice copies the entry to the clipboard
- [ ] Sleep mode per app (Insert+Shift+S): Vox stops speaking and swallowing keys while that app has focus

### Audio cues

- [ ] Add earcons: list entry/exit, table entry/exit, landmark, clickable, error, progress beep; scheme stored in `assets/sounds/<scheme>/` with a manifest
- [ ] Optional indentation reporting by tone (for code)
- [ ] Setting to report states as sound only

## Acceptance Criteria

- [ ] Unit tests for `TextProcessor` (punctuation levels, emoji, dictionary rules incl. regex, capitals, repeated characters, numbers)
- [ ] Unit tests for settings ring state, engine registry fallback and language voice selection
- [ ] Manual: 600 wpm reading with natural pauses, bilingual page switches voice, a new dictionary rule applies at once

## Out of Scope

- Bundling commercially licensed voices (third-party engines can plug into the registry)
