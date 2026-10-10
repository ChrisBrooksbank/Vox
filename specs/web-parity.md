# Web Parity

## Overview

Close the remaining web gaps with JAWS and NVDA: Firefox and Electron/WebView2 apps, the full quick-navigation key set, table and list navigation, find, selection and copy in browse mode, fuller ARIA support, activation fallbacks, PDFs, rich editors, math and large-page performance. Builds on virtual-buffer.md and navigation.md. Workstream W5 of `COMMERCIAL_PARITY_PLAN.md` (Milestone B).

## User Stories

- As a blind user, I want the same browse mode in Firefox as in Chrome and Edge
- As a blind user, I want single keys for buttons, edits, combo boxes, check boxes, lists, graphics and more
- As a blind user, I want to move cell by cell in a data table and hear headers
- As a blind user, I want to find text on a page and select and copy text from it
- As a blind user, I want to read tagged PDFs and maths

## Requirements

### Browsers and apps

- [ ] Firefox: Gecko document detection and an `IVBufferElement` provider (UIA where Gecko exposes enough; otherwise IAccessible2 through a native helper, see PLAN.md Phase 3)
- [x] Electron and WebView2 apps (Teams, Slack, VS Code, Discord, new Outlook): document detection works; `role="application"` and `aria-roledescription="editor"`-style apps default to Focus mode

### Quick navigation

- [x] Index and commands (next/prev with Shift): B button, E edit, C combo box, X check box, R radio button, L list, I list item, G graphic, Q block quote, M frame, S separator, O embedded object, V visited link, U unvisited link, N text after a block of links, P paragraph, J heading not yet visited on this page; `,`/Shift+`,` to end/start of container
- [x] Elements List: add Buttons, Form fields by kind, and Tables tabs

### Tables and lists

- [x] `TableModel` in the buffer: grid of cells with row/column spans, header cells (`th`, `columnheader`, `rowheader`), dimensions; layout-table heuristic (no headers, one row or column, presentation role)
- [x] `TableNavigator`: Ctrl+Alt+arrows cell to cell, Ctrl+Alt+Home/End first/last cell; announce row/column number and headers when they change; boundary cue at edges
- [x] Read current row / column (Insert+Shift+Up / Insert+Shift+Num5 style bindings), report table dimensions on entry
- [x] Manually set header row/column for tables without markup (persisted per URL + table index)
- [x] Lists: "list with N items" on entry, nesting level, "out of list" while reading

### Find, select, copy

- [x] `FindInBuffer`: Insert+Ctrl+F opens a find prompt (accessible text box), Insert+F3 / Insert+Shift+F3 next/previous, case-insensitive by default, wraps with cue, keeps history
- [x] Browse-mode selection: Shift+arrows (char/line), Ctrl+Shift+arrows (word), Shift+Home/End; "selected" speech; Ctrl+C copies plain text; Insert+F9 sets mark, Insert+F10 selects from mark

### ARIA and HTML

- [x] Description (`aria-describedby`, `aria-description`) announced per verbosity
- [x] `aria-invalid` + `aria-errormessage` ("invalid entry", error text), `aria-current`, `aria-pressed`, `aria-sort`, `aria-roledescription`, `aria-keyshortcuts`, `aria-details` (read as Core-AAM maps them to UIA; a role description is told apart from the browser's own LocalizedControlType by a list of English default names, so it is only said with an English user interface; `aria-details` is said as "has details", with no command yet to read them)
- [x] Annotations: comments, insertions, deletions, marks/highlights announced on entry/exit
- [x] `<dialog>` / `aria-modal`: restrict the buffer to the modal while it is open
- [x] Clickable elements without a link/button role announced as "clickable"
- [x] Figures with captions; `<abbr>` expansion setting
- [x] "Screen layout" setting: inline elements on one line vs one element per line

### Activation and summary

- [x] Activation fallback: if no Invoke/Toggle/SelectionItem/ExpandCollapse pattern applies, simulate a mouse click at the element's clickable point
- [x] Page summary command: title, language, counts of headings/links/landmarks/form fields/tables
- [x] Say the URL of the link under the cursor
- [x] Overlay and cookie-banner detection: announce a modal overlay; offer Insert+Shift+D to activate its Reject/Close button; never dismiss automatically

### Documents

- [ ] PDF in Edge: tagged PDF tree reads in browse mode; warn "untagged document" when there is no structure and offer OCR (ocr-ai.md)
- [x] Rich editors (Google Docs, `contenteditable`): caret tracking through desktop-text-and-controls.md
- [ ] MathML: speech through MathCAT; interactive exploration with arrows inside an expression

### Performance

- [ ] Staged build: the first ~200 nodes around the focus are captured and spoken first, the rest fills in on later passes
- [ ] Budget: buffer ready ≤ 500 ms for a 10,000-node page; benchmark test with a synthetic snapshot tree

## Acceptance Criteria

- [ ] Unit tests for each new quick-nav index, `TableModel` (spans, headers, layout detection), `TableNavigator`, `FindInBuffer`, browse-mode selection, modal restriction, ARIA announcements
- [ ] W3C ARIA APG examples read correctly in Chrome, Edge and Firefox (spoken-output suite, quality-engineering.md)
- [ ] Manual: book a flight, complete a long form with errors, read a data table, read a tagged PDF

## Out of Scope

- Braille presentation of web content (braille.md)
- AI image description (ocr-ai.md)
