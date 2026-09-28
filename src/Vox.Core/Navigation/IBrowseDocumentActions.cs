using Vox.Core.Buffer;

namespace Vox.Core.Navigation;

/// <summary>
/// Actions on the live web document that require UI Automation (implemented in Vox.Core/Accessibility).
/// </summary>
public interface IBrowseDocumentActions
{
    /// <summary>
    /// Activates the element behind <paramref name="node"/>: focuses edit fields, otherwise
    /// invokes it (Invoke pattern, then LegacyIAccessible default action, then SetFocus).
    /// Returns false if the element could not be found or activated.
    /// </summary>
    Task<bool> ActivateAsync(VBufferNode node);

    /// <summary>
    /// Asks for the subtree with <paramref name="runtimeId"/> (or the whole document when null)
    /// to be captured again and delivered as a SubtreeChangedEvent.
    /// </summary>
    void RequestRecapture(int[]? runtimeId);
}
