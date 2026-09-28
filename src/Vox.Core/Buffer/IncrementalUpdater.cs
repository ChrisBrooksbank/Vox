namespace Vox.Core.Buffer;

/// <summary>
/// Applies incremental updates to a <see cref="VBufferDocument"/> when UIA StructureChanged events arrive.
///
/// On a StructureChanged event, the caller provides the RuntimeId of the changed subtree root
/// and a new <see cref="IVBufferElement"/> for that subtree.  The updater:
///   1. Locates the existing node in the document by RuntimeId.
///   2. Rebuilds only the changed subtree into new <see cref="VBufferNode"/>s.
///   3. Splices the new subtree into document order in place of the old subtree.
///   4. Recalculates text offsets for all nodes shifted downstream by the splice.
///   5. Returns a new <see cref="VBufferDocument"/> reflecting the update.
///
/// The input document is never modified: every node of the result is a fresh copy with
/// Ids renumbered in document order, so holders of the old snapshot stay consistent.
///
/// If the RuntimeId is not found the full document is returned unchanged.
/// </summary>
public sealed class IncrementalUpdater
{
    /// <summary>
    /// Applies an incremental update for the subtree identified by <paramref name="changedRuntimeId"/>.
    /// </summary>
    /// <param name="document">The current document snapshot.</param>
    /// <param name="changedRuntimeId">RuntimeId of the UIA element whose subtree changed.</param>
    /// <param name="newSubtreeRoot">
    ///     The new <see cref="IVBufferElement"/> subtree root to splice in.
    ///     Pass <c>null</c> to remove the subtree (element was deleted).
    /// </param>
    /// <returns>
    ///     A new <see cref="VBufferDocument"/> with the change applied,
    ///     or the original <paramref name="document"/> if the runtime ID was not found.
    /// </returns>
    public VBufferDocument ApplyUpdate(
        VBufferDocument document,
        int[] changedRuntimeId,
        IVBufferElement? newSubtreeRoot)
    {
        var oldSubtreeRoot = document.FindByRuntimeId(changedRuntimeId);
        if (oldSubtreeRoot is null)
            return document;

        // Removing the document root would leave no valid document
        if (oldSubtreeRoot.Parent is null && newSubtreeRoot is null)
            return document;

        // Collect all nodes in the old subtree (pre-order).
        var oldSubtreeNodes = CollectSubtree(oldSubtreeRoot);
        int oldSubtreeCount = oldSubtreeNodes.Count;

        // Find the position of the old subtree root in document order.
        var oldNodes = document.AllNodes;
        int insertIndex = -1;
        for (int i = 0; i < oldNodes.Count; i++)
        {
            if (ReferenceEquals(oldNodes[i], oldSubtreeRoot))
            {
                insertIndex = i;
                break;
            }
        }

        if (insertIndex < 0)
            return document; // safety guard

        // Build the replacement subtree (or use empty if deletion). Offsets start at 0.
        List<VBufferNode> newSubtreeNodes;
        string newSubtreeText;

        if (newSubtreeRoot is not null)
        {
            (newSubtreeNodes, newSubtreeText) = VBufferBuilder.BuildSubtree(newSubtreeRoot);
        }
        else
        {
            newSubtreeNodes = [];
            newSubtreeText = string.Empty;
        }

        // Determine the text span covered by the old subtree.
        int oldTextStart = oldSubtreeRoot.TextRange.Start;
        int oldTextEnd   = OldSubtreeTextEnd(oldSubtreeNodes, oldTextStart);
        int oldTextLen   = oldTextEnd - oldTextStart;

        // Build new FlatText by splicing.
        string oldFlatText = document.FlatText;
        string newFlatText =
            oldFlatText[..oldTextStart] +
            newSubtreeText +
            oldFlatText[oldTextEnd..];

        int textDelta = newSubtreeText.Length - oldTextLen;

        // Merged document order, as (source node, text offset shift) pairs.
        var merged = new List<(VBufferNode Source, int Shift)>(
            oldNodes.Count - oldSubtreeCount + newSubtreeNodes.Count);

        for (int i = 0; i < insertIndex; i++)
            merged.Add((oldNodes[i], 0));

        foreach (var n in newSubtreeNodes)
            merged.Add((n, oldTextStart));

        for (int i = insertIndex + oldSubtreeCount; i < oldNodes.Count; i++)
            merged.Add((oldNodes[i], textDelta));

        // Copy every node with its final Id and offsets, then rebuild the tree links.
        var copies = new Dictionary<VBufferNode, VBufferNode>(ReferenceEqualityComparer.Instance);
        var allNewNodes = new List<VBufferNode>(merged.Count);

        for (int i = 0; i < merged.Count; i++)
        {
            var (source, shift) = merged[i];
            var copy = source.CloneDetached(i, (source.TextRange.Start + shift, source.TextRange.End + shift));
            copies[source] = copy;
            allNewNodes.Add(copy);
        }

        var newRoot = newSubtreeNodes.Count > 0 ? newSubtreeNodes[0] : null;
        for (int i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source;
            var copy = allNewNodes[i];

            // The new subtree root takes the old root's place under the old parent
            var sourceParent = ReferenceEquals(source, newRoot) ? oldSubtreeRoot.Parent : source.Parent;
            if (sourceParent is not null && copies.TryGetValue(sourceParent, out var parentCopy))
            {
                copy.Parent = parentCopy;
                // Pre-order iteration appends children in document order
                parentCopy.Children.Add(copy);
            }

            if (i > 0)
            {
                copy.PrevInOrder = allNewNodes[i - 1];
                allNewNodes[i - 1].NextInOrder = copy;
            }
        }

        return new VBufferDocument(newFlatText, allNewNodes[0], allNewNodes);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Collects all nodes in the subtree rooted at <paramref name="node"/> in pre-order.</summary>
    private static List<VBufferNode> CollectSubtree(VBufferNode node)
    {
        var result = new List<VBufferNode>();
        var stack = new Stack<VBufferNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            result.Add(current);
            for (int i = current.Children.Count - 1; i >= 0; i--)
                stack.Push(current.Children[i]);
        }
        return result;
    }

    /// <summary>
    /// Returns the exclusive end offset of the text span covered by all nodes in the subtree.
    /// </summary>
    private static int OldSubtreeTextEnd(List<VBufferNode> subtreeNodes, int start)
    {
        int end = start;
        foreach (var n in subtreeNodes)
            if (n.TextRange.End > end)
                end = n.TextRange.End;
        return end;
    }
}
