using System.Windows.Forms;
using Vox.Core.Navigation;
using Xunit;

namespace Vox.Core.Tests.Navigation;

/// <summary>Smoke tests for the find prompt (WinForms; run on an STA thread).</summary>
public class FindDialogTests
{
    private static void RunOnSta(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught is not null)
            throw new Xunit.Sdk.XunitException($"STA thread threw: {caught}", caught);
    }

    [Fact]
    public void Dialog_OffersTheHistory_StartingWithTheLatestSearch()
    {
        RunOnSta(() =>
        {
            using var dlg = new FindDialog(new[] { "latest", "older" }, matchCase: true);
            Assert.Equal("Find", dlg.Text);
            Assert.Null(dlg.Request);

            var box = Assert.IsType<ComboBox>(dlg.ActiveControl);
            Assert.Equal("latest", box.Text);
            Assert.Equal(new object[] { "latest", "older" }, box.Items.Cast<object>().ToArray());
        });
    }
}
