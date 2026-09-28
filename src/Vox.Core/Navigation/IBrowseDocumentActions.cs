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
}
