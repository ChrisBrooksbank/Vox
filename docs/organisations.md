# Vox for organisations

What an IT manager, accessibility lead or procurement team needs to decide on Vox and roll it
out: how to compare its cost with the screen readers already in use, a deployment checklist, the
conformance and security material, and how support works. The details of each step are in
[Deploying Vox in an organization](enterprise.md); this page puts them in order.

## Comparing the cost

Vox is free and open source under the MIT licence: no licence fee, no per-seat or per-computer
count, no software maintenance agreement (SMA) and no activation. The cost of moving to it is
therefore mostly people's time. Compare the screen readers you are considering over the same
period (three years is usual) with this worksheet, filling in your own figures; this page gives
no prices for other products, which change and depend on your agreements.

| Cost | How to work it out | Vox |
| --- | --- | --- |
| Licences | seats × price per seat (or the site/enterprise licence) | none |
| Maintenance agreement (SMA) or subscription | yearly price × years, for every seat that must stay current | none |
| Upgrades without an SMA | upgrade price × seats × upgrades in the period | none |
| Deployment | IT hours to package, test and roll out × hourly cost, plus hours for each yearly update | the MSI and Group Policy below; updates are signed MSIs |
| Training | users × hours of training × (trainer's hourly cost + the user's own hourly cost) | lower for people already using NVDA or JAWS keys: the JAWS keyboard layout, settings import and the "Vox for JAWS users" and "Vox for NVDA users" guide pages |
| Customisation | scripts, app modules or add-ons you need written or kept up to date × their cost | add-ons and label packs ([Add-ons](addons.md)); custom labels and per-site settings need no programming |
| Support | the vendor's support plan, or internal helpdesk hours × hourly cost | community support (below); plan internal first-line support |
| Optional cloud AI | requests per user per month × the provider's price, if you allow it | off unless set up; a model on the computer costs nothing per request; `DisableAI` turns AI off |
| Braille displays and hardware | the same for every screen reader | HID braille displays need no driver; others through BRLTTY |

