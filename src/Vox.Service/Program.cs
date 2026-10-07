using Vox.Core.Lifecycle;
using Vox.Service;

// Vox.Service: a Windows service (LocalSystem) that keeps a secure-mode Vox on the Winlogon desktop
// of the active console session, so the sign-in, lock, Ctrl+Alt+Del and UAC screens are read.
// Installed by the installer:  sc create VoxService binPath= "C:\Program Files\Vox\Vox.Service.exe" start= auto
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "VoxService");
builder.Services.AddSingleton<ISecureInstanceHost>(_ =>
    new Win32SecureInstanceHost(Path.Combine(AppContext.BaseDirectory, "Vox.App.exe")));
builder.Services.AddHostedService<SecureScreenService>();
builder.Build().Run();
