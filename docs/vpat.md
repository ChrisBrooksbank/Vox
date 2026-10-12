# Accessibility conformance (VPAT outline)

This is an outline for an Accessibility Conformance Report in the VPAT 2.5 (WCAG and Section 508)
format. Vox is assistive technology, so most criteria concern its own user interface: its
dialogs, the Vox menu, the notification-area icon and the installer. **Nothing below has been
audited by a third party**; the levels are the project's own assessment and must be verified
before the report is published.

| Area | Level (self-assessed) | Notes |
| --- | --- | --- |
| Keyboard (WCAG 2.1.1, 2.1.2) | Supports | Every Vox function has a key (keyboard reference); dialogs are standard WinForms controls reachable with Tab; no keyboard traps (Escape closes every dialog) |
| Name, role, value (4.1.2) | Supports | Vox dialogs use standard controls with accessible names; verified by reading them with Vox and Narrator |
| Focus order and visible focus (2.4.3, 2.4.7) | Partially supports | Standard Windows focus rectangles; dialogs not yet reviewed for high-contrast themes |
| Contrast and high contrast (1.4.3, 1.4.11) | Not evaluated | Dialogs follow the system colours; to be checked in high-contrast modes |
| Resize text (1.4.4) | Partially supports | Dialogs follow system DPI scaling; text size setting not yet tested at 200 % |
| Timing (2.2.1) | Supports | Nothing in Vox times out on the user, except the two-press confirmations (2 seconds), which can be repeated |
| Language (3.1.1) | Partially supports | English user interface; translations are in progress (Milestone D) |
| Help (Section 508 602) | Supports | User guide in accessible HTML, generated keyboard reference, input help (Insert+1), command search |
| Interoperability with assistive technology (508 502) | Supports | Vox is itself the assistive technology; it coexists with Narrator and other screen readers (sleep mode) |
| Installer (WCAG via 508 E205) | Partially supports | WiX standard minimal UI, keyboard operable; silent install documented (`docs/enterprise.md`) |

## Before publishing

- Have the dialogs audited with Narrator and NVDA as well as Vox.
- Test high-contrast themes, 200 % text and 400 % zoom for each dialog.
- Fill in the remarks per criterion from the audit, and the product version and date.
