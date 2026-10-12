# Remote Desktop and Citrix

## Where Vox must run

A screen reader reads the applications on the computer it runs on. To read applications in a
Remote Desktop (RDP, Azure Virtual Desktop, Windows 365) or Citrix session, **Vox must run inside
that session**, on the remote computer, with its speech coming back through the session's audio
redirection. A Vox on the local computer only sees the remote session as one window.

Vox finds out at startup what kind of session it is in (`RemoteSessionDetector`: Windows' remote
session flag, the session's client protocol — 1 Citrix ICA, 2 RDP — and the session name). In a
remote session it:

- logs it ("Remote session: Remote Desktop from LAPTOP-7") and says it once: "Remote session:
  Remote Desktop";
- turns off keeping the audio device awake, which would otherwise stream silence over the
  connection all the time.

## Known limits

- **Latency:** speech is played remotely and sent back as audio, adding the connection's delay to
  every key press. Use a fast connection and the client's "play on this computer" audio setting.
- **Keyboard:** the remote session gets the keys only when its window has focus, and the client
  may keep some (Windows key combinations unless "apply Windows key combinations" is set to "on
  the remote computer"). Insert and Caps Lock as the Vox key both work remotely.
- **Two screen readers:** a local screen reader also hears the keys first. Put it to sleep for the
  remote desktop client (Vox: Insert+Shift+S) or use its remote-access add-on.
- **UI Automation** works inside the session as at the computer; providers are slower when the
  host is busy, and Vox's timeouts (`docs/` robustness) report "not responding" rather than hang.
- **Braille:** a USB HID braille display can be redirected by RDP's USB redirection or Citrix's
  generic USB; BRLTTY can run on the host. Local displays aren't otherwise seen.
- **Secure screens:** the remote sign-in screen is the client's, not read by the remote Vox.

## Manual test checklist (UIA over RDP)

1. Connect with Remote Desktop to a Windows 11 host with Vox installed; start Vox in the session.
   It says "Remote session: Remote Desktop".
2. Tab through Notepad, File Explorer and Settings: each focus change is said once, promptly.
3. Type in Notepad: characters and words are echoed; arrow keys read the line.
4. Open a web page in Edge: browse mode loads, H moves between headings, Insert+Down reads it all.
5. Minimize and restore the session window; disconnect and reconnect: Vox keeps speaking.
6. Repeat 1–4 through Citrix Workspace if available ("Remote session: Citrix").
7. Check the log (`%LOCALAPPDATA%\Vox\logs`) for the session line and no repeated UIA timeouts.
