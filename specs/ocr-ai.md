# OCR & AI

## Overview

Make inaccessible content usable: local OCR of windows, objects and images; image and screen description with local models first and an optional cloud provider; CAPTCHA guidance; page summaries. Privacy and consent are part of every feature. Workstream W9 of `COMMERCIAL_PARITY_PLAN.md` (Milestone E).

## User Stories

- As a blind user, I want to read text in an image, an untagged PDF or an inaccessible app
- As a blind user, I want to know what a picture shows
- As a blind user, I want help understanding a cluttered screen or page
- As a blind user, I want to know nothing is sent off my PC without my say-so

## Requirements

- [ ] `IOcrEngine` and `WindowsOcrEngine` (`Windows.Media.Ocr`, local); language from settings
- [ ] Screen capture helper for the foreground window, navigator object bounds or image element bounds (not available in secure mode)
- [ ] OCR result as a reviewable `ITextDocument` with word positions; routing/click on a recognised word; Insert+R recognises the navigator object
- [ ] `IImageDescriber` with `LocalImageDescriber` (Windows AI APIs where present) and `CloudImageDescriber` (user-configured provider and API key, e.g. the Claude API)
- [ ] Consent: first cloud use per session asks; setting to always ask / allow / never; never send password fields, secure-desktop content, or windows from apps on a private list
- [ ] Insert+G describes the image or object under the cursor; result cached by image hash; "Describing image…" progress cue; cancellable
- [ ] Screen explanation: question prompt ("how do I…") answered from the UIA tree plus a screenshot, answer shown as a reviewable buffer
- [ ] CAPTCHA helper: detect common CAPTCHA widgets, announce them and point to audio alternatives or accessibility bypass options; don't attempt to solve them
- [ ] Page summary and "jump to main content" suggestions from the buffer text (local first, cloud with consent)

## Acceptance Criteria

- [ ] Unit tests with fake OCR/describer: result buffer, routing positions, consent rules (password field never sent, secure mode blocked), caching, cancellation
- [ ] Manual: read an image of text, an untagged PDF and an inaccessible app toolbar; describe photos on a news site

## Out of Scope

- Solving CAPTCHAs automatically
- Continuous screen narration
