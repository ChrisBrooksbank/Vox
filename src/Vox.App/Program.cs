using Serilog;
using Vox.App;
using Vox.Core.Input;
using Vox.Core.Lifecycle;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Vox", "logs", "vox-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)
    .CreateLogger();

// Only one instance: a second one would install another keyboard hook and UIA handlers, so every
// key would be handled twice and everything spoken twice
using var singleInstance = new Mutex(initiallyOwned: true, @"Local\Vox.ScreenReader", out bool isFirstInstance);
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

    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureServices(ServiceRegistration.RegisterServices)
        .Build();

    // A crash must never leave the keyboard hooked: release it before anything else
    safetyNet = host.Services.GetRequiredService<HookSafetyNet>();
    safetyNet.Register();

    await host.RunAsync();
    return VoxExitCodes.Normal;
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
