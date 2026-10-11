# Microsoft Word

Vox reads a Word document as it reads any text: by line, word and character as the caret moves,
with spelling errors. The Word module adds what needs Word's object model.

## Quick navigation

The document takes letters as typing, so Word's quick navigation keys use the screen reader key
and Alt (add Shift to go back). The caret moves to what is found, which is then read.

| Key | Moves to the next |
| --- | --- |
| Insert+Alt+H | heading ("Results, heading level 2") |
| Insert+Alt+T | table ("table with 3 rows, 4 columns", then the first cell) |
| Insert+Alt+L | list ("list with 5 items", then the first item) |
| Insert+Alt+C | comment ("Comment by Ann: check this figure") |
| Insert+Alt+R | tracked change ("Insertion by Bob: new words") |
| Insert+Alt+S | spelling error ("teh, misspelled") |

With Shift (Insert+Alt+Shift+H ...) each moves to the previous one. Moving between comments is
also how to go through the document's comments in order (the Elements List is for web pages).

## Comments and tracked changes at the caret

When the caret moves into a comment or a tracked change it wasn't in before, it is said after the
text: "Deletion by Bob: old words", "Comment by Ann: why?". Turn this off on the Applications
page of the settings dialog (*Word: say comments and tracked changes at the caret*).

## Location and tables

| Key | Command |
| --- | --- |
| Insert+Delete | Page (of how many), section, line and column of the caret |
| Ctrl+Alt+Left / Right | Previous / next cell in the row, with the column's header (from the first row) |
| Ctrl+Alt+Up / Down | Cell above / below, with the row's header (from the first column) |

At the edge of a table Vox says "Start of row", "End of row", "Top of column" or "Bottom of column".

## To verify on Windows

- Word's object model is found from the `_WwG` document window.
- After quick navigation moves the caret, Word's caret event may also read the line; check the
  two don't talk over each other.
- `Selection.GoTo` with `wdGoToHeading` finds headings as expected, and `NextRevision` doesn't wrap.
