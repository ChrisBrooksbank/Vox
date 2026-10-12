# Text recognition (OCR)

Vox can read text that isn't available to it any other way: text in pictures, scanned or untagged PDFs, and applications that draw their own controls. It uses the text recognition built into Windows (`Windows.Media.Ocr`), so everything is recognised on your computer and nothing is sent anywhere.

## Installing a recognition language

Windows recognises text in the languages whose optical character recognition feature is installed. Most Windows installations already have it for their display language. To add another: Settings, Time and language, Language and region, a language's Language options, then Optical character recognition. Without any, Vox says that text recognition is not available.

The language Vox recognises is the **Text recognition language** setting (Insert+Ctrl+G, Review page; `OcrLanguage` in `settings.json`). Automatic uses Vox's own language when Windows can recognise it, otherwise the languages of your Windows profile.

## Using it

| Key | What it does |
| --- | --- |
| Insert+R | Recognise the navigator object. While browsing a page: the graphic at the browse cursor, or else the page as it is shown. |
| Insert+Ctrl+R | Recognise the whole foreground window. |
| Insert+Ctrl+Enter | Click the recognised word at the review cursor. |

Vox says "Recognizing", then reads what it found. The review cursor is put at the start of the recognised text, so the review keys (keypad 7/8/9 by line, 4/5/6 by word, 1/2/3 by character; see the keyboard reference for laptop keys) read it again in place, and Insert+Ctrl+Enter clicks the word the review cursor is on (to press a button in an application that has no accessible controls, for instance). A braille display shows the review line, and its routing keys move the review cursor.

The review cursor leaves the recognised text, back to what its review mode reviews, when focus or the caret moves, when you change the review mode (Insert+keypad 7 and 1), or when the browse cursor moves.

### PDFs

When an untagged PDF (no headings or structure) or a PDF with no text at all (a scanned image) opens in Edge or Chrome, Vox says so and offers Insert+R. In browse mode Insert+R then recognises the part of the PDF that is on the screen; scroll the page and press it again for the next part.

## Privacy and secure screens

The screen is captured only when you press one of these keys, and only the area being recognised. The picture and the recognised text stay in memory and are never written to disk or the log. On the sign-in, lock and UAC screens (secure mode) Vox never captures the screen and says "Not available on this screen".

## How it works

- `IOcrEngine` (`WindowsOcrEngine`) recognises a `CapturedImage` (BGRA pixels plus the screen rectangle they came from) and returns an `OcrResult`: lines of `OcrWord`s with their screen rectangles. Small captures are enlarged two times first (`OcrScaling`); recognition reads small screen text much better enlarged, and positions are mapped back to the screen (`CapturedImage.ToScreen`).
- `IScreenCapture` (`GdiScreenCapture`, BitBlt from the screen) is unavailable in secure mode (`RunPolicy.IsSecure`).
- `IOcrTargetSource` (`UIAOcrTargetSource`) finds what to capture: the navigator object's bounding rectangle (`INavigatorObject.GetBounds`), the foreground window's rectangle, or a page element's (`BrowseDocumentTracker.ElementBoundsAsync`, scrolled into view first). `BrowseModeController.RecognitionTarget` picks the graphic at the browse cursor, else the page.
- `OcrDocument` is the result as an `ITextDocument` (one line per recognised line; Chinese and Japanese words joined without spaces) that keeps each word's rectangle (`WordAt`).
- `OcrCommands` runs the commands; `ReviewCommands.ShowRecognizedAsync` puts the review cursor in the result and `RecognizedWordAsync` finds the word to click. A newer recognition cancels one still running.
