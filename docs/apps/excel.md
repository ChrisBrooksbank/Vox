# Microsoft Excel

Vox reads Excel through its object model as well as UI Automation, so it can say what the screen
alone doesn't show: formulas, comments and notes, filters, validation messages and titles.

## Moving between cells

Each cell you move to is said as its value, then its address: "42, B5". After it come, when they apply:

- "merged B5 to C5" for a merged cell;
- "has formula" (Applications settings: *Excel: say when a cell has a formula*);
- "has comment", "has note" (*Excel: say when a cell has a comment or note*);
- "filter on" on the heading of a filtered column;
- the cell's data-validation input message ("Amount: enter pounds").

Moving to another sheet says the sheet's name first. With several cells selected (Shift+arrows),
the selection is said instead: "A1 to C5 selected, 5 rows, 3 columns, 15 cells".

If Excel is busy (while you edit a cell, or with a dialog open) Vox says what UI Automation reports instead.

## Titles

Like JAWS's titles and NVDA's row and column headers, you can tell Vox which row holds a sheet's
column titles and which column holds its row titles. Moving to another column then says the column's
title first, and moving to another row says the row's title. Titles are kept per workbook and sheet
in `%APPDATA%\Vox\excel-titles.json` (only for the session on the secure screens).

## Keys

| Key | Command |
| --- | --- |
| Insert+F2 | Read the active cell's formula |
| Insert+Alt+C | Read the active cell's comment and note |
| Insert+Shift+C | Use the current row as the sheet's column titles (again on that row: clear) |
| Insert+Shift+R | Use the current column as the sheet's row titles (again on that column: clear) |
| Insert+Shift+G | Chart summary: title, type and series of the selected chart, or the sheet's first |

These keys only apply while Excel is in the foreground. Excel support can be turned off on the
Applications page of the settings dialog.

## To verify on Windows

- That Excel's cells take focus as UIA data items (`DataItem`) as assumed.
- That the window's native object model is found from the `EXCEL7` window, and the running
  object table is the fallback.
- That Shift+arrows raise selection events Vox sees (the selection is read on `ElementSelected`).
