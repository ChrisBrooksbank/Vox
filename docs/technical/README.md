# Vox technical documentation

These pages explain how Vox works inside: how the pieces fit together, which thread runs what,
and where to make a change. They are for people who want to read, fix or extend the code. To
use Vox, start with the [documentation index](../README.md) instead.

The diagrams are [Mermaid](https://mermaid.js.org/). GitHub draws them as pictures, and each one
has a text description beside it for anyone reading with a screen reader.

## Pages

1. [Overview](overview.md): the projects in the solution, the processes that run, how Vox
   starts, and the folders in `Vox.Core`.
2. [Event pipeline and threading](event-pipeline.md): the single ordered event pipeline, the
   threads Vox uses, and the rules that keep them apart.
3. [Keyboard input](input.md): the low-level hook, the keymap, the dispatcher, and browse,
   focus and sleep modes.
4. [UI Automation](ui-automation.md): the UIA thread, timeouts and recovery, focus and text
   tracking, object navigation and review.
5. [Web pages and the virtual buffer](virtual-buffer.md): capturing a page, building the
   buffer, staged loading, live updates and browse mode.
6. [Speech and audio](speech.md): the speech queue, priorities, text rules and engines.
7. [Braille](braille.md): drivers, translation, the controller and routing.
8. [Settings, profiles and policy](configuration.md): the settings layers, profiles, run policy
   and the network gate.
9. [Extensibility](extensibility.md): app modules, add-ons, custom labels and remote access.
10. [Testing](testing.md): unit tests, test seams, end-to-end tests, benchmarks and CI.

## Other sources

- [CLAUDE.md](../../CLAUDE.md) is the dense, single-page summary of the same architecture,
  kept up to date with every change. If these pages and the code disagree, the code wins, then
  CLAUDE.md.
- [specs/](../../specs/) holds the requirements for each subsystem.
- The feature guides in [docs/](../README.md#for-developers-and-maintainers) cover add-ons,
  signing, the installer, updates, translating and security in more depth.
