using Serilog;
using Vox.App;
using Vox.Core.AddOns;
using Vox.Core.Enterprise;
using Vox.Core.Input;
using Vox.Core.Lifecycle;

// Build step: write the user guide's keyboard reference from the keymaps beside Vox, and exit
if (args is ["--keyboard-reference", var referencePath])
{
    var configDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "config");
    File.WriteAllText(referencePath, KeyboardReference.Html(configDirectory));
    File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(referencePath))!, KeyboardReference.JawsFileName),
        KeyboardReference.JawsHtml(configDirectory));
    return VoxExitCodes.Normal;
}

// Add-on developers' tools (docs/addons.md): package an add-on folder, sign a catalogue
if (args is ["--pack-addon", var addOnFolder, var packagePath, .. var packRest])
{
    var key = packRest is [var keyPath, ..] ? File.ReadAllText(keyPath) : null;
    var packed = AddOnPackage.Create(addOnFolder, packagePath, key);
    Console.WriteLine($"{packed.Id} {packed.Version}: {packed.Files.Count} files{(key is null ? ", unsigned" : ", signed")} -> {packagePath}");
    return VoxExitCodes.Normal;
}
if (args is ["--sign-catalogue", var cataloguePath, var catalogueKeyPath])
{
    using var rsa = System.Security.Cryptography.RSA.Create();
    rsa.ImportFromPem(File.ReadAllText(catalogueKeyPath));
    var signature = rsa.SignData(File.ReadAllBytes(cataloguePath), System.Security.Cryptography.HashAlgorithmName.SHA256,
        System.Security.Cryptography.RSASignaturePadding.Pkcs1);
    File.WriteAllText(cataloguePath + ".sig", Convert.ToBase64String(signature));
    Console.WriteLine($"Signed {cataloguePath} -> {cataloguePath}.sig");
    return VoxExitCodes.Normal;
}

// --secure: the instance Vox.Service starts on the sign-in, lock and UAC screens; --safe: no add-ons
var policy = RunPolicy.FromArgs(args);

// The organization's Group Policy (Software\Policies\Vox; the secure screens read only the
// computer's): it can switch the network, add-ons and AI off, and keep settings on this computer
var organizationPolicy = PolicySettings.Read(new Win32RegistryReader(), includeUser: !policy.IsSecure);
policy = organizationPolicy.Restrict(policy);
if (organizationPolicy.SettingsLocation == SettingsLocation.Local)
    VoxPaths.KeepUserDataLocal(true);

// With UI access Vox runs only code a user-level program can't have planted (docs/security.md)
if (UIAccess.IsGranted() == true)
{
    policy = policy with { RequireSignedAddOns = true };
    VoxPaths.RequireMachineComponents(true);
}

// A relay for remote access between computers that can't reach each other (docs/remote.md)
if (args is ["--remote-relay", .. var relayRest])
{
    if (!policy.AllowNetwork)
    {
        Console.Error.WriteLine("The network is turned off for Vox by policy.");
        return VoxExitCodes.Fatal;
    }
    var relayPort = relayRest is [var portText, ..] && int.TryParse(portText, out var parsedPort) ? parsedPort : Vox.Core.Remote.RemoteEndpoint.DefaultPort;
    using var relayLogs = LoggerFactory.Create(b => b.AddSimpleConsole());
    using var relay = new Vox.Core.Remote.RemoteRelayServer(relayLogs.CreateLogger("Vox.Relay"));
    using var relayStop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; relayStop.Cancel(); };
    relay.Start(relayPort);
    Console.WriteLine($"Vox remote relay on port {relay.Port}; Ctrl+C stops it.");
    await relay.RunAsync(relayStop.Token);
    return VoxExitCodes.Normal;
}

// Unattended import of NVDA's or JAWS's settings for deployment (docs/switching.md)
if (args is ["--import-settings", var importSource, .. var importRest])
    return SettingsImportTool.Run(importSource, importRest, policy);

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
// Started by the Vox before this one to restart: it may still be closing
if (!isFirstInstance && RestartRequest.HasFlag(args, RestartRequest.RestartedFlag))
{
    try { isFirstInstance = singleInstance.WaitOne(TimeSpan.FromSeconds(15)); }
    catch (AbandonedMutexException) { isFirstInstance = true; }
}
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
    Log.Information(organizationPolicy.Describe());
    foreach (var problem in organizationPolicy.Problems)
        Log.Warning("Policy: {Problem}", problem);
    Log.Information(UIAccess.Describe(UIAccess.IsGranted()));

    if (!policy.IsSecure)
    {
        var restart = await RunScreenReaderAsync(CancellationToken.None);
        if (restart is { } safeMode)
            return Restart(safeMode);
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

// A restart asked for from the Vox menu: the watchdog restarts Vox as the exit code says;
// without one, this Vox starts the next itself, which waits for this one to close
int Restart(bool safeMode)
{
    var supervised = RestartRequest.HasFlag(args, RestartRequest.SupervisedFlag);
    Log.Information("Restarting {Mode}", safeMode ? "in safe mode" : "with add-ons");
    if (supervised)
        return RestartRequest.ExitCode(safeMode);
    try
    {
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        foreach (var argument in RestartRequest.ArgumentsFor(args, safeMode, supervised: false))
            start.ArgumentList.Add(argument);
        System.Diagnostics.Process.Start(start)?.Dispose();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Could not restart Vox");
    }
    return VoxExitCodes.Normal;
}

// Runs Vox until it stops; the restart asked for (true: safe mode), or null
async Task<bool?> RunScreenReaderAsync(CancellationToken stop)
{
    using var host = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureServices((context, services) =>
        {
            services.AddSingleton(policy);
            services.AddSingleton(organizationPolicy);
            ServiceRegistration.RegisterServices(context, services);
        })
        .Build();

    // A crash must never leave the keyboard hooked: release it before anything else
    safetyNet?.Dispose();
    safetyNet = host.Services.GetRequiredService<HookSafetyNet>();
    safetyNet.Register();

    await host.RunAsync(stop);
    return host.Services.GetRequiredService<RestartRequest>().SafeMode;
}
