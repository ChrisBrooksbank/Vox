using Serilog;
using Vox.App;

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
    return 1;
}

try
{
    Log.Information("Vox Screen Reader starting");

    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureServices(ServiceRegistration.RegisterServices)
        .Build();

    await host.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Vox terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
