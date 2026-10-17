# End-to-end tests

`tests/Vox.E2E.Tests` runs Vox against a real browser and checks what it says. Each test starts Vox's services in the test process, wired as the app wires them (`ServiceRegistration`), except that:

- speech goes to a `RecordingSpeechEngine`, which the test asserts on;
- settings come from a temporary read-only file (first-run wizard done, audio cues off);
- start at logon is a no-op, so your own registration is never touched.

It then opens a page from the test-page corpus (`tests/pages/`) in a new Edge window with a fresh profile, presses keys through `SendInput` (they go through Vox's keyboard hook like typed keys), and waits for the expected speech.

## Running them

They need Windows, an unlocked interactive desktop that nobody types into while they run, and Microsoft Edge (or another Chromium browser named by `VOX_E2E_BROWSER`). Close Vox first: a running Vox would handle every key as well. Close other browser windows too, and don't touch the keyboard or mouse during a run: a stray window can take the focus. The tests run one at a time (xUnit parallelism is off for this assembly), because they share the one desktop and foreground window.

```powershell
$env:VOX_E2E = "1"
dotnet test tests/Vox.E2E.Tests
```

`tools/run-e2e.ps1 [filter]` does the same and is the safer way to run them: it stops a `testhost` left by an interrupted run (which locks `Vox.Core.dll`, so the next build fails and the tests would run against the old binary), stops if the build fails, and writes the full output to a log, printing a summary. Don't pipe a run into `head` or `Select-Object -First`: that abandons the test host mid-run. A corpus run takes several minutes.

Without `VOX_E2E=1` (for example on CI, which has no interactive desktop for them) the tests are skipped.

## Writing one

```csharp
[E2EFact]
public async Task Something()
{
    await using var vox = await VoxHarness.StartAsync();
    using var browser = BrowserSession.Open("tables.html");
    await vox.Speech.WaitForAsync(s => s.Text.Contains("Tables"), TimeSpan.FromSeconds(20));
    KeyInjector.Press('T');
    await vox.Speech.WaitForAsync(s => s.Text.Contains("Train times"), TimeSpan.FromSeconds(5));
}
```

## Approved transcripts

A scenario can end with `Transcript.Verify(vox.Speech, "<name>")`: everything Vox said (one utterance per line, with its priority) must match `tests/Vox.E2E.Tests/Transcripts/<name>.approved.txt` exactly. Any difference fails the test and writes `<name>.received.txt` next to it (git ignores those). A new scenario fails on its first run for the same reason. Read the received file, and if the speech is right, approve it:

```powershell
tools/approve-transcripts.ps1            # every received transcript
tools/approve-transcripts.ps1 headings-h # just one
```

Then commit the approved file; `git diff` shows what changed.

## Corpus scenarios

`CorpusScenarios` lists a scenario for every corpus page: the keys pressed once its title has been said (written as `"H"`, `"Shift+H"`, `"Insert+Shift+D"`, `"Ctrl+Alt+Right"`). `CorpusTests` runs each in Edge and in Chrome (a browser that isn't installed is passed over; `VOX_E2E_CHROME` names Chrome's path), waits after each key until Vox has been quiet for 600 ms, and checks the transcript `<scenario>.<browser>`. A unit test makes sure every corpus page has a scenario. Firefox joins once Vox browses it fully ([Firefox](firefox.md)).

## Desktop scenarios

`DesktopScenarios` drive Windows' own apps (English United States Windows 11 assumed) and check
what Vox says rather than every word, since the apps change with Windows updates:

- **notepad**: the focused editor, lines read as the caret moves, Insert+Up, typing echo
  (characters and words) and deleting;
- **explorer**: a temporary folder in details view, rows with their column headers (the Explorer
  app module), moving through the list, first-letter navigation, into a folder and back;
- **settings**: moving through the categories, and a toggle switch turned and turned back.

They run as `ScenarioRunTests.DesktopScenario` with `VOX_E2E=1`.

## Fault-injection scenarios

`FaultScenarios` inject faults into a running Vox (`ScenarioRunTests.FaultScenario`):

| Scenario | Fault | Real or simulated |
| --- | --- | --- |
| hung-provider | `tests/Vox.FaultTestApp` blocks in its UIA provider: Vox says "not responding", keeps taking keys, replaces its stuck UIA thread and recovers | Real |
| throwing-provider | the provider throws on every read | Real |
| renderer-crash | `edge://kill` on the page's renderer | Real |
| browser-killed | the browser process killed while loading | Real |
| session-switch | Insert held when the session locks | Simulated: the WM_WTSSESSION_CHANGE notifications Windows sends (`SystemEventsSimulator`) |
| missed-key-up | a Ctrl key-up the hook never sees | Real (the key-up is withheld) |
| sleep-resume | sleep and resume | Simulated: WM_POWERBROADCAST, with the browser frozen meanwhile |
| fatal-error | an unhandled exception in Vox | Through `HookSafetyNet`, which must release the hook |

`Vox.FaultTestApp` is built with the solution; the tests start it from its build output.

## Soak test

`SoakTests` (needs `VOX_E2E=1` and `VOX_SOAK=1`) runs the desktop scenarios again and again on one
Vox for `VOX_SOAK_HOURS` (default 4), sampling private bytes, the managed heap and handle count
after each cycle into a CSV (`VOX_SOAK_REPORT`, default `%TEMP%\soak.csv`). It fails when, after
three warm-up cycles, the median of the last quarter of samples is more than half again the first
quarter's and more than 100 MB (memory) or 500 (handles) higher, or when a cycle fails.

## Performance run

`PerformanceTests` (in `Performance/`, needs `VOX_E2E=1`) opens every corpus page in Edge, the
10,000-row `large-table.html` included, and records the time from opening the page to Vox saying
its title, then the latency from each key press (the page's scenario keys and some reading keys)
to the speech engine receiving what Vox says. It writes the figures to `VOX_PERF_REPORT` (default
`%TEMP%\vox-performance.json`) and fails when the keypress p95 or a page's time to first speech is
worse than `Performance/baseline.json` beyond its margins (25% and 30 ms for keys, 25% and 300 ms
for pages: a figure must exceed both). Figures without a baseline are only reported.
`VOX_PERF_UPDATE_BASELINE=1` writes the run's figures into the baseline (commit it after a run on
the reference machine). The Performance workflow (`.github/workflows/performance.yml`, weekly and
by hand) runs it; the gate's own logic is unit-tested in every CI run. Method and figures:
[Performance](performance.md).

## Interactive runner

To watch and hear one scenario, run the test project as a program:

```
dotnet run --project tests/Vox.E2E.Tests -- list
dotnet run --project tests/Vox.E2E.Tests -- run explorer --step
dotnet run --project tests/Vox.E2E.Tests -- run notepad --delay 800
dotnet run --project tests/Vox.E2E.Tests -- soak --hours 0.5
```

Speech is heard through SAPI (add `--silent` to only record it). With `--step` each step is
printed and waits for the Pause key (or Ctrl+Shift+Alt+N), system-wide hotkeys so the app under
test keeps the focus. `VOX_E2E_PACE=<ms>` and `VOX_E2E_AUDIBLE=1` do the same for test runs.

These scenarios have not yet been run on Windows: expect to adjust waits and names on first run.
