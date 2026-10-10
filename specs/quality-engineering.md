# Quality Engineering

## Overview

Keep Vox trustworthy as it grows: CI on Windows, end-to-end spoken-output tests, performance benchmarks, fault injection, a soak test, security review and a blind-tester beta. Runs through every milestone. Workstream W11 of `COMMERCIAL_PARITY_PLAN.md`.

## User Stories

- As a maintainer, I want every PR built and tested on Windows
- As a maintainer, I want to know when a change alters what Vox says
- As a blind user, I want releases that don't regress

## Requirements

- [ ] GitHub Actions workflow on `windows-latest`: restore, `dotnet build`, `dotnet test` on push and PR; test results uploaded
- [ ] `RecordingSpeechEngine` (`ISpeechEngine`) that records utterances with priority and timing for tests
- [ ] `Vox.E2E.Tests` project: starts Vox's core services in-process with the recording engine and a synthetic key injector (`SendInput`), drives Edge/Chrome/Firefox through Playwright against a local test-page corpus
- [x] Test-page corpus in `tests/pages/`: ARIA APG patterns, tables, forms with errors, live regions, dialogs, long pages
- [x] Approved-transcript snapshots: test fails on any spoken-output difference; tool to approve new transcripts
- [ ] Desktop E2E: Notepad, Explorer, Settings scenarios through UIA (runs on a self-hosted or interactive runner; skipped on headless CI)
- [x] Benchmarks (BenchmarkDotNet): buffer build per node count (1k/10k/50k synthetic snapshots), cursor movement, `TextProcessor` throughput; tracked per commit
- [ ] Latency test: injected key → first `SpeakAsync` call p95 reported
- [ ] Fault injection harness: hung UIA provider, provider throwing, browser crash, sleep/resume, session switch
- [ ] 4-hour soak test: memory and handle counts stay flat
- [ ] Logging review: typed characters and password fields never reach Serilog (test with a log sink)
- [ ] Security review checklist for the keyboard hook, `uiAccess`, secure mode, update channel and add-ons (`docs/security.md`)
- [ ] Beta programme docs: feedback channel, issue template for screen reader users (asks for speech viewer output and developer info)

## Acceptance Criteria

- [ ] CI is required for merging; green on main
- [ ] E2E suite covers every APG pattern in the corpus
- [ ] Benchmarks and latency numbers published per release

## Out of Scope

- Telemetry without explicit opt-in
