namespace Vox.FaultTestApp;

/// <summary>What the faulty provider does when a UIA client reads it.</summary>
public enum FaultMode
{
    /// <summary>Answers normally.</summary>
    Normal,
    /// <summary>Blocks (on the UI thread, as a hung app would) until released, at most <see cref="Faults.MaxHang"/>.</summary>
    Hang,
    /// <summary>Throws, so the client gets an error back.</summary>
    Throw,
}

/// <summary>The fault the provider injects, switched by commands on standard input.</summary>
public static class Faults
{
    /// <summary>A hang never outlasts this, so a test that dies mid-run can't leave a hung window behind for long.</summary>
    public static readonly TimeSpan MaxHang = TimeSpan.FromSeconds(60);

    private static readonly ManualResetEventSlim Released = new(true);
    private static volatile FaultMode _mode;

    public static FaultMode Mode => _mode;

    public static void Set(FaultMode mode)
    {
        if (mode == FaultMode.Hang)
            Released.Reset();
        _mode = mode;
        if (mode != FaultMode.Hang)
            Released.Set();
    }

    /// <summary>Ends a hang: blocked calls return and later ones answer normally.</summary>
    public static void Release() => Set(FaultMode.Normal);

    /// <summary>Called at the start of every provider property read.</summary>
    public static void Inject(string what)
    {
        switch (_mode)
        {
            case FaultMode.Hang:
                Released.Wait(MaxHang);
                break;
            case FaultMode.Throw:
                throw new InvalidOperationException($"Injected fault reading {what}");
        }
    }
}
