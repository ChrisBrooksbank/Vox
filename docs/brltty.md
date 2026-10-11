# BRLTTY (optional braille component)

Vox drives most current braille displays itself through the Windows HID braille standard. Older
serial and USB displays that don't speak HID braille can be used through
[BRLTTY](https://brltty.app), which has drivers for almost every display ever made. BRLTTY is
installed separately (it is LGPL/GPL and runs as its own service); Vox talks to it over BrlAPI,
BRLTTY's client protocol, which Vox implements itself, so no BRLTTY DLL is needed.

## Installing BRLTTY for Windows

1. Download the Windows build from <https://brltty.app/download.html> (the `brltty-win-*.zip`
   with libusb, or the installer) and extract or install it, for example to
   `C:\Program Files (x86)\BRLTTY`.
2. Install the display's USB driver if it needs one (the `LibUSB-1.0` or `LibUSB-Win32` drivers
   that come with BRLTTY, or the manufacturer's serial-over-USB driver).
3. Choose the display: edit `etc\brltty.conf` in the BRLTTY folder and set `braille-driver`
   (for example `bm` for Baum, `eu` for EuroBraille, `ht` for Handy Tech) and `braille-device`
   (`usb:`, `serial:COM3`, `bluetooth:...`). Leave `screen-driver` as `no`: Vox gives BRLTTY
   what to show.
4. BrlAPI is on by default and listens on the local machine as display `:0` (TCP port 4101).
   Leave it that way: don't disable the API (`-N`) or move it with `api-parameters host=...`.
5. Install BRLTTY as a service so it starts with Windows: from an administrator command prompt in
   the BRLTTY folder run `bin\brltty -I` (BRLTTY's "install service" option; the package's
   `install-service` script does the same), then start the "BrlAPI" service from Services. To try
   it first, run `bin\brltty -n -e` in a console instead (no daemon, log to the console).

## Authentication and the key file

BRLTTY normally asks BrlAPI clients for the contents of its key file, `brlapi.key`, which it
creates in its `etc` folder. Vox looks for it, in order:

1. the file named by the `BRLAPI_AUTH` environment variable (`keyfile:` prefix allowed),
2. `%ProgramFiles(x86)%\BRLTTY\etc\brlapi.key`,
3. `%ProgramFiles%\BRLTTY\etc\brlapi.key`,
4. `C:\brltty\etc\brlapi.key`.

If BRLTTY is installed elsewhere, set `BRLAPI_AUTH` to the key file's full path. A server
started with `api-parameters auth=none` needs no key at all.

## Using it with Vox

Set the braille display driver to BRLTTY in Vox's braille settings (`BrailleDisplayDriver`
`"BRLTTY"`), or leave it on automatic: auto-detect tries HID braille displays first and then
BRLTTY, and uses BRLTTY whenever its server answers. Vox connects within a second, takes over
the display (BrlAPI "tty mode"), and reconnects when BRLTTY restarts. While Vox has the display,
BRLTTY's own screen reading is paused; when Vox exits it hands the display back.

Display keys arrive already resolved by BRLTTY's key tables, so they keep the bindings that
BRLTTY documents for each display. Vox's braille key maps see these key names:

| BRLTTY command | Vox key name |
| --- | --- |
| LNUP / LNDN | `lineUp` / `lineDown` |
| FWINLT / FWINRT | `panLeft` / `panRight` |
| HWINLT / HWINRT | `halfPanLeft` / `halfPanRight` |
| TOP / BOT | `top` / `bottom` |
| LNBEG / LNEND | `lineBegin` / `lineEnd` |
| HOME / BACK / RETURN | `home` / `back` / `return` |
| CSRTRK | `toggleTether` |
| PRPGRPH / NXPGRPH | `previousParagraph` / `nextParagraph` |
| ROUTE (routing keys) | `routing`, with the cell |
| PASSDOTS (braille keyboard) | `dots` plus the dots, as `dots125`; `space` first when the space bar was in the chord; the space bar alone is `space` |
| keyboard keys | `key_` and the X key name: `key_BackSpace`, `key_Return`, `key_Tab`, `key_Escape`, `key_Left`, `key_F1` |
| typed characters | `char_` and the character: `char_a` |

Modifiers come first as their own names (`shift`, `control`, `meta`, `altGr`, `gui`, `upper`).
Other commands are named in `src/Vox.Core/Braille/Brltty/BrlApiKeyCodes.cs`; unknown ones are
`cmd<number>`.

## How it works

`BrlttyDisplay` (`Vox.Core/Braille/Brltty`) opens TCP port 4101 on `127.0.0.1`, answers the
server's VERSION packet (protocol 8), authenticates (key file, or none), asks for the driver name
and display size, and enters tty mode with an empty tty path and no driver name, so keys come as
BrlAPI commands. Cells are sent as WRITE packets the way brlapi's `writeDots` does (a region of
blank text, an AND mask of zeros and the dots as the OR mask), only for the cells that changed,
with BRLTTY's cursor off because Vox draws its own. A reader thread turns KEY packets into
`KeyPressed` events; when the connection drops the driver reports `Disconnected` once and
`BrailleDisplayManager` tries to connect again. The packet encoders are in `BrlApiProtocol.cs`
and the key code tables in `BrlApiKeyCodes.cs`.

Vox doesn't start or configure BRLTTY; it only connects to a server that is already running.
