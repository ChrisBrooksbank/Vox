# End-to-end tests

`tests/Vox.E2E.Tests` runs Vox against a real browser and checks what it says. Each test starts Vox's services in the test process, wired as the app wires them (`ServiceRegistration`), except that:

- speech goes to a `RecordingSpeechEngine`, which the test asserts on;
- settings come from a temporary read-only file (first-run wizard done, audio cues off);
- start at logon is a no-op, so your own registration is never touched.

It then opens a page from the test-page corpus (`tests/pages/`) in a new Edge window with a fresh profile, presses keys through `SendInput` (they go through Vox's keyboard hook like typed keys), and waits for the expected speech.

## Running them

They need Windows, an unlocked interactive desktop that nobody types into while they run, and Microsoft Edge (or another Chromium browser named by `VOX_E2E_BROWSER`). Close Vox first: a running Vox would handle every key as well.

```powershell
$env:VOX_E2E = "1"
dotnet test tests/Vox.E2E.Tests
```

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

`CorpusScenarios` lists a scenario for every corpus page: the keys pressed once its title has been said (written as `"H"`, `"Shift+H"`, `"Insert+Shift+D"`, `"Ctrl+Alt+Right"`). `CorpusTests` runs each in Edge and in Chrome (a browser that isn't installed is passed over; `VOX_E2E_CHROME` names Chrome's path), waits after each key until Vox has been quiet for 600 ms, and checks the transcript `<scenario>.<browser>`. A unit test makes sure every corpus page has a scenario. Firefox joins once Vox browses it fully (`docs/firefox.md`).
