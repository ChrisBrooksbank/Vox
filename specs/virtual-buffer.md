# Virtual Buffer

## Overview

In-memory document model built from UIA tree, with cursor navigation, pre-built indices, and incremental updates for SPAs.

## User Stories

- As a blind user, I want to arrow through web page content line by line, word by word, and character by character
- As a blind user, I want the page model to update when SPAs change content dynamically
- As a blind user, I want quick access to lists of all headings, links, landmarks, and form fields

## Requirements

- [ ] `VBufferNode` — Tree node with: Id, UIARuntimeId, Name, ControlType, AriaRole, HeadingLevel (0-6), LandmarkType, IsLink, IsVisited, IsRequired, IsExpandable, IsExpanded, IsFocusable, TextRange, Parent, Children, NextInOrder, PrevInOrder
- [ ] `VBufferDocument` — FlatText (all text with \n separators), Root node, AllNodes in document order
- [ ] Pre-built indices: Headings, Links, FormFields, Landmarks, FocusableElements
- [ ] Lookup: `FindByRuntimeId()`, `FindNodeAtOffset()`
- [ ] `VBufferBuilder` — Detects `ControlType.Document`, walks UIA tree depth-first with cached `TreeWalker`
- [ ] Parses AriaRole for heading levels, landmark types, link status
- [ ] Parses AriaProperties for required, expanded, visited, live
- [ ] Builds navigation indices
- [ ] Target: < 500ms for 1000-element page
- [ ] `VBufferCursor` — Position as `(currentNode, textOffset)`
- [ ] Movement: NextLine, PrevLine, NextWord, PrevWord, NextChar, PrevChar
- [ ] Boundary detection plays `boundary.wav`, optional wrap with `wrap.wav`
- [ ] `IncrementalUpdater` — On StructureChanged, identifies changed subtree by RuntimeId
- [ ] Rebuilds only changed subtree, splices into existing document
- [ ] Recalculates text offsets

## Acceptance Criteria

- [ ] VBufferBuilder produces correct node tree from mock UIA tree
- [ ] All node properties (heading level, landmark, link, etc.) correctly parsed
- [ ] Pre-built indices contain correct elements
- [ ] Cursor movement works correctly at all granularities (line/word/char)
- [ ] Boundary wrap behavior works correctly
- [ ] Incremental update correctly patches the document
- [ ] Unit tests for VBufferBuilder, VBufferCursor movement, and boundaries

## Out of Scope

- Table model / cell navigation (Phase 3)
- Native C++/CLI accelerated tree walking (Phase 3)
