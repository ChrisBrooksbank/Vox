# Firefox

Vox browses Firefox pages through UI Automation, like Chrome and Edge.

## How a Firefox page is found

`BrowseDocumentTracker` looks for the outermost web `Document` above the focused element.
An element counts as web content (`WebContent.IsWebElement`) when:

- its UIA framework is `Chrome` (Chrome, Edge, Electron, WebView2) or `Gecko` (Firefox's native UIA), or
- it is inside a Firefox window (an ancestor's class name starts with `Mozilla`) and isn't a plain `Win32` window. This covers Firefox without native UIA, whose elements reach UIA through Windows' MSAA-to-UIA proxy.

The page is then captured with the same cached `BuildUpdatedCache` call and `UIAElementSnapshot` as a Chromium page.

## What works through UIA

- Text, links, form fields, buttons, check boxes, radio buttons, combo boxes, lists, tables (GridItem), landmarks where Firefox reports them, and focus following.
- Headings: from the UIA `HeadingLevel` property, or else from the localized control type ("heading", "heading 2"; English only, `WebContent.HeadingLevelFromLocalizedType`).

## What needs IAccessible2 (not yet available)

Through the MSAA proxy, Firefox doesn't report several things Chromium puts in UIA properties:

- ARIA roles and properties (`AriaRole`, `AriaProperties`): landmarks, `aria-current`, `aria-sort`, `aria-pressed`, modal dialogs and annotations aren't recognised.
- Visited links, `aria-describedby`/`aria-errormessage` relations, `aria-details`, and the `lang` of an element.
- Text attributes (fonts, spelling errors) and live regions, which Firefox reports through IA2 events.

Reading these needs an `IVBufferElement` provider over IAccessible2 (`IAccessible2::attributes` holds `xml-roles`, `level` and the ARIA states; `IAccessible2::relations` the described-by and error-message targets; `IAccessibleText` the text attributes). Calling IA2 needs the IA2 proxy/stub, so it belongs in the planned C++/CLI `Vox.NativeHelper` (PLAN.md, Phase 3). The provider would plug in beside `UIAElementSnapshot` and be chosen for documents inside a Firefox window.

## The native helper (in progress)

`src/Vox.NativeHelper` (C++/CLI, not in `Vox.sln`; built by the `native-helper` CI job) captures a Firefox document's IA2 tree as JSON (`IA2Capture.CaptureDocument`): role, states, name, value, description, `IAccessible2::attributes`, relations (target unique ids) and text-attribute runs. `IA2SnapshotReader` (Vox.Core) reads it into a COM-free `IA2Node` tree. The `IVBufferElement` provider over that tree and the IA2 events are the next steps; until then Firefox pages are read through UIA as above.
