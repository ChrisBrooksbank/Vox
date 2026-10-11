using Vox.Core.AppModules.Office;

namespace Vox.Core.Tests.TestSupport;

/// <summary>
/// An <see cref="IOfficeObjectModel"/> over plain objects standing in for Office's
/// <c>Application</c> objects (late binding works on public C# types as on COM). Work runs at once,
/// on the caller's thread; an application not set, or work that throws, gives null as the real one does.
/// </summary>
public sealed class FakeOfficeObjectModel : IOfficeObjectModel
{
    public Dictionary<OfficeApp, object> Applications { get; } = new();

    /// <summary>How many calls were made.</summary>
    public int Calls { get; private set; }

    /// <summary>When set, every call waits for this before running (to test results arriving late).</summary>
    public Task? Gate { get; set; }

    public async Task<T?> RunAsync<T>(OfficeApp app, IntPtr window, Func<dynamic, T?> work, TimeSpan? timeout = null) where T : class
    {
        Calls++;
        if (Gate is { } gate)
            await gate.ConfigureAwait(false);
        if (!Applications.TryGetValue(app, out var application))
            return null;
        try
        {
            return work(application);
        }
        catch
        {
            return null;
        }
    }
}
