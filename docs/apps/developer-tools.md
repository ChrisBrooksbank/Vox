# Developer tools, Zoom and Acrobat

## Visual Studio Code

VS Code's editor is read in focus mode (its editor is a web application). VS Code in screen
reader optimized mode plays its own audio cues and announces things itself ("Line 12", "No
results"); when it announces what Vox has just said, or says the same thing twice within two
seconds, it is said once. VS Code's accessible view (Alt+F2) is an ordinary text area Vox reads.
To make every VS Code document start in browse mode instead, set `AppDefaultModes` to
`{ "Code": "Browse" }` in your settings.

## Visual Studio

Its notifications that repeat what Vox has just said are dropped, as for VS Code.

## Zoom

| Key | Command |
| --- | --- |
| Insert+Alt+M | Call state: in a call, microphone, video, hand raised, sharing |
| Alt+A, Alt+V, Alt+Y | Zoom's own mute, video and raise hand keys; after them Vox says what changed |

## Adobe Acrobat

Acrobat's notifications that repeat what Vox has just said are dropped. Acrobat shows PDF
documents through MSAA rather than as a web document, so browse mode doesn't apply to them yet;
open tagged PDFs in Edge, where Vox browses them like web pages (headings, lists, tables), or
use Acrobat's own Read Out Loud.

Each of these can be turned off on the Applications page of the settings dialog.
