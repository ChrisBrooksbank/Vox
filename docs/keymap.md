# Your own keys

Vox's keys come from `assets/config/default-keymap.json` (and `laptop-keymap.json` for the laptop layout). To change them, put your own bindings in `%APPDATA%\Vox\keymap.json`; they are layered on top when Vox starts or the keyboard layout changes.

```json
{
  "bindings": [
    { "modifiers": "None",         "vkCode": 74, "mode": "Browse", "command": "NextHeading" },
    { "modifiers": "Insert|Shift", "vkCode": 72, "mode": "Any",    "command": "SayTime" },
    { "modifiers": "None",         "vkCode": 81, "mode": "Browse", "command": "None" }
  ]
}
```

- Each binding replaces the standard one on the same keys and mode. `"command": "None"` takes a key away from Vox, so it reaches the application again.
- `modifiers` combine `Insert` (the Vox key, Insert or Caps Lock), `Ctrl`, `Alt` and `Shift` with `|`; `vkCode` is the Windows virtual-key code; `mode` is `Browse`, `Focus` (both only on web pages) or `Any`. Command names are those in `NavigationCommand` (see `CommandCatalog` for what each does).
- Problems go to the log (`%APPDATA%\Vox\logs`): two bindings of yours on the same key (the last one is used), a standard command left with no key, and a file that can't be read (it is then ignored and the standard keys apply).

The sign-in and lock screens always use the standard keys.
