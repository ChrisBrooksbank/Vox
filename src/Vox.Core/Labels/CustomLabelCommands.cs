using Microsoft.Extensions.Logging;
using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Localization;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.Core.Labels;

/// <summary>
/// Labels the element at the browse cursor (or the focused one) on a web page, asking for its
/// label, and runs the custom labels dialog (Vox menu). The page is captured again after a change,
/// so the buffer and focus announcements use the new labels. None on secure screens (the store
/// has no file there). Commands arrive on the pipeline thread.
/// </summary>
public sealed class CustomLabelCommands(
    CustomLabelStore store,
    BrowseModeController browse,
    ITextPrompt prompt,
    ICustomLabelsPresenter presenter,
    IEventSink pipeline,
    Action recapture,
    ILogger<CustomLabelCommands> logger,
    SpeechQueue? speechQueue = null)
{
    public bool TryHandle(NavigationCommand command)
    {
        switch (command)
        {
            case NavigationCommand.LabelElement:
                Label();
                return true;
            case NavigationCommand.ManageLabels:
                Manage();
                return true;
            default:
                return false;
        }
    }

    private void Label()
    {
        if (!store.IsAvailable)
        {
            Say(VoxStrings.Get("Labels.NotHere"));
            return;
        }
        if (browse.LabelTarget() is not { } target)
        {
            Say(VoxStrings.Get("Labels.NothingToLabel"));
            return;
        }
        if (CustomLabelSites.SiteOf(target.Document.Root.Value) is not { } site)
        {
            Say(VoxStrings.Get("Labels.NotOnThisPage"));
            return;
        }
        var signature = ElementSignature.Of(target.Node, target.Document.FlatText);
        var existing = store.Find(site, signature);
        if (browse.BeginOwnDialog())
            _ = AskAsync(site, signature, existing, Describe(target.Node, target.Document.FlatText));
    }

    /// <summary>Asks for the label and keeps it (an existing label is offered to change).</summary>
    public async Task AskAsync(string site, string signature, CustomLabel? existing, string element)
    {
        string? answer = null;
        try
        {
            answer = await prompt.AskAsync(VoxStrings.Get("Labels.PromptTitle"), VoxStrings.Format("Labels.Prompt", element),
                history: existing is null ? [] : [existing.Label]).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Custom label prompt failed");
        }
        pipeline.Post(new VoxDialogClosedEvent(DateTimeOffset.UtcNow, QuietFocusReturn: answer is not null));
        if (answer is null)
            return;
        if (store.Set(new CustomLabel(site, signature, answer.Trim(), existing?.PathPrefix)))
        {
            Say(VoxStrings.Format("Labels.Saved", answer.Trim()));
            recapture();
        }
        else
        {
            Say(VoxStrings.Get("Labels.NotSaved"));
        }
    }

    private void Manage()
    {
        if (!store.IsAvailable)
        {
            Say(VoxStrings.Get("Labels.NotHere"));
            return;
        }
        if (browse.BeginOwnDialog())
            _ = ManageAsync(new CustomLabelsEditor(store.All, CustomLabelSites.SiteOf(browse.CurrentAddress)));
    }

    /// <summary>Runs the dialog over <paramref name="editor"/> and keeps what it returns.</summary>
    public async Task ManageAsync(CustomLabelsEditor editor)
    {
        IReadOnlyList<CustomLabel>? kept = null;
        try
        {
            kept = await presenter.EditAsync(editor).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Custom labels dialog failed");
        }
        pipeline.Post(new VoxDialogClosedEvent(DateTimeOffset.UtcNow));
        if (kept is null)
            return;
        if (store.ReplaceAll(kept))
            recapture();
        else
            Say(VoxStrings.Get("Labels.NotSaved"));
    }

    // "button img_23.png": what the prompt asks a label for (its name, else its content's text)
    private static string Describe(VBufferNode node, string flatText)
    {
        var role = ControlTypeNames.ToSpoken(node.ControlType) ?? string.Empty;
        var name = node.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            int end = node.TextRange.End;
            for (var next = node.NextInOrder; next is not null && IsInside(next, node); next = next.NextInOrder)
                end = Math.Max(end, next.TextRange.End);
            name = flatText[Math.Min(node.TextRange.Start, end)..Math.Min(end, flatText.Length)].ReplaceLineEndings(" ").Trim();
            if (name.Length > 60)
                name = name[..60];
        }
        return $"{role} {name}".Trim();
    }

    private static bool IsInside(VBufferNode node, VBufferNode ancestor)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (ReferenceEquals(parent, ancestor))
                return true;
        }
        return false;
    }

    private void Say(string text) => speechQueue?.Enqueue(new Utterance(text, SpeechPriority.Interrupt));
}
