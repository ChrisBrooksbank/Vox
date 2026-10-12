using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Vox.Core.Accessibility;
using Vox.Core.Input;

namespace Vox.E2E.Tests;

/// <summary>
/// Faults injected into a running Vox, and what it must do about them. Real where a test can make
/// the fault happen (a provider that hangs or throws, a renderer or a whole browser killed, keys
/// whose key-up the hook never sees); simulated through Vox's own seams where it can't (a session
/// switch and sleep/resume through the notifications Windows would send, a crash through
/// <see cref="HookSafetyNet"/>). docs/e2e.md lists which is which.
/// </summary>
public static class FaultScenarios
{
    private const StringComparison IgnoreCase = StringComparison.OrdinalIgnoreCase;
    private const string Page = "headings-landmarks.html", PageTitle = "Headings and landmarks";

    public static readonly E2EScenario[] All =
    [
        new("hung-provider", "A provider that blocks: Vox says the app isn't responding, keeps taking keys, replaces its stuck UIA thread and follows the focus again once the app answers", HungProviderAsync),
        new("throwing-provider", "A provider that throws on every read: Vox goes on following the focus past it", ThrowingProviderAsync),
        new("renderer-crash", "The page's renderer crashes (edge://kill): the page reloads and is read again", RendererCrashAsync),
        new("browser-killed", "The browser is killed while a page is loading: the next browser's page is read", BrowserKilledAsync),
        new("session-switch", "Insert held when the session is locked (simulated lock and unlock): Vox forgets it", SessionSwitchAsync),
        new("missed-key-up", "A Ctrl key-up the hook never sees (as on the secure desktop): the next key isn't taken as Ctrl+key", MissedKeyUpAsync),
        new("sleep-resume", "Sleep and resume (simulated: power notifications, the browser frozen meanwhile): Vox stays responsive and reads the page after", SleepResumeAsync),
        new("fatal-error", "An unhandled exception in Vox (through HookSafetyNet): the keyboard hook is released and keys reach the app", FatalErrorAsync),
    ];

    private static async Task HungProviderAsync(ScenarioContext c)
    {
        var uiaThread = c.Vox.Services.GetRequiredService<UIAThread>();
        var recovery = c.Vox.Services.GetRequiredService<UIARecovery>();
        var recovered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<bool> onRecovered = (_, allSucceeded) => recovered.TrySetResult(allSucceeded);
        recovery.Recovered += onRecovered;
        using var app = await FaultApp.StartAsync();
        try
        {
            c.Target = app.Window;
            await c.ExpectSaidAsync("Before");
            await c.QuietAsync();
            c.Mark();
            await c.PressAsync("Tab");
            await c.ExpectSaidAsync("Faulty button");

            await c.StepAsync("hang the faulty button's provider");
            await app.HangAsync();
            int generation = uiaThread.Generation;
            c.Mark();
            // Moving the navigator to the focus reads the focused element through UIA: the call hangs
            await c.PressAsync("Insert+NumpadMinus", "navigator to focus");
            await c.ExpectSaidAsync("not responding");

            // The keyboard and the pipeline don't depend on UIA
            c.Mark();
            await c.PressAsync("Insert+1", "input help on");
            await c.ExpectSaidAsync("Input help on");
            c.Mark();
            await c.PressAsync("Insert+1", "input help off");
            await c.ExpectSaidAsync("Input help off");

            // The watchdog replaces the thread stuck in the call
            await c.StepAsync("wait for the UIA watchdog");
            await WaitUntilAsync(() => uiaThread.Generation > generation, TimeSpan.FromSeconds(20), "the stuck UIA thread to be replaced");

            await c.StepAsync("the app answers again");
            await app.ReleaseAsync();
            var allSucceeded = await recovered.Task.WaitAsync(TimeSpan.FromSeconds(60));
            c.Log($"  UIA recovery finished (every step succeeded: {allSucceeded})");
            await Task.Delay(1000);
            await c.QuietAsync();

            // Following the focus on the new thread
            c.Mark();
            await c.PressAsync("Tab");
            await c.ExpectSaidAsync("After");
            c.Mark();
            await c.PressAsync("Shift+Tab");
            await c.ExpectSaidAsync("Faulty button");
        }
        finally
        {
            recovery.Recovered -= onRecovered;
            await app.ReleaseAsync().WaitAsync(TimeSpan.FromSeconds(5)).ContinueWith(_ => { });
        }
    }

