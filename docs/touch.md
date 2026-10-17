# Touch

On a device with a touch screen, Vox can speak what is under your finger and let you move around
with gestures. It is off by default. Turn it on with the Touch setting on the Mouse page of the settings (Insert+Ctrl+G).

While touch is on, Vox puts a transparent layer over the screen and takes all touch input. Touch
that Vox doesn't use is not passed on to the application under it, so turn touch off when you want
to use the screen without Vox. Vox in a signed install can cover elevated windows as well; an
unsigned build covers ordinary windows only. Touch is never available on the sign-in, lock and
"allow changes" screens.

## Gestures

Touching the screen says the object under your finger and nothing is activated by touching alone.

| Gesture | Outside browse mode | In browse mode |
| --- | --- | --- |
| Touch, or slide a finger | Say the object under the finger | The same, and the browse cursor moves there |
| Double-tap with one finger | Activate the last object touched | Activate |
| Flick right, flick left | Next, previous object | Next, previous item of the rotor |
| Flick down, flick up | Not used | Next, previous line |
| Two-finger tap | Stop speech | Stop speech |
| Two-finger double-tap | Pause or resume speech | Pause or resume speech |
| Two-finger flick down | Say All | Say All |
| Three-finger flick right, left | Not used | Next, previous rotor setting |
| Three-finger tap | Open the Vox menu | Open the Vox menu |

The **rotor** is a short list of what a flick moves by on a web page: headings, links, form fields,
landmarks or lines. Three-finger flicks choose it and Vox says the choice. It is kept for the
session.

## Changing the gestures

The gestures are in `assets/config/touch-keymap.json`, in the keymap format with a gesture where a
key would be. Your own `%APPDATA%/Vox/touch-keymap.json` is layered on top, and
`"command": "None"` removes a gesture. Input help (Insert+1) says the gesture and the command it is
bound to without running it.

## What isn't built yet

Moving the navigator object and browse cursor to a touched element, magnifier support
(`MagnifierFollowTouch`), passing unclaimed touch on to applications, and a touch table in the
keyboard reference. [The design](technical/touch.md) has the details.
