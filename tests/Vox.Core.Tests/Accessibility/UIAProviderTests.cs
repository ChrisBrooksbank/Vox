using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Accessibility;
using Xunit;

namespace Vox.Core.Tests.Accessibility;

public class UIAProviderTests : IDisposable
{
    private readonly UIAThread _uiaThread;
    private readonly UIAProvider _provider;

    public UIAProviderTests()
    {
        _uiaThread = new UIAThread(NullLogger<UIAThread>.Instance);
        _provider = new UIAProvider(_uiaThread, NullLogger<UIAProvider>.Instance);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _uiaThread.Dispose();
    }

    [Fact]
    public void Automation_BeforeInit_ThrowsInvalidOperationException()
    {
        // Accessing Automation before InitializeAsync should throw
        var ex = Assert.Throws<InvalidOperationException>(() => _ = _provider.Automation);
        Assert.Contains("InitializeAsync", ex.Message);
    }

    [Fact]
    public void CacheRequest_BeforeInit_ThrowsInvalidOperationException()
    {
        // Accessing CacheRequest before InitializeAsync should throw
        var ex = Assert.Throws<InvalidOperationException>(() => _ = _provider.CacheRequest);
        Assert.Contains("InitializeAsync", ex.Message);
    }

    [Fact(Skip = "Requires live UIA COM on STA thread — integration test only")]
    public async Task InitializeAsync_CreatesAutomationAndCacheRequest()
    {
        // This test requires a real Windows desktop environment with COM available.
        // Run manually or in an integration test suite.
        await _provider.InitializeAsync();

        Assert.NotNull(_provider.Automation);
        Assert.NotNull(_provider.CacheRequest);
    }
}
