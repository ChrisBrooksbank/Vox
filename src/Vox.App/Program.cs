using Serilog;
using Vox.App;
using Vox.Core.Input;
using Vox.Core.Lifecycle;

// --secure: the instance Vox.Service starts on the sign-in, lock and UAC screens
var policy = RunPolicy.FromArgs(args);

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(policy.LogDirectory, "vox-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)
    .CreateLogger();

// Only one instance: a second one would install another keyboard hook and UIA handlers, so every
// key would be handled twice and everything spoken twice. The secure-screen instance runs alongside
// the user's (the UAC screen is in the same session), so it has its own name.
var instanceName = policy.IsSecure ? @"Local\Vox.ScreenReader.Secure" : @"Local\Vox.ScreenReader";
using var singleInstance = new Mutex(initiallyOwned: true, instanceName, out bool isFirstInstance);
if (!isFirstInstance)
{
    Log.Error("Vox is already running; exiting");
    Log.CloseAndFlush();
    return VoxExitCodes.AlreadyRunning;
}

HookSafetyNet? safetyNet = null;
try
{
    Log.Information("Vox Screen Reader starting");
    Log.Information(UIAccess.Describe(UIAccess.IsGranted()));

    if (!policy.IsSecure)
    {
        await RunScreenReaderAsync(CancellationToken.None);
        return VoxExitCodes.Normal;
    }

    // Secure mode: Vox.Service keeps this instance on the Winlogon desktop. It speaks only while
    // that desktop has input (sign-in, lock, Ctrl+Alt+Del and UAC screens) and is silent, with no
    // keyboard hook, the rest of the time, so it never doubles the user's own Vox.
    Log.Information("Secure mode: settings are read-only, no add-ons, network or user profile");
    while (true)
    {
        await InputDesktop.WaitUntilAsync(SecureDesktopActivation.ShouldBeActive);
        using var leftSecureDesktop = new CancellationTokenSource();
        var watch = InputDesktop.WaitUntilAsync(name => !SecureDesktopActivation.ShouldBeActive(name))
            .ContinueWith(_ => leftSecureDesktop.Cancel(), TaskScheduler.Default);
        await RunScreenReaderAsync(leftSecureDesktop.Token);
        // Quit on a secure screen, or the screen went away: wait for the next one
        await watch;
    }
}
catch (Exception ex)
{
    safetyNet?.ReleaseHook();
    Log.Fatal(ex, "Vox terminated unexpectedly");
    return VoxExitCodes.Fatal;
}
finally
{
    Log.CloseAndFlush();
}

async Task RunScreenReaderAsync(CancellationToken stop)
{
    using var host = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureServices((context, services) =>
        {
            services.AddSingleton(policy);
            ServiceRegistration.RegisterServices(context, services);
        })
        .Build();

    // A crash must never leave the keyboard hooked: release it before anything else
    safetyNet?.Dispose();
    safetyNet = host.Services.GetRequiredService<HookSafetyNet>();
    safetyNet.Register();

    await host.RunAsync(stop);
}
