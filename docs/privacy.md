# Privacy policy

Vox reads what is on your screen and what you type, so you need to know where that goes. In
short: **it stays on your computer.** Vox has no telemetry, no analytics and no crash reporting.
Your settings, logs and dictionaries are files on your own computer. Vox never sends what you
type, your field values or your passwords anywhere, and it never writes them to its logs.

Vox connects to the network only for the features below. Each one is either turned off until you
set it up, or runs only when you ask for it. An organisation can turn all of them off with the
`DisableNetwork` policy ([Deploying Vox in an organization](enterprise.md)). The sign-in, lock
and User Account Control screens never use the network.

## What can leave your computer

- **Update checks** ([Updates](updates.md)). When an update key ships with Vox, it reads the
  signed list of releases from this GitHub repository once a day. The request carries nothing
  about you beyond what any web request does: your IP address and that the file was requested.
  Turn it off with Settings, General, Check for updates: Never.
- **AI descriptions with a cloud provider** ([AI descriptions](ai.md)). Off until you add your own
  API key. With your consent, a picture of what you asked about (and, for questions about a
  window, its text) goes to the provider you chose, under that provider's privacy policy. Password
  fields, password managers and the secure screens are never sent. A model on your own computer
  sends nothing anywhere.
- **The add-on store** ([Add-ons](addons.md)). Only when you open it: Vox reads the add-on list and
  downloads the add-ons you choose. Add-ons are written by other people and can do what any program
  can; install only ones you trust.
- **Remote access** ([Remote access](remote.md)). Only when you start a session: your speech, braille
  and keys travel encrypted to the other computer, directly or through the relay you chose.

## Bug reports

A bug report is public on GitHub. The [beta programme](beta.md) explains how to copy what Vox said
into a report; read it before you send it, and remove anything private.

## Questions

Ask in a [GitHub issue](https://github.com/ChrisBrooksbank/Vox/issues), or report a security
problem privately as [docs/security.md](security.md) explains.
