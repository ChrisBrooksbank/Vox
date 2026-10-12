# Performance

How quickly Vox answers a key and starts reading a page, how those figures are measured, and how
to measure NVDA and JAWS the same way. The figures come from the performance run of the end-to-end
tests ([End-to-end tests](e2e.md#performance-run)); CI holds every later run to them.

> **Status:** the method and the regression gate are in place; the figures below have not been
> measured yet. They are filled in from the first run on the reference machine (and the baseline
> committed with them). Until then every figure reads "to be measured", and no number on this
> page should be quoted.

## What is measured

| Figure | From | To |
| --- | --- | --- |
| Keypress-to-speech latency (95th percentile) | the key press, sent through `SendInput` as a typed key would be | the speech engine receiving the first text Vox says about it |
| Time to first speech, per page | asking the browser to open the page (a new window with a fresh profile) | Vox saying the page's title |

Keys measured on each page are its corpus scenario's keys (quick navigation, table navigation,
activating controls, reading) and ten reading keys (Down ×5, Up ×3, Insert+Up,
Ctrl+Home). A key that makes Vox say nothing within 3 seconds is not counted. The 95th percentile
is taken over every key on every page (nearest rank).

The pages are the test-page corpus in `tests/pages/` (`manifest.json` lists them), including the
two large ones:

- `long-page.html`: about 10,000 elements (240 sections of headings, paragraphs with links, lists
  and small tables);
- `large-table.html`: a data table of 10,000 rows and 4 columns with row and column headers.

What the figures leave out: the time the speech engine takes to turn text into sound (the same for
every screen reader using the same voice, and measured separately by the audio method below), and
the time the browser takes to start (in time to first speech, so compare it between screen readers
on the same machine only).

## Vox's figures

To be filled from the E2E run: copy them from `vox-performance.json` (`VOX_PERF_REPORT`) of a run
on the reference machine, and record the same run as the baseline (`VOX_PERF_UPDATE_BASELINE=1`).

| Figure | Vox |
| --- | --- |
| Keypress-to-speech latency, p95, all pages | to be measured |
| Time to first speech, `long-page.html` | to be measured |
| Time to first speech, `large-table.html` | to be measured |
| Time to first speech, median of the other corpus pages | to be measured |

### Hardware and versions

To be filled with the run's figures (`recordedOn` in the baseline gives the machine, Windows,
browser and Vox version):

| | |
| --- | --- |
| Computer (CPU, memory, disk) | to be recorded |
| Windows (edition, version, build) | to be recorded |
| Browser (Edge version) | to be recorded |
| Vox (version, Release build, speech engine) | to be recorded |
| Power plan, other software running | to be recorded (High performance; nothing else running) |

## Running it

On Windows, signed in, with nothing else using the keyboard and no other screen reader running:

```powershell
$env:VOX_E2E = "1"
$env:VOX_PERF_REPORT = "$env:TEMP\vox-performance.json"
dotnet test tests/Vox.E2E.Tests -c Release --filter "FullyQualifiedName~Performance"
```

The test log prints a summary; the report holds every sample. Run it three times and keep the
middle run's figures. `VOX_PERF_UPDATE_BASELINE=1` writes the run's figures into
`tests/Vox.E2E.Tests/Performance/baseline.json` instead of checking them.

## The regression gate

The Performance workflow (`.github/workflows/performance.yml`, every Monday and from the Actions
tab) runs the measurement and fails when a figure is worse than the baseline by more than both of
its margins: 25% and 30 ms for the keypress p95, 25% and 300 ms for a page's time to first speech
(the margins are in `baseline.json`). A figure with no baseline is only reported. The workflow
needs a runner with an unlocked interactive desktop; set the repository variable
`VOX_PERF_RUNNER` to a self-hosted runner's label so the figures always come from the same
machine. The gate's own logic runs with the unit tests on every push.

Inside Vox, `LatencyTracker` keeps the same key-to-speech figure broken into stages (keyboard hook
to dispatcher, command to pipeline, queue to engine) and writes their percentiles to the log, which
helps find which stage a regression is in.

## Measuring NVDA and JAWS the same way

Use the same computer, Windows, browser version, pages and voice (a OneCore voice at the same
rate) for every screen reader, with each one's default settings otherwise, in browse mode (the
virtual cursor), and only one screen reader running at a time. Open the pages from disk
(`tests/pages/`) in Edge started as the E2E tests start it:

```powershell
& "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe" --user-data-dir="$env:TEMP\perf-profile" `
  --no-first-run --no-default-browser-check --force-renderer-accessibility --new-window "file:///C:/path/to/tests/pages/large-table.html"
```

Press each page's keys as the corpus scenario lists them (`tests/Vox.E2E.Tests/CorpusScenarios.cs`)
followed by the ten reading keys, waiting for speech to finish after each, and repeat the page
three times.

### NVDA: from its log

NVDA timestamps what it hears and says, so it can be measured to the same point as Vox (the text
reaching the synthesizer):

1. NVDA menu, Preferences, Settings, General: set **Logging level** to **Input/output**.
2. Restart NVDA, open the page, and press the keys.
3. Open the log (NVDA menu, Tools, View log). Each key appears as an `Input: kb(desktop):...` entry
   and what NVDA says as a `Speaking [...]` entry, each with its time. The latency of a key is the
   time of the first `Speaking` after its `Input` entry, less the time of the `Input` entry.
4. Time to first speech: note the time just before starting Edge (for example by running
   `Get-Date -Format HH:mm:ss.fff` in the same command), and take the first `Speaking` entry that
   contains the page's title.

The `Input` entry is written when NVDA handles the key, slightly after the key press, so NVDA's
figure leaves out the keyboard hook's own (sub-millisecond) time; note it with the results.

### JAWS (and any screen reader): from a recording

JAWS keeps no such log, so measure from the sound, and measure NVDA and Vox the same way when
comparing with JAWS:

1. Record what the computer plays (Audacity with the Windows WASAPI host, the output device as a
   loopback input) at 48 kHz.
2. Press each key with a script that plays a short click through the same output device at the
   moment it sends the key, so the recording marks the key press:

   ```powershell
   Add-Type -AssemblyName System.Windows.Forms
   $click = New-Object System.Media.SoundPlayer "C:\path\to\click.wav"
   $click.Load()
   foreach ($key in "{DOWN}", "{DOWN}", "h") {
       $click.Play(); [System.Windows.Forms.SendKeys]::SendWait($key)
       Start-Sleep -Seconds 3
   }
   ```

3. In the recording, the latency of each key is the time from the start of its click to the start
   of the speech after it. For time to first speech, play the click as the script starts Edge, and
   measure to the start of the page's title.
4. Take the 95th percentile of the keys, as for Vox (with 20 keys, the second-highest value).

This figure includes the synthesizer's time to produce sound and the audio output's buffering, so
it is higher than the log-based figure; compare screen readers only by the same method. Report
the hardware and versions table above for every screen reader measured, and each screen reader's
version and synthesizer.

### Comparison table

To be filled from the measurements above, by the same method for every column:

| Figure (method) | Vox | NVDA | JAWS |
| --- | --- | --- | --- |
| Keypress-to-speech p95 (recording) | to be measured | to be measured | to be measured |
| Time to first speech, `large-table.html` (recording) | to be measured | to be measured | to be measured |
| Time to first speech, `long-page.html` (recording) | to be measured | to be measured | to be measured |
| Keypress-to-speech p95 (Vox: E2E run, NVDA: log) | to be measured | to be measured | not available |
