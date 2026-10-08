using Vox.Core.Navigation;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class TableHeaderStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vox-table-headers-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "table-headers.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Get_UnknownTable_HasNoHeaders() =>
        Assert.Equal(TableHeaderStore.Headers.None, new TableHeaderStore().Get("page#0"));

    [Fact]
    public void SetRowAndColumn_AreKeptTogether()
    {
        var store = new TableHeaderStore();
        store.SetRow("page#0", 1);
        store.SetColumn("page#0", 0);
        Assert.Equal(new TableHeaderStore.Headers(1, 0), store.Get("page#0"));

        store.SetRow("page#0", null);
        Assert.Equal(new TableHeaderStore.Headers(null, 0), store.Get("page#0"));
    }

    [Fact]
    public void Saved_AndReadBackByANewStore()
    {
        new TableHeaderStore(FilePath).SetRow("https://example.com/#2", 0);

        Assert.True(File.Exists(FilePath));
        Assert.Equal(new TableHeaderStore.Headers(0, null), new TableHeaderStore(FilePath).Get("https://example.com/#2"));
    }

    [Fact]
    public void ClearingBoth_RemovesTheEntry()
    {
        var store = new TableHeaderStore(FilePath);
        store.SetRow("a#0", 0);
        store.SetRow("a#0", null);
        Assert.DoesNotContain("a#0", File.ReadAllText(FilePath));
    }

    [Fact]
    public void KeepsOnlyTheMostRecentEntries()
    {
        var store = new TableHeaderStore();
        for (int i = 0; i <= TableHeaderStore.MaxEntries; i++)
            store.SetRow($"page#{i}", 0);

        Assert.Equal(TableHeaderStore.Headers.None, store.Get("page#0"));
        Assert.Equal(0, store.Get($"page#{TableHeaderStore.MaxEntries}").Row);
    }

    [Fact]
    public void UnreadableFile_StartsEmpty()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{ not json");
        Assert.Equal(TableHeaderStore.Headers.None, new TableHeaderStore(FilePath).Get("x#0"));
    }
}
