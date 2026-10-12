# Remote access

Two people with Vox can share one computer: a trainer can control a student's computer and hear
it, or a colleague can help. Nothing else is needed than Vox on both computers and a way for them
to reach each other: directly, or through a relay server when routers and firewalls are in the way.

## Using it

On the computer to be controlled: Vox menu, **Remote access**, "Allow this computer to be
controlled", Start. Vox says a new **session code** (12 letters and digits, `XXXX-XXXX-XXXX`) and
puts it on the clipboard; read or send it to the other person. Vox waits.

On the controlling computer: Vox menu, Remote access, "Control another computer", type the other
computer's address (directly) or the relay server's, and the code; Connect.

Both sides then say that the session started. On the controlling computer:

- **Insert+F11** switches between sending your keys to the other computer and using your own.
  While sending, every key goes there (including the Vox key, which becomes the other computer's
  own Vox key, Insert or Caps Lock), except Insert+F11 itself.
- What the other computer says is spoken here too, and shown on your braille display; while you
  control it, its braille line is shown as well.
- **Insert+Ctrl+F11** (either computer) ends the session, as does Remote access again. Both
  sides say that it ended; any key still held down on the controlled computer is released.

The dialog remembers the relay, the last address and the port to wait on.

## Direct or through a relay

**Directly**, the controlled computer listens on a port (6837 by default; Windows may ask to allow
it through the firewall) and the controlling one connects to its address (`name`, `name:port`,
an IP address, `[IPv6]:port`). This works on a local network or over a VPN.

**Through a relay**, both computers connect out to a relay server, which pairs them. Anyone can
run one:

```bash
Vox.App --remote-relay [port]          # default 6837; Ctrl+C stops it
```

The relay only ever sees a *channel* (a slow PBKDF2 hash of the session code, so the code can't
be worked out from it) and encrypted frames: it can't read, change or join the session.

## Security

- The session is encrypted end to end (`SecureChannel`): an ephemeral ECDH (P-256) key exchange,
  mixed with a key derived from the session code (PBKDF2-SHA256, 100,000 rounds, salted with both
  sides' random nonces) and the handshake transcript (HKDF-SHA256). Only someone who knows the
  code gets the keys, and a recorded session can't be decrypted later even with the code. Each
  side proves it has the keys before anything else is sent.
- Messages are AES-256-GCM frames with a key per direction and a counter that must go up by one,
  so they can't be changed, replayed, reordered or sent back.
- The code is 60 random bits, new for each session. Wrong codes are said on the controlled computer,
  slowed down (3 s each) and, after 5, it stops waiting.
- Nothing is logged of what is said or typed. Remote access is never available on the sign-in,
  lock or UAC screens, and not while the network is off (`DisableNetwork` policy); it connects only
  through `NetworkGate`.

## What it can't do

- Keys are sent with `SendInput`, so they don't reach elevated windows unless the controlled Vox
  has UI access ([Signing Vox and UI access](signing.md)), nor the secure desktop (Ctrl+Alt+Del, UAC prompts).
- Braille on the controlled side follows its own display; without one, only speech comes back.
