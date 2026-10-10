# MathCAT (optional math speech)

[MathCAT](https://github.com/NSoiffer/MathCAT) turns MathML into speech ("x squared", "fraction a over b") and lets you explore an expression part by part. Vox doesn't ship it: install it as a separate component, as with eSpeak NG (`docs/espeak.md`).

## Installing

1. Get the Windows 64-bit build of MathCAT's C interface (`libmathcat_c.dll`) and its `Rules` folder (from the MathCAT releases, or from the MathCAT NVDA add-on, which carries both).
2. Create the folder `%LOCALAPPDATA%\Vox\components\mathcat`.
3. Copy `libmathcat_c.dll` and the `Rules` folder into it.
4. Restart Vox.

Vox never loads the component on the sign-in and lock screens (secure mode).

## Using it

- Moving onto an expression by line or paragraph in browse mode says it through MathCAT (by word or character it is read as text).
- Insert+Alt+M on an expression starts exploring it: Right/Left (or Ctrl+Right/Left) move to the next/previous part, Down zooms in, Up zooms out, Home/End go to the first/last part, Insert+Up reads the current part. Insert+Alt+M again (or any other command) stops exploring.

Without MathCAT, math is read as its plain text and Insert+Alt+M says that the component is needed.

## How it works

`MathMarkup` finds math in the buffer (role `math`: Chromium's `<math>` and `role="math"`) and rebuilds its MathML from the elements below it, which Chromium reports with their MathML tag names as roles (`mi`, `mn`, `mo`, `mfrac`...); MathML text in the math element's name or description (MathJax's alternative text) is used as is. `MathCatSpeech` loads `libmathcat_c.dll` with `NativeLibrary` and calls `SetRulesDir`, `SetPreference`, `SetMathML`, `GetSpokenText` and `DoNavigateCommand`, freeing each returned string with `FreeMathCATString`. `MathExplorer` maps the browse keys to MathCAT's navigation commands.
