# Updates

Vox can tell you when a newer version is out and install it when you say so. It checks once a day, a minute after it starts, and says something only when there is an update. "Check for updates" in the Vox menu checks at once. Installing always asks first: Vox downloads the installer, checks it, starts it, and closes so it can be replaced.

**Settings, General, Check for updates**: Never, Releases (the default), or Releases and betas. The sign-in and lock screens never check.

## How an update is trusted

- The feed (`updates/feed.json` in this repository, read from `UpdateChecker.DefaultFeedUrl`) lists every release:

  ```json
  { "releases": [
    { "version": "1.2.0", "channel": "stable", "url": "https://.../Vox-1.2.0.msi", "sha256": "<64 hex digits>", "notes": "..." }
  ] }
  ```

  Installer addresses must be HTTPS.
- Beside it, `feed.json.sig` holds the base64 RSA-SHA256 signature of the feed's exact bytes, made with Vox's release key.
- Vox checks the signature with the public key in `assets/config/update-key.pem`, which ships with Vox. A feed that doesn't check out is ignored. **Without that file, Vox never checks for updates**, so builds without a release key never trust anything.
- A downloaded installer is run only if its SHA-256 is the one the signed feed gives. The MSI itself is also Authenticode-signed (`docs/installer.md`).

## Publishing a release

```bash
# once: the release key (keep release-key.pem secret; commit update-key.pem as assets/config/update-key.pem)
openssl genrsa -out release-key.pem 3072
openssl rsa -in release-key.pem -pubout -out update-key.pem

# per release: add the entry (sha256sum Vox-1.2.0.msi), then sign the feed
openssl dgst -sha256 -sign release-key.pem updates/feed.json | base64 -w0 > updates/feed.json.sig
```
