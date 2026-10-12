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

## Signing releases with SignPath

Vox is applying for free open-source signing from the [SignPath Foundation](https://signpath.org/):
the certificate belongs to SignPath Foundation (shown as the publisher), the private key stays
with SignPath, and every release is approved by hand in SignPath before it is signed. The README's
"Code signing policy" section and the [privacy policy](privacy.md) are part of that agreement; keep
the team roles there up to date. Every Vox assembly says its product is "Vox" (`src/Directory.Build.props`)
and the Release workflow gives them all the release's version, as SignPath requires.

The Release workflow signs through SignPath when it has the token and the organization ID, and
there is no `VOX_SIGNING_CERTIFICATE` (which wins). It builds Vox.App with `-p:VoxUiAccess=true`
(the UI-access manifest without signing in the build), sends Vox's own exes and `Vox.*.dll` (never
third-party libraries) to be signed, builds the MSI from the signed files, then sends the MSI. Each
request waits up to 30 minutes for your approval in SignPath.

Once SignPath has accepted the project:

1. In SignPath, add the predefined **GitHub.com** trusted build system to the organization and link
   it to the project.
2. Create two artifact configurations in the project, named `programs` and `installer`, from
   [.signpath/artifact-configurations](../.signpath/artifact-configurations).
3. Create a CI user with submitter rights on the signing policy and an API token for it.
4. In GitHub, Settings, Secrets and variables, Actions:
   - Secret `SIGNPATH_API_TOKEN`: the token.
   - Variable `SIGNPATH_ORGANIZATION_ID`: the organization ID (a GUID, on the organization's
     settings page).
   - Variable `SIGNPATH_PROJECT_SLUG` if the project's slug isn't `Vox`, and
     `SIGNPATH_SIGNING_POLICY_SLUG` if the policy's isn't `release-signing`.
5. Run the Release workflow and approve the two signing requests in SignPath when they appear.
