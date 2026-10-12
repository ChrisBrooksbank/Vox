# Beta programme

Vox is tested by the people who use it: blind and low-vision screen reader users who run it on
their own computers, with their own apps and web sites, and tell us where it falls short. This page
says how to join, what to test, and how to send a report we can act on.

## Joining

1. Install Vox with the installer, as [Installing Vox](install.md) describes. The installer puts it
   under Program Files, which is what lets Vox read elevated windows
   ([Signing Vox and UI access](signing.md)).
2. Open the settings (Insert+Ctrl+G), General, and set **Check for updates** to **Releases and
   betas**. Vox then offers beta builds as well as releases, and always asks before it installs one
   ([Updates](updates.md)).
3. Keep another screen reader you trust installed. A beta can have bugs that leave you without
   speech; Vox.Watchdog restarts Vox after a crash, but you should always have a way back.

To leave the beta, set **Check for updates** back to **Releases**. Vox offers the next release when
it is newer than the beta you have.

## What to test

Use Vox for what you normally do, and tell us when it gets in the way. We especially want to hear
about:

- web pages that read wrongly or not at all in browse mode (Edge, Chrome, Firefox);
- forms, menus, dialogs and editors that stay in or out of focus mode when they shouldn't;
- desktop apps, including Word, Excel, Outlook, Teams, Explorer and Settings;
- braille displays ([Braille](braille.md));
- anything Vox says too much, too little, too late, or twice.

Each beta's release notes (in the update feed and on the release page) say what changed and
what to try first.

## Sending a report

Feedback goes to the repository's issues: <https://github.com/ChrisBrooksbank/Vox/issues/new/choose>.
Pick **Screen reader bug report**. The form is a list of headings and plain text fields, so press H
to move from one to the next. For ideas rather than bugs, pick **Suggestion**.

Security problems are the exception: report them privately ([Security review](security.md)), not as an issue.

A good report lets us hear what you heard. The form asks for these:

### Speech viewer output

What Vox said, word for word, is the most useful thing you can send.

1. Turn on the speech viewer with Insert+Shift+V (or Speech viewer in the Vox menu, Insert+N). It is
   a small window at the top right of the screen that shows everything Vox says; it never takes
   focus.
2. Reproduce the problem.
3. Copy what Vox said:
   - with sighted help, select the text in the speech viewer with the mouse and copy it;
   - on your own, press Insert+Shift+F11 to step back through the last 100 things Vox said
     (Insert+Shift+F12 steps forward again). Pressing it twice quickly copies the one you are on to
     the clipboard; paste each into the report in order, oldest first.

### Developer info

Put focus on the control the problem is about and press **Insert+F1**. Vox reads its name, control
type, ARIA role, framework, class, automation id, process and runtime id, and copies them to the
clipboard; paste them into the report. For a web page, add the page's address too (Insert+K reads
the address of the link at the browse cursor; the address bar, Alt+D, has the page's own).

### Version and setup

- Vox's version: Settings, Apps, Installed apps, the Vox entry; or the installer's file name.
- Windows version: Windows+R, `winver`, Enter.
- The app or browser, with its version.
- Speech engine and voice, braille display, keyboard layout (desktop or laptop), and which add-ons
  you have. If you have add-ons, try **Restart with add-ons disabled** in the Vox menu: if the
  problem goes away, name the add-on.

### Log (optional)

Vox writes a log a day to `%LOCALAPPDATA%\Vox\logs` (type that into the Windows+R box), keeping the last
seven. Attach the newest file, or paste the part from around the time of the problem. A portable copy
keeps its logs in the `userConfig\logs` folder beside it.

## Privacy

Vox sends nothing about you anywhere unless you send it: there is no telemetry. A report holds only
what you paste into it, and issues are public, so read before you send:

- Speech viewer output is what Vox said, which can include the text of messages, documents and
  pages you were reading. Remove anything private.
- Developer info holds the control's name, which can be private text (an email subject, a file
  name).
- The log never holds what you typed, what Vox said, field values or recognised text
  (`SensitiveTextLoggingTests` checks this), but it does name windows, applications and the
  elements Vox had trouble with.

## For maintainers

- Label beta reports `beta` (the form does this) and reply within a week, even if only to say the
  report was reproduced.
- Ask for the speech viewer output before guessing: most "Vox says the wrong thing" reports are
  found from it alone.
- Turn a reproduced web report into a page in the test corpus (`tests/pages/`, [End-to-end tests](e2e.md)) and a
  transcript test, so the fix stays fixed.
- Before a beta is offered: CI green, the end-to-end suite and soak test run ([End-to-end tests](e2e.md)), the
  benchmarks compared with the last release ([Benchmarks](benchmarks.md)), and the security checklist
  ([Security review](security.md)) walked for anything the release changed.
