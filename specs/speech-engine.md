# Speech Engine

## Overview

Priority-based speech synthesis system using SAPI5 with interrupt semantics, coalescing, and audio cues.

## User Stories

- As a blind user, I want navigation actions to immediately interrupt current speech so I can browse quickly
- As a blind user, I want to adjust speech rate (150-450 WPM) and voice so it's comfortable to listen to
- As a blind user, I want audio cues (earcons) for mode changes and boundaries so I have spatial awareness

## Requirements

- [ ] `ISpeechEngine` interface: `SpeakAsync(Utterance, CancellationToken)`, `Cancel()`, `SetRate(int wpm)`, `SetVoice(string)`, `GetAvailableVoices()`, `IsSpeaking`
- [ ] `SapiSpeechEngine` wrapping `System.Speech.Synthesis.SpeechSynthesizer`
- [ ] Pre-init engine at startup to avoid first-utterance delay
- [ ] Cancel-before-speak: every Interrupt-priority utterance calls `SpeakAsyncCancelAll()` first
- [ ] User-facing WPM (150-450) maps to SAPI rate (-10 to +10)
- [ ] Select OneCore voices by default
- [ ] `Utterance` record: `Text`, `SpeechPriority`, optional `SoundCue`
- [ ] `SpeechPriority` enum: Interrupt > High > Normal > Low
- [ ] `SpeechQueue` backed by `Channel<Utterance>` with priority ordering
- [ ] Coalescing: multiple Normal-priority utterances within 50ms window get concatenated
- [ ] `IAudioCuePlayer` + `AudioCuePlayer` using NAudio `WaveOutEvent` with pre-loaded `CachedSound`
- [ ] Phase 1 sounds: `browse_mode.wav`, `focus_mode.wav`, `boundary.wav`, `wrap.wav`, `error.wav`

## Acceptance Criteria

- [ ] Interrupt-priority speech cancels any currently playing speech immediately
- [ ] Speech rate adjustment takes effect on the next utterance
- [ ] Audio cues play concurrently with speech (not blocking)
- [ ] Normal-priority utterances within 50ms coalesce into single speech call
- [ ] Unit tests for SpeechQueue priority ordering, interrupt behavior, and coalescing

## Out of Scope

- Azure AI premium voices (Phase 4)
- Braille display output (Phase 2)
- SSML markup