    private static async Task ThrowingProviderAsync(ScenarioContext c)
    {
        using var app = await FaultApp.StartAsync();
        c.Target = app.Window;
        await c.ExpectSaidAsync("Before");
        await c.QuietAsync();

        await c.StepAsync("make the faulty button's provider throw");
        await app.ThrowAsync();
        c.Mark();
        await c.PressAsync("Tab", "onto the throwing button");
        c.Log($"  on the throwing button Vox said: {c.Said}");
        c.Mark();
        await c.PressAsync("Tab", "past it");
        await c.ExpectSaidAsync("After");

        await app.ReleaseAsync();
        c.Mark();
        await c.PressAsync("Shift+Tab", "back, now that it answers");
        await c.ExpectSaidAsync("Faulty button");
    }

    private static async Task RendererCrashAsync(ScenarioContext c)
    {
        using var browser = await OpenPageAsync(c, Page, PageTitle);
        c.Mark();
        await c.PressAsync("H");
        await c.ExpectSaidAsync("heading level");

        // Chromium's own crash page kills the tab's renderer
        await c.PressAsync("Ctrl+L", "address bar");
        await c.StepAsync("type edge://kill");
        KeyInjector.Type("edge://kill");
        await c.PressAsync("Enter");
        await Task.Delay(2000);
        await c.QuietAsync();

        c.Mark();
        await c.PressAsync("Ctrl+L", "address bar");
        await c.StepAsync("type the page's address");
        KeyInjector.Type(new Uri(Path.Combine(BrowserSession.PagesDirectory, Page)).AbsoluteUri);
        await c.PressAsync("Enter", "load it again");
        await c.ExpectAsync(t => t.Contains(PageTitle), "the reloaded page's title", CorpusScenarios.PageLoad);
        await c.QuietAsync();
        c.Mark();
        await c.PressAsync("H");
        await c.ExpectSaidAsync("heading level");
    }

    private static async Task BrowserKilledAsync(ScenarioContext c)
    {
        using (var browser = BrowserSession.Open("long-page.html"))
        {
            // Killed as soon as the page is being read in: its document capture may be under way
            await c.ExpectAsync(t => t.Contains("Long page"), "the long page's title", CorpusScenarios.PageLoad);
            await c.StepAsync("kill the browser");
            browser.Kill();
        }
        await Task.Delay(2000);
        await c.QuietAsync();

        using var next = await OpenPageAsync(c, Page, PageTitle);
        c.Mark();
        await c.PressAsync("H");
        await c.ExpectSaidAsync("heading level");
    }

    private static async Task SessionSwitchAsync(ScenarioContext c)
    {
        using var browser = await OpenPageAsync(c, Page, PageTitle);

        // Insert goes down; its key-up is lost to the lock screen
        await c.StepAsync("hold Insert down");
        KeyInjector.KeyDown(KeyInjector.Insert);
        c.Mark();
        await c.PressAsync("T", "with Insert held: the window's title");
        await c.ExpectSaidAsync(PageTitle);

        await c.StepAsync("lock and unlock the session (simulated)");
        SystemEventsSimulator.LockAndUnlock();
        await Task.Delay(300);

        c.Mark();
        await c.PressAsync("H", "Insert is forgotten: the next heading");
        await c.ExpectSaidAsync("heading level");
    }

    private static async Task MissedKeyUpAsync(ScenarioContext c)
    {
        const ushort leftControl = 0xA2;
        using var browser = await OpenPageAsync(c, Page, PageTitle);

        await c.StepAsync("Ctrl down, its key-up unseen by Vox");
        KeyInjector.KeyDown(leftControl);
        KeyInjector.KeyUp(leftControl, KeyInjector.UnseenByVox);

        c.Mark();
        await c.PressAsync("H", "not Ctrl+H: the next heading");
        await c.ExpectSaidAsync("heading level");
    }

