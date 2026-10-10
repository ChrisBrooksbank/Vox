using Xunit;

namespace Vox.E2E.Tests;

/// <summary>
/// An end-to-end test: it runs Vox against a real browser, injecting keys into the desktop, so
/// it needs Windows, an interactive session nobody is typing in, and VOX_E2E=1 (docs/e2e.md).
/// Skipped otherwise, so ordinary test runs and headless CI leave the desktop alone.
/// </summary>
public sealed class E2EFactAttribute : FactAttribute
{
    public const string EnableVariable = "VOX_E2E";

    public E2EFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "End-to-end tests need Windows";
        else if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
            Skip = $"End-to-end tests drive the desktop; set {EnableVariable}=1 to run them (docs/e2e.md)";
    }
}
