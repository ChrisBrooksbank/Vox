# Microsoft Outlook

The Outlook module is for classic Outlook (`OUTLOOK.EXE`). The new Outlook (`olk`) shows its
mail as web content, which Vox reads in browse mode like any web page.

## The message list

Each message is read from Outlook's object model: its statuses, then its columns in your order.
"Unread, flagged, has attachment, John Smith, Lunch, 10:42". Meeting requests say so, with your
answer so far: "meeting request, not answered". On the Applications page of the settings dialog:

- *Outlook: message list columns* — which columns are read and in what order (from, subject,
  received, size, categories);
- *Outlook: say message statuses* — first, last, or not at all;
- *Outlook: read the selected message* — read the message itself after its row (the reading
  pane's auto-read), once each time another message is selected.

## Keys

| Key | Command |
| --- | --- |
| Insert+Alt+1 to 6 | From, To, Cc, Subject, Date, Attachments of the open or selected message |
| Insert+Alt+I | Meeting request: time, place and your answer so far |
| Insert+Alt+A | Accept the meeting request (press twice: the answer is sent at once) |
| Insert+Alt+T | Tentatively accept (press twice) |
| Insert+Alt+D | Decline (press twice) |
| Insert+Alt+8 | Today's appointments |
| Insert+Alt+9 | The next 7 days' appointments |

Appointments are said with their time, subject, place and organizer.

## To verify on Windows

- That message list rows take focus as list or data items whose name contains the subject
  (the row is only replaced when it does, so other lists keep their usual announcement).
- That `Items.Restrict` takes dates in the system's short format, as written.
- That answering with `Respond(..., true)` then `Send()` sends without a dialog.
