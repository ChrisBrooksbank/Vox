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
    if (policy.IsSecure)
        Log.Information("Secure mode: settings are read-only, no add-ons, network or user profile");

    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureServices((context, services) =>
        {
            services.AddSingleton(policy);
            ServiceRegistration.RegisterServices(context, services);
        })
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