    private static async Task SleepResumeAsync(ScenarioContext c)
    {
        using var browser = await OpenPageAsync(c, Page, PageTitle);

        await c.StepAsync("go to sleep (simulated): power notification, browser frozen");
        SystemEventsSimulator.Suspend();
        var frozen = new List<Process> { browser.Process };
        frozen.AddRange(DesktopWindows.Descendants(browser.Process.Id));
        foreach (var process in frozen)
            DesktopWindows.Suspend(process);
        try
        {
            // The buffer needs no UIA
            c.Mark();
            await c.PressAsync("H", "read from the buffer while the browser is frozen");
            await c.ExpectSaidAsync("heading level");

            // UIA calls into the frozen browser time out
            c.Mark();
            await c.PressAsync("Insert+NumpadMinus", "navigator to focus: a UIA call");
            await c.ExpectAsync(t => t.Contains("not responding", IgnoreCase) || t.Contains("Not available", IgnoreCase),
                "that the browser isn't responding", TimeSpan.FromSeconds(15));
            await Task.Delay(TimeSpan.FromSeconds(8));
        }
        finally
        {
            foreach (var process in frozen)
            {
                try { DesktopWindows.Resume(process); }
                catch (InvalidOperationException) { }
                if (process != browser.Process)
                    process.Dispose();
            }
        }
        await c.StepAsync("wake up");
        SystemEventsSimulator.Resume();
        await Task.Delay(2000);
        await c.QuietAsync();

        c.Mark();
        await c.PressAsync("Tab", "the focus moves in the page");
        await c.ExpectSaidAsync("link");
        c.Mark();
        await c.PressAsync("H");
        await c.ExpectSaidAsync("heading level");
    }

    private static async Task FatalErrorAsync(ScenarioContext c)
    {
        using var app = await FaultApp.StartAsync();
        c.Target = app.Window;
        await c.ExpectSaidAsync("Before");
        await c.QuietAsync();
        await c.PressUntilAsync("Tab", t => t.Contains("Notes", IgnoreCase), 4, "the Notes field");
        c.Mark();
        await c.TypeAsync("a");
        await c.ExpectAsync(t => t.Equals("a", IgnoreCase), "the typed \"a\"");

        await c.StepAsync("an unhandled exception in Vox (through its hook safety net)");
        var safetyNet = c.Vox.Services.GetRequiredService<HookSafetyNet>();
        safetyNet.HandleFatalError(new InvalidOperationException("Injected by the end-to-end fault run"));
        if (!safetyNet.HookReleased)
            throw new ScenarioFailedException("The safety net didn't release the keyboard hook");

        // Insert+T would be Vox's (say the title); now Insert and T both reach the field
        c.Mark();
        await c.PressAsync("Insert+T");
        c.ExpectNotSaid(t => t.Contains(FaultApp.Title, IgnoreCase), "the window title (it still handled Insert+T)");
        var notes = await app.NotesAsync();
        if (notes != "at")
            throw new ScenarioFailedException($"Notes holds \"{notes}\", not \"at\": the keys didn't reach the app");
    }

    /// <summary>Opens a corpus page in Edge and waits until Vox has read it in.</summary>
    private static async Task<BrowserSession> OpenPageAsync(ScenarioContext c, string page, string title)
    {
        var browser = BrowserSession.Open(page);
        try
        {
            await c.ExpectAsync(t => t.Contains(title), $"the title \"{title}\"", CorpusScenarios.PageLoad);
            c.Target = await browser.WindowAsync(TimeSpan.FromSeconds(5));
            await c.QuietAsync();
            return browser;
        }
        catch
        {
            browser.Dispose();
            throw;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string description)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new ScenarioFailedException($"Timed out waiting for {description}");
            await Task.Delay(100);
        }
    }
}
