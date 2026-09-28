using Vox.Core.Buffer;

namespace Vox.Core.Navigation;

/// <summary>
/// Shows the Elements List dialog and returns the node the user picked (null if cancelled).
/// </summary>
public interface IElementsListPresenter
{
    Task<VBufferNode?> ShowAsync(VBufferDocument document);
}

/// <summary>
/// Runs <see cref="ElementsListDialog"/> on its own STA thread, as WinForms requires.
/// </summary>
public sealed class ElementsListPresenter : IElementsListPresenter
{
    public Task<VBufferNode?> ShowAsync(VBufferDocument document)
    {
        var tcs = new TaskCompletionSource<VBufferNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(ElementsListDialog.ShowModal(document));
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Vox-ElementsList",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
