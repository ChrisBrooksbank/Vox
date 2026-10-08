# eSpeak NG (optional speech engine)

eSpeak NG is a small, very fast synthesizer that many screen reader users prefer at high rates.
It is licensed under the GPL, so Vox doesn't ship it: install it as a separate component and Vox
loads it at runtime.

## Installing

1. Install eSpeak NG for Windows (64-bit) from <https://github.com/espeak-ng/espeak-ng/releases>.
2. Create the folder `%LOCALAPPDATA%\Vox\components\espeak-ng`.
3. Copy `libespeak-ng.dll` and the `espeak-ng-data` folder from the eSpeak NG installation
   (usually `C:\Program Files\eSpeak NG`) into it.
4. Restart Vox. "eSpeak NG" now appears as a synthesizer in the settings ring
   (Insert+Ctrl+Left/Right to "Synthesizer", then Insert+Ctrl+Up/Down), or set
   `"SpeechEngine": "eSpeak"` in `%APPDATA%\Vox\settings.json`.

Vox never loads the component on the sign-in and lock screens (secure mode). If it fails to
start, Vox falls back to the next engine (SAPI).

## How it works

`EspeakNative` loads `libespeak-ng.dll` with `NativeLibrary` and uses its synchronous API;
`EspeakSynthesizer` turns the samples into audio for `WaveSpeechEngine`, which plays them through
the same output device as the earcons (the `AudioOutputDevice` setting). eSpeak's own rate range
is 80–450 words per minute.
