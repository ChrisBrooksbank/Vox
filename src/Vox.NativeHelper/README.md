# Vox.NativeHelper

C++/CLI helper that reads a Firefox document's IAccessible2 tree and returns it as JSON
(`Vox.NativeHelper.IA2Capture.CaptureDocument`), which `Vox.Core.Accessibility.IA2SnapshotReader` reads into
a COM-free `IA2Node` tree. No IA2 object reaches managed code. See `docs/firefox.md`.

- Windows only; needs Visual Studio's "C++/CLI support for v143 build tools" component. Not in `Vox.sln`:
  `msbuild src/Vox.NativeHelper/Vox.NativeHelper.vcxproj -p:Configuration=Release -p:Platform=x64`.
- `ia2_api_all.h` and the IA2 proxy/stub come from the IAccessible2 SDK; CI fetches them into `third_party/ia2`.
- Vox falls back to the UIA path when the DLL or the interface is unavailable.
