using Vox.Core.Audio;
using Vox.Core.Buffer;
using Vox.Core.Input;

namespace Vox.Core.Navigation;

/// <summary>
/// Handles Browse-mode quick navigation single-letter key commands.
///
/// Supported commands:
///   NextHeading / PrevHeading  — H / Shift+H: next/prev heading any level
///   HeadingLevel1-6            — 1-6 / Shift+1-6: heading at specific level (next only)
///   NextLink / PrevLink        — K / Shift+K: next/prev link
///   NextLandmark / PrevLandmark— D / Shift+D: next/prev landmark
///   NextFormField / PrevFormField — F / Shift+F: next/prev form field
///   NextTable / PrevTable      — T / Shift+T: next/prev table (not yet indexed; plays boundary)
///   NextFocusable / PrevFocusable — Tab / Shift+Tab: next/prev focusable element
///
/// Plays boundary.wav when no element is found and wrapping is disabled.
/// Plays wrap.wav when wrapping to the other end of the collection.
/// </summary>
public sealed class QuickNavHandler
{
    private readonly IAudioCuePlayer _audioCuePlayer;

    private VBufferDocument? _document;

    /// <summary>Current cursor node — updated after each navigation. Set externally after focus changes.</summary>
    public VBufferNode? CurrentNode { get; set; }

    /// <summary>The currently active document, or null if none has been set.</summary>
    public VBufferDocument? CurrentDocument => _document;

    /// <summary>When true the handler wraps from last to first (and first to last) element.</summary>
    public bool WrapEnabled { get; set; } = true;

    public QuickNavHandler(IAudioCuePlayer audioCuePlayer)
    {
        _audioCuePlayer = audioCuePlayer;
    }

    // -------------------------------------------------------------------------
    // Document management
    // -------------------------------------------------------------------------

    /// <summary>Sets (or replaces) the active VBufferDocument. Resets CurrentNode.</summary>
    public void SetDocument(VBufferDocument? document)
    {
        _document = document;
        CurrentNode = null;
    }

    // -------------------------------------------------------------------------
    // Command dispatch
    // -------------------------------------------------------------------------

    /// <summary>
    /// Handles a quick-navigation command in Browse mode.
    /// Returns the node navigated to, or null if no match / no document.
    /// </summary>
    public VBufferNode? Handle(NavigationCommand command)
    {
        if (_document is null) return null;

        return command switch
        {
            NavigationCommand.NextHeading     => FindNext(_document.Headings, _ => true),
            NavigationCommand.PrevHeading     => FindPrev(_document.Headings, _ => true),

            NavigationCommand.HeadingLevel1   => FindNext(_document.Headings, n => n.HeadingLevel == 1),
            NavigationCommand.HeadingLevel2   => FindNext(_document.Headings, n => n.HeadingLevel == 2),
            NavigationCommand.HeadingLevel3   => FindNext(_document.Headings, n => n.HeadingLevel == 3),
            NavigationCommand.HeadingLevel4   => FindNext(_document.Headings, n => n.HeadingLevel == 4),
            NavigationCommand.HeadingLevel5   => FindNext(_document.Headings, n => n.HeadingLevel == 5),
            NavigationCommand.HeadingLevel6   => FindNext(_document.Headings, n => n.HeadingLevel == 6),

            NavigationCommand.NextLink        => FindNext(_document.Links, _ => true),
            NavigationCommand.PrevLink        => FindPrev(_document.Links, _ => true),

            NavigationCommand.NextLandmark    => FindNext(_document.Landmarks, _ => true),
            NavigationCommand.PrevLandmark    => FindPrev(_document.Landmarks, _ => true),

            NavigationCommand.NextFormField   => FindNext(_document.FormFields, _ => true),
            NavigationCommand.PrevFormField   => FindPrev(_document.FormFields, _ => true),

            // Tables are not yet indexed in VBufferDocument; play boundary
            NavigationCommand.NextTable       => PlayBoundaryAndReturnNull(),
            NavigationCommand.PrevTable       => PlayBoundaryAndReturnNull(),

            NavigationCommand.NextFocusable   => FindNext(_document.FocusableElements, _ => true),
            NavigationCommand.PrevFocusable   => FindPrev(_document.FocusableElements, _ => true),

            _ => null,
        };
    }

