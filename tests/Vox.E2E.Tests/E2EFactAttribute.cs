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

    public E2EFactAttribute() => Skip = SkipReason();

    /// <summary>Why end-to-end tests can't run here, or null when they can.</summary>
    internal static string? SkipReason() =>
        !OperatingSystem.IsWindows() ? "End-to-end tests need Windows"
        : Environment.GetEnvironmentVariable(EnableVariable) != "1"
            ? $"End-to-end tests drive the desktop; set {EnableVariable}=1 to run them (docs/e2e.md)"
            : null;
}

/// <summary>An end-to-end theory (see <see cref="E2EFactAttribute"/>).</summary>
public sealed class E2ETheoryAttribute : TheoryAttribute
{
    public E2ETheoryAttribute() => Skip = E2EFactAttribute.SkipReason();
}
