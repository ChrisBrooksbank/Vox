using Vox.Core.Lifecycle;
using Xunit;

namespace Vox.Core.Tests.Lifecycle;

public class SecureInstanceHardeningTests
{
    [Fact]
    public void MutexIsInTheGlobalNamespace() =>
        Assert.StartsWith(@"Global\", SecureInstanceHardening.MutexName);

    [Fact]
    public void MutexDiffersFromTheUsersInstance() =>
        Assert.NotEqual(@"Local\Vox.ScreenReader", SecureInstanceHardening.MutexName);

    [Theory]
    [InlineData("SeDebugPrivilege")]
    [InlineData("SeTcbPrivilege")]
    [InlineData("SeLoadDriverPrivilege")]
    [InlineData("SeBackupPrivilege")]
    [InlineData("SeRestorePrivilege")]
    [InlineData("SeTakeOwnershipPrivilege")]
    public void DangerousPrivilegesAreRemoved(string privilege) =>
        Assert.Contains(privilege, SecureInstanceHardening.RemovedPrivileges);

    [Theory]
    [InlineData("SeChangeNotifyPrivilege")]
    [InlineData("SeCreateGlobalPrivilege")]
    public void PrivilegesTheInstanceNeedsAreKept(string privilege) =>
        Assert.DoesNotContain(privilege, SecureInstanceHardening.RemovedPrivileges);

    [Fact]
    public void NoPrivilegeIsListedTwice() =>
        Assert.Equal(SecureInstanceHardening.RemovedPrivileges.Count,
            SecureInstanceHardening.RemovedPrivileges.Distinct().Count());
}