    // -------------------------------------------------------------------------
    // Navigation helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Finds the next node after <see cref="CurrentNode"/> in <paramref name="collection"/>
    /// that satisfies <paramref name="predicate"/>. Updates <see cref="CurrentNode"/> on success.
    /// </summary>
    private VBufferNode? FindNext(IReadOnlyList<VBufferNode> collection, Func<VBufferNode, bool> predicate)
    {
        if (collection.Count == 0)
        {
            _audioCuePlayer.Play("boundary");
            return null;
        }

        int startIndex = IndexAfterCurrent(collection);

        // Search forward from startIndex to end
        for (int i = startIndex; i < collection.Count; i++)
        {
            if (predicate(collection[i]))
            {
                CurrentNode = collection[i];
                return CurrentNode;
            }
        }

        // Wrap from beginning up to (but not including) startIndex
        if (WrapEnabled)
        {
            for (int i = 0; i < startIndex; i++)
            {
                if (predicate(collection[i]))
                {
                    _audioCuePlayer.Play("wrap");
                    CurrentNode = collection[i];
                    return CurrentNode;
                }
            }
        }

        _audioCuePlayer.Play("boundary");
        return null;
    }

    /// <summary>
    /// Finds the previous node before <see cref="CurrentNode"/> in <paramref name="collection"/>
    /// that satisfies <paramref name="predicate"/>. Updates <see cref="CurrentNode"/> on success.
    /// </summary>
    private VBufferNode? FindPrev(IReadOnlyList<VBufferNode> collection, Func<VBufferNode, bool> predicate)
    {
        if (collection.Count == 0)
        {
            _audioCuePlayer.Play("boundary");
            return null;
        }

        int endIndex = IndexBeforeCurrent(collection);

        // Search backward from endIndex to 0
        for (int i = endIndex; i >= 0; i--)
        {
            if (predicate(collection[i]))
            {
                CurrentNode = collection[i];
                return CurrentNode;
            }
        }

        // Wrap from end down to (but not including) endIndex
        if (WrapEnabled)
        {
            for (int i = collection.Count - 1; i > endIndex; i--)
            {
                if (predicate(collection[i]))
                {
                    _audioCuePlayer.Play("wrap");
                    CurrentNode = collection[i];
                    return CurrentNode;
                }
            }
        }

        _audioCuePlayer.Play("boundary");
        return null;
    }

    // -------------------------------------------------------------------------
    // Index-finding helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the collection index to start a forward search from.
    /// If <see cref="CurrentNode"/> is in the collection, returns its index + 1.
    /// Otherwise finds the first collection node with a higher document-order Id.
    /// Returns collection.Count if no forward starting point exists (triggers wrap).
    /// </summary>
    private int IndexAfterCurrent(IReadOnlyList<VBufferNode> collection)
    {
        if (CurrentNode is null) return 0;

        // Fast path: current node is directly in the collection
        for (int i = 0; i < collection.Count; i++)
        {
            if (ReferenceEquals(collection[i], CurrentNode))
                return i + 1;
        }

        // Fallback: find first collection node that comes after CurrentNode in document order
        int currentId = CurrentNode.Id;
        for (int i = 0; i < collection.Count; i++)
        {
            if (collection[i].Id > currentId)
                return i;
        }

        return collection.Count; // triggers wrap
    }

    /// <summary>
    /// Returns the collection index to start a backward search from.
    /// If <see cref="CurrentNode"/> is in the collection, returns its index - 1.
    /// Otherwise finds the last collection node with a lower document-order Id.
    /// Returns -1 if no backward starting point exists (triggers wrap).
    /// </summary>
    private int IndexBeforeCurrent(IReadOnlyList<VBufferNode> collection)
    {
        if (CurrentNode is null) return collection.Count - 1;

        // Fast path: current node is directly in the collection
        for (int i = 0; i < collection.Count; i++)
        {
            if (ReferenceEquals(collection[i], CurrentNode))
                return i - 1;
        }

        // Fallback: find last collection node that comes before CurrentNode in document order
        int currentId = CurrentNode.Id;
        for (int i = collection.Count - 1; i >= 0; i--)
        {
            if (collection[i].Id < currentId)
                return i;
        }

        return -1; // triggers wrap
    }

    private VBufferNode? PlayBoundaryAndReturnNull()
    {
        _audioCuePlayer.Play("boundary");
        return null;
    }
}
