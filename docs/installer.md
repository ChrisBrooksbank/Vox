# Installer

`installer/Vox.Installer` is a WiX Toolset v5 project that builds `Vox.msi`. It installs per machine to `C:\Program Files\Vox`, the secure location Windows requires before it grants UI access (`docs/signing.md`), and adds:

- every file of a published Vox: `Vox.App`, `Vox.Watchdog` and `Vox.Service`, self-contained, so no .NET runtime has to be installed first;
- the `VoxService` Windows service (LocalSystem, automatic start), which reads the sign-in, lock and UAC screens;
- a Start menu and a desktop shortcut to `Vox.Watchdog.exe` (which starts Vox and restarts it after a crash). The Start menu shortcut has the hotkey **Ctrl+Alt+V**, so Vox can be started without seeing the screen;
- with `STARTATLOGON=1`, a shortcut in the all-users Startup folder, so Vox starts at every sign-in.

Uninstalling removes all of these and stops the service. Users' settings in `%APPDATA%\Vox` are kept.

## Building

The installer builds only on Windows, so it isn't part of `Vox.sln`. Use the **Release** workflow (Actions tab, "Run workflow", give a version), or locally:

```powershell
foreach ($p in 'src/Vox.App', 'src/Vox.Watchdog', 'src/Vox.Service') {
  dotnet publish $p -c Release -r win-x64 --self-contained true -o publish\Vox
}
dotnet build installer/Vox.Installer -c Release -p:PublishDir="$PWD\publish\Vox" -p:VoxVersion=1.0.0
```

With the repository secrets `VOX_SIGNING_CERTIFICATE` (the .pfx, base64) and `VOX_SIGNING_PASSWORD`, the workflow signs the three programs (so Vox gets UI access) and the MSI.

The project has only been checked on paper so far. WiX doesn't run on Linux, where it was written, so the first Windows build of the Release workflow is its first real test.

## Installing

```powershell
msiexec /i Vox.msi                       # with the setup dialog
msiexec /i Vox.msi /qn STARTATLOGON=1    # silently, starting Vox at every sign-in
```
