using Vox.Core.Configuration;
using Vox.Core.Localization;
using Vox.Core.Navigation;

namespace Vox.Core.Ai;

/// <summary>Asks whether something may be sent to the cloud provider, in a yes/no Vox dialog.</summary>
public sealed class AiConsentPrompt(IConfirmPresenter confirm, OwnDialogRunner dialogs) : IAiConsentPrompt
{
    public Task<bool> AskAsync(string question, CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested
            ? Task.FromResult(false)
            // Another Vox dialog is open: nothing is sent without a yes
            : dialogs.RunAsync(() => confirm.AskAsync(VoxStrings.Get("Ai.Consent.Title"), question), whenBusy: false);
}
