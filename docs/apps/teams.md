# Microsoft Teams

New Teams (`ms-teams`) is a web application: Vox reads it in browse mode, where I and Shift+I
move between the messages of a chat, and Teams' own keys work in focus mode.

## Keys

| Key | Command |
| --- | --- |
| Insert+Alt+1 to 5 | Read the latest message, or the 2nd to 5th from the latest |
| Insert+Alt+M | Call state: in a call, microphone, camera, hand raised, sharing |
| Ctrl+Shift+M, Ctrl+Shift+O, Ctrl+Shift+K | Teams' own mute, camera and raise hand keys; after them Vox says what changed ("Microphone muted") |
| Insert+Ctrl+Shift+M | Mutes or unmutes the microphone from any application: Teams' own button here, else the default microphone |

In Teams, Insert+Alt+M is the call state, not math exploring.

## New messages

Teams announces new messages itself. On the Applications page of the settings dialog,
*Teams: say new messages* chooses all of them, only those that mention you, or none. When Teams
says the same thing twice in a row, or says what Vox has just said, it is said once.

## To verify on Windows

- The wording of Teams' new-message announcements (Vox recognizes "New message", "replied",
  "sent a message", "mentioned you").
- That the conversation is the longest list named like "Chat content" or "Messages".
- That the call buttons are named "Mute"/"Unmute", "Turn camera on/off", "Raise/Lower hand", "Leave".
