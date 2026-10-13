# Vox sample add-on

A small add-on that shows each part of the add-on API (`src/Vox.Sdk`, documented in
[docs/addons.md](../../docs/addons.md)):

- a global command with a key: **Insert+Ctrl+F12** says hello and how many times focus moved;
- an app module for Notepad with its own command: **Insert+Alt+F12** says how many words the text has
  (counted on a background task given the host's `Stopping` token, then said through `RunOnPipeline`);
- an announcement filter that says "e.g." as "for example";
- a global plug-in that counts focus changes.

Build and package it (CI does the same and keeps the package as a build artifact):

```bash
dotnet build samples/Vox.SampleAddOn -c Release
dotnet src/Vox.App/bin/Release/net9.0-windows10.0.19041.0/Vox.App.dll --pack-addon \
  samples/Vox.SampleAddOn/bin/Release/net9.0-windows10.0.19041.0 vox.sample.voxaddon
```

Add a private key (`--pack-addon <folder> <package> key.pem`) to sign it. Install the package
from the Vox menu: Manage add-ons, Install from file.
