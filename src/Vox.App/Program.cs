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

try
{
    Log.Information("Vox Screen Reader starting");

    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureServices(ServiceRegistration.RegisterServices)
        .Build();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Vox terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
