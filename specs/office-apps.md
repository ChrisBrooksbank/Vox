# Office & Productivity Apps

## Overview

Per-application support for the apps people use at work: an app-module framework, then Word, Excel, Outlook, PowerPoint, Teams, File Explorer and developer tools. Workstream W7 of `COMMERCIAL_PARITY_PLAN.md` (Milestone D).

## User Stories

- As a blind employee, I want to read and edit Word documents with tracked changes and comments
- As a blind employee, I want to work in Excel knowing which cell I'm in and its headers
- As a blind employee, I want to triage email and calendar in Outlook efficiently
- As a blind employee, I want to follow Teams chats and meetings

## Requirements

### Framework

- [ ] `IAppModule`: `ProcessNames`, `OnActivated`/`OnDeactivated`, keymap overrides, announcement overrides (`FormatAnnouncement`), event filters
- [ ] `AppModuleManager`: picks the module for the foreground process, swaps keymap layers, default module when none matches
- [ ] Office object-model access helper: out-of-process COM through `UIAThread`-like dedicated STA thread with timeouts

### Word

- [ ] Document reading through `UIATextDocument` (desktop-text-and-controls.md)
- [ ] Quick navigation in documents: headings, tables, lists, comments, revisions, spelling errors (UIA text attributes / annotations)
- [ ] Comments and tracked changes announced on caret entry; list of comments (Elements List tab)
- [ ] Page, section, line and column report (Insert+Delete-style command)
- [ ] Table cell navigation reusing `TableNavigator` with Word tables

### Excel

- [ ] Cell move speaks address, value and (setting) formula indicator; Insert+F2-style formula read
- [ ] Column/row titles: user-defined title row/column per sheet (JAWS-style), spoken on move
- [ ] Selection range size ("A1 to C5, 15 cells"), sheet name on switch, filters, comments/notes, data-validation input messages
- [ ] Chart summary command (title, type, series)

### Outlook

- [ ] Message list: configurable column order, unread/flagged/attachment/importance first or last
- [ ] Reading pane auto-read (setting), header fields command
- [ ] Calendar day/week view: appointment summaries; meeting request actions

### PowerPoint

- [ ] Slide number and title on slide change; shape navigation; speaker notes command; slideshow reading

### Teams

- [ ] Chat/channel message navigation, new message announcements (filtered), meeting controls state (mute/camera), call state changes

### Others

- [ ] File Explorer details view: column headers with values
- [ ] Visual Studio Code: don't double-speak its own accessibility signals; respect its accessible view
- [ ] Visual Studio, Zoom, Adobe Acrobat basics

## Acceptance Criteria

- [ ] Unit tests for `AppModuleManager` selection and keymap layering
- [ ] Unit tests for each module's announcement formatting with mock data
- [ ] Manual "first day at work" scenario: Outlook triage, accept a meeting, edit a Word report with tracked changes, update an Excel budget, join a Teams call — no sighted help

## Out of Scope

- Office versions older than Microsoft 365 current channel
