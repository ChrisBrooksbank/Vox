using Moq;
using Vox.Core.Input;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Input;

public class HookSafetyNetTests
{
    [Fact]
    public void HandleFatalError_UninstallsHookBeforeLogging()
    {
        var order = new List<string>();
        var hook = new Mock<IKeyboardHook>();
        hook.Setup(h => h.Uninstall()).Callback(() => order.Add("uninstall"));
        var logger = new CapturingLogger<HookSafetyNet>();
        var safetyNet = new HookSafetyNet(hook.Object, logger);

        safetyNet.HandleFatalError(new InvalidOperationException("crash"));
        order.Add(logger.Entries.Count > 0 ? "logged" : "not logged");

        Assert.Equal(["uninstall", "logged"], order);
        Assert.True(safetyNet.HookReleased);
    }

    [Fact]
    public void ReleaseHook_OnlyUninstallsOnce()
    {
        var hook = new Mock<IKeyboardHook>();
        var safetyNet = new HookSafetyNet(hook.Object, new CapturingLogger<HookSafetyNet>());

        safetyNet.HandleFatalError(null);
        safetyNet.ReleaseHook();

        hook.Verify(h => h.Uninstall(), Times.Once);
    }

    [Fact]
    public void ReleaseHook_UninstallThrows_DoesNotThrow()
    {
        var hook = new Mock<IKeyboardHook>();
        hook.Setup(h => h.Uninstall()).Throws(new InvalidOperationException("already gone"));
        var safetyNet = new HookSafetyNet(hook.Object, new CapturingLogger<HookSafetyNet>());

        var ex = Record.Exception(() => safetyNet.HandleFatalError(new Exception("crash")));

        Assert.Null(ex);
    }

    [Fact]
    public void Register_ThenDispose_DoesNotThrow()
    {
        var safetyNet = new HookSafetyNet(Mock.Of<IKeyboardHook>(), new CapturingLogger<HookSafetyNet>());

        safetyNet.Register();
        safetyNet.Register();
        safetyNet.Dispose();
        safetyNet.Dispose();
    }
}
