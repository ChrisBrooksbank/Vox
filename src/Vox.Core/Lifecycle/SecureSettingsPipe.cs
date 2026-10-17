using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.Logging;
using Vox.Core.Configuration;

namespace Vox.Core.Lifecycle;

/// <summary>
/// How a user's settings reach the sign-in, lock and UAC screens. <c>%ProgramData%\Vox</c> is
/// writable only by administrators and SYSTEM (the installer sets that), so Vox sends the settings to
/// Vox.Service over a named pipe and the service, which checks and writes them, is the only writer.
/// </summary>
public static class SecureSettingsPipe
{
    public const string PipeName = "VoxSecureSettings";

    /// <summary>Larger than any real settings file; anything bigger is refused.</summary>
    public const int MaxBytes = 256 * 1024;

    /// <summary>Sends <paramref name="settings"/> to the service; false when it isn't running or refuses.</summary>
    public static bool Send(VoxSettings settings, int timeoutMs = 3000, string pipeName = PipeName)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.None,
                System.Security.Principal.TokenImpersonationLevel.Identification);
            client.Connect(timeoutMs);
            var bytes = Encoding.UTF8.GetBytes(SettingsManager.Serialize(settings with { FirstRunCompleted = true }));
            if (bytes.Length > MaxBytes)
                return false;
            client.Write(BitConverter.GetBytes(bytes.Length));
            client.Write(bytes);
            client.Flush();
            return client.ReadByte() == 1;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Serves the pipe until cancelled. Every authenticated local user may send settings (as they may
    /// use the sign-in screens' settings today); network logons may not.
    /// </summary>
    public static async Task ServeAsync(SecureSettingsReceiver receiver, CancellationToken token, string pipeName = PipeName)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = Create(pipeName);
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var accepted = await ReadAndAcceptAsync(server, receiver, timeout.Token).ConfigureAwait(false);
                server.WriteByte(accepted ? (byte)1 : (byte)0);
                server.Flush();
            }
            catch (OperationCanceledException)
            {
                // Shutting down, or a client that never finished
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A client that went away mid-message: serve the next
            }
        }
    }

    private static async Task<bool> ReadAndAcceptAsync(Stream stream, SecureSettingsReceiver receiver, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        int length = BitConverter.ToInt32(header);
        if (length <= 0 || length > MaxBytes)
            return false;
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, token).ConfigureAwait(false);
        return receiver.Accept(Encoding.UTF8.GetString(body));
    }

    private static NamedPipeServerStream Create(string pipeName)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 0, 0, security);
    }
}

/// <summary>Checks settings received from a user and writes them where the secure instance reads them.</summary>
public sealed class SecureSettingsReceiver(ILogger<SecureSettingsReceiver> logger, string? targetPath = null)
{
    private readonly string _targetPath = targetPath ?? RunPolicy.SecureSettingsPath;

    /// <summary>Writes the settings atomically if <paramref name="json"/> is valid settings.</summary>
    public bool Accept(string json)
    {
        if (json.Length > SecureSettingsPipe.MaxBytes
            || !SettingsManager.TryParse(json, out var settings) || settings is null)
        {
            logger.LogWarning("Refused settings for the secure screens that were not valid");
            return false;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_targetPath)!);
            var temp = _targetPath + ".tmp";
            File.WriteAllText(temp, SettingsManager.Serialize(settings with { FirstRunCompleted = true }));
            File.Move(temp, _targetPath, overwrite: true);
            logger.LogInformation("Saved settings for the secure screens");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not save settings for the secure screens");
            return false;
        }
    }
}
