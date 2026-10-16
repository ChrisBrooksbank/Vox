# Magnifier

Vox has a magnifier for people who use some sight as well as speech. It enlarges the screen up to 16 times, follows what you are working on (the focus, the caret, the browse cursor, the review cursor and the mouse), and can change the screen's colours. It uses the Magnification API built into Windows, so there is nothing else to install.

The first-run wizard asks "Do you use magnification?"; press 1 there to turn it on. You can also turn it on at any time with its key, or on the Magnifier page of Settings.

## Keys

| Key | What it does |
| --- | --- |
| Insert+Alt+Z | Turn the magnifier on or off |
| Insert+Alt+= (equals) | Zoom in (turns the magnifier on when it is off) |
| Insert+Alt+- (minus) | Zoom out |
| Insert+Alt+F | Next colour filter: inverted, greyscale, inverted greyscale, yellow on black, none |
| Insert+Alt+Shift+Z | Next view: full screen, docked, lens |
| Insert+Alt+Shift+D | Move the docked view to the next edge of the screen |
| Insert+Alt+Shift+= / - | Make the docked view or the lens larger or smaller |
| Insert+Alt+Shift+arrows | Pan the magnified view |

The same keys work in the desktop, laptop and JAWS layouts (Insert is whichever key you use as the Vox key). Change them in Input gestures, under Magnifier. The zoom moves through 1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4, 5, 6, 8, 10, 12, 14 and 16 times; Vox says each zoom, and plays the edge sound at 1 and 16 times or when the view is already at the edge of the screen.

## Views

- **Full screen** magnifies the whole screen.
- **Docked** shows the magnified view in a strip along one edge of the screen (the top, at first) and leaves the rest of the screen as it is. Windows make room for it, as they do for the taskbar. Insert+Alt+Shift+D moves it to the next edge; Insert+Alt+Shift+= and - make it deeper or shallower (10 to 80 percent of the screen).
- **Lens** is a window that sits over what is being followed (the mouse pointer, or the focus as you move with the keyboard) and magnifies what is under it. Insert+Alt+Shift+= and - make it larger or smaller.

The docked and lens windows let mouse clicks through to what is under them, and never take the focus.

## What it follows

The view moves to what you are working on. Each of these has a setting on the Magnifier page, all on at first:

- **The focus**: the newly focused control is put in the middle of the view, unless the view already shows it.
- **The caret**: the view moves only as far as it must to keep the caret in sight, with a little room around it, so it doesn't jump with each character typed.
- **The browse cursor**: on a web page, the element the browse cursor is on (scrolled into view first).
- **The review cursor**: the text the review cursor is on.
- **The mouse**: the view moves when the pointer nears its edge.

A move made from the keyboard wins over the mouse for a second, so a hand resting on the mouse can't pull the view away from where the keyboard just took it. The view glides to where it is going; turn **Glide to what is followed** off to make it jump there instead.

## Colour filters

Insert+Alt+F goes through the filters: **inverted colours** (light text on a dark background), **greyscale**, **inverted greyscale** and **yellow on black** (high contrast: dark text becomes yellow, a light page black), then back to none. Settings has the same choice, and **Smooth edges of magnified text** turns off smoothing for a sharper, blockier view.

## Settings and profiles

The Magnifier page of Settings (Insert+Ctrl+G) has every setting: on or off, the zoom, the view, the docked edge and size, the lens size, the colour filter, smoothing, gliding and what is followed. They are ordinary settings (`Magnifier…` in `settings.json`), so a [configuration profile](profiles.md) can change them for one application: zoom in further in a spreadsheet, or turn the magnifier on only in one program. While a profile is in use, the zoom and the other changes you make with the keys go into it.

## When the magnifier can't be used

- **Windows Magnifier is running**: two magnifiers would fight over the screen, so Vox says "Windows Magnifier is running" and doesn't start its own. Close Windows Magnifier (Windows+Escape) and press Insert+Alt+Z again.
- **The sign-in, lock and UAC screens** (secure mode) have no magnifier for now. Windows Magnifier works there.
- **Your organization has turned it off** with the `DisableMagnifier` policy ([Deploying Vox in an organization](enterprise.md)). Its keys then say so.

When Vox exits or stops unexpectedly, Windows puts the screen back as it was.

## How it works

- `MagnifierViewport` (pure) is the view as maths: the zoom (1 to 16), the part of the screen magnified and its size (the primary display for full screen, the window for the docked and lens views), kept inside the virtual screen across every monitor. It centres on a rectangle (`CentreOn`, showing the start of a target too large for the view), keeps one in view with a margin (`KeepInView`) and pans by a quarter of the view (`PanStep`). `MagnifierLayout` places the docked and lens windows, `MagnifierZoom` holds the zoom steps and `MagnifierColorMatrices` the filters' 5×5 colour matrices.
- `MagnifierController` runs the commands and the tracking. It turns the magnifier on when `MagnifierEnabled` says so (and off again), follows settings changes (so profiles work), holds the mouse off for `MouseHoldOff` after a keyboard move and glides with a timer from an injectable `TimeProvider`. It sends `MagnifierFrame`s (view, zoom, source origin, window, filter, smoothing) to an `IMagnifier`. `ScreenCurtainActive` is how the screen curtain (still to come) will turn it off: the curtain wins, the magnifier says so and stops, and comes back with the curtain gone if its setting still says on. `IMagnifierOffer` is its first-run wizard question.
- `WindowsMagnifier` is the `IMagnifier`: on a thread of its own (per-monitor DPI aware, so in physical pixels like UI Automation), `MagSetFullscreenTransform` and `MagSetFullscreenColorEffect` for full screen, and for the docked and lens views a topmost, click-through window holding a Magnification control (`MagSetWindowSource`, `MagSetWindowTransform`, `MagSetColorEffect`), redrawn 60 times a second; the docked window is an app bar. It refuses to start while `Magnify.exe` runs.
- `MagnifierFeed` gives the controller where things are: the focus's bounds from the focus event (`FocusChangedEvent.Bounds`, cached with the focus), the caret from the system caret (`Win32Caret.GetCaretBounds`) after caret events and shortly after typing, the browse cursor's element from `BrowseDocumentTracker.ElementBoundsAsync` (one lookup at a time, the newest element winning), the review cursor from `ITextRange.GetBounds` and the mouse from `GetPhysicalCursorPos`. Nothing is looked up for a source that isn't followed. `Vox.App/MagnifierService` wires it to the pipeline.
- Not in secure mode, or with `DisableMagnifier` (`RunPolicy.AllowMagnifier`).