Count the switching cost once: retraining (shortened by the JAWS layout and the imported
settings), checking the applications people use every day with Vox
([Applications](README.md#applications)), and a pilot. Keep the old screen reader installed during
the pilot: trial mode stops it while Vox is tried and brings it back with one key
([Trying Vox safely](switching.md#trying-vox-safely)).

## Deployment checklist

1. **Pilot first.** Give a few users Vox alongside their current screen reader, in trial mode
   (`Vox.App --trial`, or the welcome wizard's offer), and collect their reports (below).
2. **Install silently** with `msiexec /i Vox.msi /qn` and the properties you need
   ([Silent install](enterprise.md#silent-install)):
   - `STARTATLOGON=1` to start Vox at every sign-in;
   - `INSTALLSERVICE=1` (the default) to read the sign-in, lock and UAC screens;
   - `UPDATECHANNEL=Off` when you deploy updates yourself, `Stable` to let Vox offer them;
   - defaults for users: `KEYBOARDLAYOUT` (`Desktop`, `Laptop`, `Jaws`, `JawsLaptop`),
     `MODIFIERKEY`, `VERBOSITY`, `SPEECHRATE`, `LANGUAGE`.
   Install per machine under Program Files: a signed install there is what lets Vox read elevated
   windows ([Signing Vox and UI access](signing.md)).
3. **Set computer defaults** for anything the properties don't cover in
   `%ProgramData%\Vox\machine-settings.json` (same format as a user's `settings.json`); users start
   from them and can change them ([How settings are layered](enterprise.md#how-settings-are-layered)).
4. **Apply Group Policy** with `installer/policy/Vox.admx` and `Vox.adml`
   ([Group Policy](enterprise.md#group-policy)): decide on `DisableNetwork`, `DisableUpdates` or
   `UpdateChannel`, `DisableAddOns`, `DisableAI`, `DisableMagnifier`, `StartAtLogon`, `LockSettings` and
   `SettingsLocation` (Local for shared or kiosk computers), and enforce individual settings under
   `Software\Policies\Vox\Settings`. AI deserves a decision of its own: what may be sent to a cloud
   provider is set by `AiCloudConsent`, and `AiPrivateApps` and `AiPrivateSites` list applications
   and web sites that are never sent ([AI](ai.md#what-is-never-sent)).
5. **Profiles.** For applications that need different settings, ship configuration profiles
   (`%APPDATA%\Vox\profiles\*.json`) with the user's profile, or let users make their own; web
   sites can have their own settings and custom labels, which can be shared as label packs
   ([Configuration profiles](profiles.md), [Custom labels](switching.md#custom-labels)).
6. **Add-on trust.** With add-ons allowed, decide whose add-ons may run: deploy your publishers'
   public keys to `%APPDATA%\Vox\addons\trusted-keys` (or ship one as `assets\config\addon-key.pem`
   beside Vox), and point `AddOnCatalogueUrl` at your own signed catalogue if users should only
   install what you approve ([Add-ons](addons.md)). `DisableAddOns` turns them off altogether.
7. **Bring users' settings over unattended**, in a sign-in script or once per profile folder:
   `Vox.App --import-settings nvda|jaws [--user-profile <path>]`. It brings over speech settings,
   keys and dictionaries, keeps a copy of every Vox file it replaces, prints only what it imported,
   and exits with 1 when there was nothing to import
   ([Bringing your settings from NVDA or JAWS](switching.md)).
8. **Remote sessions.** For Remote Desktop and Citrix, install Vox on the session host and start it
   in the session ([Remote Desktop and Citrix](remote-sessions.md)).
9. **Check** on a test computer: the sign-in screen speaks, Vox starts at sign-in, the policies show
   as set by the organisation in the settings dialog (Insert+Ctrl+G), and the daily applications
   read as expected. Keep the old screen reader installed until the pilot is signed off.

## Conformance and security

- [Accessibility conformance (VPAT outline)](vpat.md): Vox's own user interface against WCAG and
  Section 508, in VPAT 2.5 form. It is the project's self-assessment and has not been audited by
  a third party; have it verified before relying on it in procurement.
- [Security review](security.md): what Vox protects (keys, passwords, the secure screens), the
  checklist every release is reviewed against, and the known weaknesses not yet fixed.
- [Privacy policy](privacy.md): what Vox sends over the network, and when; with `DisableNetwork`
  it sends nothing.
- [Updates](updates.md): updates are signed and checked before they are installed.
- [Performance](performance.md): how Vox's speed is measured and held in CI, and how to measure
  other screen readers the same way.

## Support

Vox is supported by its project, not by a vendor contract: there is no service-level agreement,
so plan first-line support inside your organisation (the helpdesk, or the accessibility team).

- **Problems and requests:** the repository's issues,
  <https://github.com/ChrisBrooksbank/Vox/issues/new/choose> (Screen reader bug report, or
  Suggestion). The form is accessible; a report with the speech viewer's text, the developer info
  (Insert+F1) and the log is the quickest to act on ([Sending a report](beta.md#sending-a-report)).
- **Security problems:** privately, through the repository's
  [security advisories](https://github.com/ChrisBrooksbank/Vox/security/advisories/new), never as
  a public issue.
- **Releases and what changed:** <https://github.com/ChrisBrooksbank/Vox/releases>; the Beta
  channel lets a few people try a version before everyone else gets it ([Beta programme](beta.md)).
- **When Vox stops working:** Vox.Watchdog restarts it, and after repeated crashes starts Narrator
  (or the screen reader a trial stepped in for) so nobody is left without speech; Restart with
  add-ons disabled in the Vox menu rules add-ons out.
- **Users' own help:** the user guide (Vox menu, User guide), input help (Insert+1), command search
  and the practice lessons.
