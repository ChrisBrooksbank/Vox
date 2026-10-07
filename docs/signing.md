# Signing Vox and UI access

Windows only lets a screen reader read and control **elevated** windows (Task Manager, installers,
anything "Run as administrator") if the screen reader is elevated too, or if it has **UI access**.
Running Vox elevated is a poor answer: everything it starts would be elevated as well. UI access is
what NVDA and Narrator use.

Windows grants UI access only when all of these hold:

1. The exe's manifest asks for it: `requestedExecutionLevel uiAccess="true"`.
2. The exe is Authenticode-signed with a certificate that chains to a trusted root.
3. The exe runs from a secure location: `C:\Program Files\...`, `C:\Program Files (x86)\...` or
   `C:\Windows\System32\...`.

An exe that asks for UI access without meeting 2 and 3 doesn't start at all. So:

- **Development builds** (no certificate configured) embed `src/Vox.App/app.manifest`, which does not
  ask for UI access. They run from anywhere; elevated windows are unreadable unless Vox runs as admin.
- **Signed builds** embed `src/Vox.App/app.uiaccess.manifest` and are signed after building:

  ```bash
  # certificate file
  dotnet build -c Release -p:VoxSigningCertificate=C:\certs\vox.pfx -p:VoxSigningPassword=...
  # certificate in the Windows certificate store
  dotnet build -c Release -p:VoxSigningCertificate=store -p:VoxSigningThumbprint=<sha1>
  ```

  `signtool.exe` must be on the PATH (Windows SDK), or pass `-p:VoxSignTool=<path>`. The timestamp
  server can be changed with `-p:VoxTimestampUrl=...`. Install the signed output under Program Files
  (the installer will do this).

At startup Vox logs whether it has UI access (`UIAccess.IsGranted`).

An EV code-signing certificate is recommended: it also avoids SmartScreen warnings on download.
