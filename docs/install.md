# Installing Vox

This page takes you through installing Vox using only the keyboard and speech. Every step names
the keys to press. It takes about five minutes.

## Before you start

- You need **Windows 11**, 64-bit. Vox reads web pages in **Microsoft Edge** or **Google Chrome**
  (version 126 or later) best. Firefox works, with some limits.
- You don't need to install .NET or anything else first. The installer has everything Vox needs.
- Until Vox is installed you need another screen reader to hear the installer. **Narrator** is
  built into Windows: press **Ctrl+Windows+Enter** to turn it on, and the same keys again to turn
  it off. NVDA or JAWS work just as well.
- Vox is new and still in testing. Keep the screen reader you use now installed, so you always have
  a way back.

## 1. Download the installer

1. Open the Vox releases page in your browser: <https://github.com/ChrisBrooksbank/Vox/releases>.
2. The newest release is first. Press **H** (in Narrator, Caps Lock+H, or turn on scan mode first
   with Caps Lock+Space) to move between headings until you reach **Assets**.
3. Below Assets there are two files you can download:
   - `Vox-<version>.msi`, **the installer**. Most people want this one.
   - `Vox-<version>-portable.zip`, a portable copy, if you can't install programs on this computer.
     See [The portable copy](#the-portable-copy) below.
4. Move to the `.msi` link and press **Enter**. Edge saves it to your Downloads folder.

If the releases page lists no files yet, no build has been published so far. You can
[build Vox yourself](#building-from-source) instead.

## 2. Run the installer

1. Open the downloaded file. In Edge, **Ctrl+J** opens the downloads list. Press **Tab** until you
   reach the Vox file, then press **Enter**. You can also open it from your Downloads folder in
   File Explorer.
2. If Windows warns that the file isn't commonly downloaded, or says "Windows protected your PC",
   the build isn't signed yet. In Edge's downloads list choose **Keep**, then **Keep anyway**. In
   the "Windows protected your PC" window, activate **More info**, then **Run anyway**.
3. The Vox Setup window opens with the licence (MIT, a free open-source licence). Press **Tab**
   to the check box **I accept the terms in the License Agreement** and press **Space** to check
   it. Then Tab to **Install** and press **Enter**.
4. Windows asks "Do you want to allow this app to make changes to your device?". Press **Alt+Y**,
   or Tab to **Yes** and press Enter.
5. Wait for the installer to finish. It takes under a minute. The last page says the setup is
   complete, and its **Start Vox now** check box is already checked. Press **Enter** on
   **Finish**.

Vox starts and speaks. Now turn off the other screen reader, or both will talk at once. For
Narrator, press **Ctrl+Windows+Enter**.

## 3. The welcome wizard

The first time Vox starts, a short spoken wizard asks how fast Vox should speak, how much detail you
want, and which key to use as the **Vox key** (Insert, or Caps Lock). Follow what it says. Press
**Escape** to keep the choices made so far and finish early. At the end it offers practice lessons
for web pages, editing text and reviewing the screen.

You can run the wizard again at any time with **Insert+Ctrl+S**. The lessons are also in the Vox
menu (**Insert+N**), under Practice lessons.

## Starting and stopping Vox

| To | Press |
| --- | --- |
| Start Vox | **Ctrl+Alt+V** (works from anywhere, once Vox is installed) |
| Quit Vox | **Insert+Q** twice quickly, or Exit Vox in the Vox menu (**Insert+N**) |
| Start Vox every time you sign in | **Insert+Ctrl+G** opens the settings. On the General page, check **Start Vox when I sign in** |
| Stop speech | **Ctrl** |

Vox also reads the Windows sign-in and lock screens and the "allow changes" prompts. The installer
sets this up for you.

If Vox stops working, it restarts itself. If it can't, it says so with the Windows voice, and you
can switch back to your other screen reader.

## What's installed where

- Vox itself: `C:\Program Files\Vox`. Its user guide is in the Vox menu, under User guide.
- Your settings: `%APPDATA%\Vox`. Logs: `%LOCALAPPDATA%\Vox\logs`.
- A Start menu shortcut called **Vox** (with the Ctrl+Alt+V hotkey), a desktop shortcut, and the
  "Vox secure screens" Windows service that reads the sign-in screen.

## Updating

Vox checks for a new version once a day and tells you when there is one. It always asks before it
installs anything. **Check for updates** in the Vox menu checks straight away. To try beta versions,
see the [beta programme](beta.md).

## Uninstalling

Open Settings (**Windows+I**), Apps, Installed apps. Type `Vox` in the search box, Tab to the Vox
entry, open its **More options** button, and choose **Uninstall**. Your settings in
`%APPDATA%\Vox` are kept, in case you install Vox again.

## The portable copy

If you can't install programs (on a work or library computer, for example), use the portable zip:

1. Download `Vox-<version>-portable.zip` from the releases page.
2. In File Explorer, select the zip, open its context menu (**Shift+F10** or the Applications key),
   choose **Extract All**, and press **Enter**.
3. In the extracted folder, open **Vox.Watchdog**.

The portable copy keeps its settings in the `userConfig` folder beside it, so it runs from a USB
stick without changing the computer. It can't read the sign-in screen or windows running as
administrator, and Ctrl+Alt+V doesn't start it.

## Building from source

For developers, or before a release is published. You need Windows 11, the
[.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) and Git:

```powershell
git clone https://github.com/ChrisBrooksbank/Vox.git
cd Vox
dotnet build
dotnet run --project src/Vox.App
```

That runs Vox from the build folder without installing it. You don't need administrator rights,
but Vox can't read windows running as administrator ([signing.md](signing.md)). To build the
installer yourself, see [installer.md](installer.md).

## Something went wrong?

- **No speech after Finish:** press **Ctrl+Alt+V**. If you still hear nothing, check the sound is
  on and the right audio device is selected. Then look in the log (press **Windows+R**, type
  `%LOCALAPPDATA%\Vox\logs`, and press **Enter**).
- **Keys don't seem to work:** Vox's commands use the Vox key you chose (Insert or Caps Lock).
  Press **Insert+1** to turn on input help, then press any key to hear what it does. Press
  **Insert+1** again to turn input help off.
- **Report a problem:** see the [beta programme](beta.md#sending-a-report). It explains how to send
  what Vox said.
