namespace Vox.E2E.Tests;

/// <summary>
/// Runs scenarios one at a time to watch and hear them (docs/e2e.md):
/// <code>
/// dotnet run --project tests/Vox.E2E.Tests -- list
/// dotnet run --project tests/Vox.E2E.Tests -- run notepad [--step] [--delay 800] [--silent]
/// dotnet run --project tests/Vox.E2E.Tests -- soak [--hours 0.5] [--report soak.csv]
/// </code>
/// Speech is heard through SAPI unless --silent. With --step each step waits for the Pause key
/// (or Ctrl+Shift+Alt+N); with --delay it waits that many milliseconds.
/// </summary>
public static class InteractiveRunner
{
    public static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("The scenarios drive the Windows desktop: run them on Windows.");
            return 2;
        }
        switch (args.FirstOrDefault()?.ToLowerInvariant())
        {
            case "list":
                foreach (var scenario in E2EScenarios.All)
                    Console.WriteLine($"{scenario.Name,-20} {scenario.Description}");
                return 0;
            case "run" when args.Length >= 2:
                return await RunAsync(args[1], args.Skip(2).ToList());
            case "soak":
                return await SoakAsync(args.Skip(1).ToList());
            default:
                Console.Error.WriteLine("Usage: list | run <scenario> [--step] [--delay ms] [--silent] | soak [--hours h] [--report path]");
                return 2;
        }
    }

    private static async Task<int> RunAsync(string name, List<string> options)
    {
        if (E2EScenarios.Find(name) is not { } scenario)
        {
            Console.Error.WriteLine($"No scenario {name}; \"list\" lists them.");
            return 2;
        }
        using var stepper = options.Contains("--step") ? new HotkeyStepPacer(Console.Out) : null;
        IScenarioPacer pacer = stepper is not null ? stepper
            : Option(options, "--delay") is { } delay && int.TryParse(delay, out var ms) ? new DelayPacer(TimeSpan.FromMilliseconds(ms))
            : ScenarioPacers.None;

        await using var vox = await VoxHarness.StartAsync(new VoxHarnessOptions { Audible = !options.Contains("--silent") });
        var context = new ScenarioContext(vox, pacer, Console.WriteLine);
        Console.WriteLine($"{scenario.Name}: {scenario.Description}");
        try
        {
            await scenario.RunAsync(context);
            Console.WriteLine("Passed");
            return 0;
        }
        catch (ScenarioFailedException ex)
        {
            Console.WriteLine($"FAILED: {ex.Message}");
            Console.WriteLine($"Said since the last step: {context.Said}");
            return 1;
        }
    }

    private static async Task<int> SoakAsync(List<string> options)
    {
        var duration = Option(options, "--hours") is { } hours && double.TryParse(hours, System.Globalization.CultureInfo.InvariantCulture, out var h)
            ? TimeSpan.FromHours(h)
            : SoakRunner.Duration;
        var report = Option(options, "--report") ?? SoakRunner.ReportPath;
        var samples = await SoakRunner.RunAsync(duration, report, Console.WriteLine);
        var problems = SoakAnalysis.Problems(samples);
        foreach (var problem in problems)
            Console.WriteLine("PROBLEM: " + problem);
        Console.WriteLine($"{samples.Count} cycles; report {report}");
        return problems.Count == 0 ? 0 : 1;
    }

    private static string? Option(List<string> options, string name)
    {
        int index = options.IndexOf(name);
        return index >= 0 && index + 1 < options.Count ? options[index + 1] : null;
    }
}
