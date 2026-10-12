# Test pages

The corpus the end-to-end tests read (spec: `specs/quality-engineering.md`): ARIA Authoring Practices patterns, tables, forms with errors, live regions, dialogs, a cookie banner, annotations, math, a long page and a 10,000-row table (the last two also for the performance run, `docs/performance.md`). `manifest.json` lists every page, its title and what it exercises; `index.html` links them for manual testing.

Pages are self-contained (no network, no external scripts) so a run gives the same speech every time. Open them from a local file server or straight from disk.

Add a page by adding the file and its manifest entry; `TestPageCorpusTests` checks that they match.
