# Microsoft PowerPoint

## Slides

When you move to another slide (in the slide list, or with Page Up/Down), Vox says its number
and title after the focus: "Slide 3 of 10, Quarterly results". In a slide show each slide is read
as it appears: number, title, then the text of each shape (and the alternative text of pictures).

## Keys

| Key | Command |
| --- | --- |
| Insert+Alt+Page Down | Select the next shape on the slide and read it |
| Insert+Alt+Page Up | Select the previous shape |
| Insert+Alt+N | Read the slide's speaker notes |
| Insert+Alt+S | Read the whole slide |

A shape is read as its name, kind, text (or alternative text) and position: "Picture 3, picture,
Sales chart, 3 of 4".

## To verify on Windows

- That the slide is found in `ActiveWindow.View.Slide`, and in a slide show in `SlideShowWindows(1).View.Slide`.
- That 150 ms after a key is enough for PowerPoint to have changed slide.
