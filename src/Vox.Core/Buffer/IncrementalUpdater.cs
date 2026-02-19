using System.Text;

namespace Vox.Core.Buffer;

/// <summary>
/// Applies incremental updates to a <see cref="VBufferDocument"/> when UIA StructureChanged events arrive.
///
/// On a StructureChanged event, the caller provides the RuntimeId of the changed subtree root
/// and a new <see cref="IVBufferElement"/> for that subtree.  The updater:
///   1. Locates the existing node in the document by RuntimeId.
///   2. Rebuilds only the changed subtree into new <see cref="VBufferNode"/>s.
///   3. Splices the new subtree into the existing document in place of the old subtree.
///   4. Recalculates text offsets for all nodes shifted downstream by the splice.
///   5. Returns a new <see cref="VBufferDocument"/> reflecting the update.
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

        // Build the replacement subtree (or use empty if deletion).
        List<VBufferNode> newSubtreeNodes;
        string newSubtreeText;

        if (newSubtreeRoot is not null)
        {
            (newSubtreeNodes, newSubtreeText) = BuildSubtree(newSubtreeRoot, oldSubtreeRoot.Parent);
        }
        else
        {
            newSubtreeNodes = [];
            newSubtreeText = string.Empty;
        }

        // Determine the text span covered by the old subtree.
        int oldTextStart = oldSubtreeRoot.TextRange.Start;
        int oldTextEnd   = OldSubtreeTextEnd(oldSubtreeNodes);
        int oldTextLen   = oldTextEnd - oldTextStart;

        // Build new FlatText by splicing.
        string oldFlatText = document.FlatText;
        string newFlatText =
            oldFlatText[..oldTextStart] +
            newSubtreeText +
            oldFlatText[oldTextEnd..];

        int textDelta = newSubtreeText.Length - oldTextLen;

        // Build the merged AllNodes list.
        var allNewNodes = new List<VBufferNode>(
            oldNodes.Count - oldSubtreeCount + newSubtreeNodes.Count);

        // Nodes before the changed subtree (unmodified, but may need text offset shift — no, they come before).
        for (int i = 0; i < insertIndex; i++)
            allNewNodes.Add(oldNodes[i]);

        // Shift text offsets for new subtree nodes (built relative to offset 0) to correct position.
        foreach (var n in newSubtreeNodes)
        {
            n.TextRange = (n.TextRange.Start + oldTextStart,
                           n.TextRange.End   + oldTextStart);
            allNewNodes.Add(n);
        }

        // Nodes after the old subtree: shift text offsets by delta.
        for (int i = insertIndex + oldSubtreeCount; i < oldNodes.Count; i++)
        {
            var n = oldNodes[i];
            if (textDelta != 0)
                n.TextRange = (n.TextRange.Start + textDelta, n.TextRange.End + textDelta);
            allNewNodes.Add(n);
        }

        // Relink the doubly-linked document-order list.
        RelinkDocumentOrder(allNewNodes);

        // Repair parent–child pointer at splice boundary.
        RepairParentChildPointers(oldSubtreeRoot, newSubtreeNodes);

        // VBufferDocument constructor requires at least one node (the document root).
        if (allNewNodes.Count == 0)
            return document; // cannot produce a valid document

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
    /// Builds a new subtree from <paramref name="element"/>, using VBufferBuilder's parsing logic.
    /// Returns the nodes in pre-order and the flat text they produce (starting from offset 0).
    /// </summary>
    private static (List<VBufferNode> Nodes, string FlatText) BuildSubtree(
        IVBufferElement element,
        VBufferNode? parentNode)
    {
        var nodes    = new List<VBufferNode>();
        var flatText = new StringBuilder();
        int nextId   = 0;
        VBufferNode? prevInOrder = null;

        var stack = new Stack<(IVBufferElement Element, VBufferNode? Parent)>();
        stack.Push((element, parentNode));

        while (stack.Count > 0)
        {
            var (el, parent) = stack.Pop();
            var node = BuildNode(el, parent, nextId++, flatText);
            nodes.Add(node);

            if (prevInOrder is not null)
            {
                prevInOrder.NextInOrder = node;
                node.PrevInOrder = prevInOrder;
            }
            prevInOrder = node;

            var children = el.GetChildren();
            for (int i = children.Count - 1; i >= 0; i--)
                stack.Push((children[i], node));
        }

        return (nodes, flatText.ToString());
    }

    private static VBufferNode BuildNode(
        IVBufferElement element,
        VBufferNode? parent,
        int id,
        StringBuilder flatText)
    {
        var ariaRole  = element.AriaRole;
        var ariaProps = element.AriaProperties;

        var headingLevel = VBufferBuilder.ParseHeadingLevel_Internal(ariaRole, ariaProps, element.ControlType);
        var landmarkType = VBufferBuilder.ParseLandmarkType_Internal(ariaRole);
        var isLink       = VBufferBuilder.IsLinkElement_Internal(ariaRole, element.ControlType);
        var isVisited    = VBufferBuilder.ParseAriaPropertyBool(ariaProps, "visited");
        var isRequired   = VBufferBuilder.ParseAriaPropertyBool(ariaProps, "required");
        var isExpanded   = VBufferBuilder.ParseAriaPropertyBool(ariaProps, "expanded");
        var isExpandable = VBufferBuilder.ParseAriaPropertyBool(ariaProps, "haspopup") || isExpanded ||
                           string.Equals(element.ControlType, "ComboBox", StringComparison.OrdinalIgnoreCase);
        var isFocusable  = element.IsFocusable ||
                           VBufferBuilder.IsFocusableControlType(element.ControlType) ||
                           isLink;

        var textStart = flatText.Length;
        VBufferBuilder.AppendNodeText_Internal(element, flatText);
        var textEnd = flatText.Length;

        var node = new VBufferNode
        {
            Id           = id,
            UIARuntimeId = element.RuntimeId,
            Name         = element.Name,
            ControlType  = element.ControlType,
            AriaRole     = ariaRole,
            HeadingLevel = headingLevel,
            LandmarkType = landmarkType,
            IsLink       = isLink,
            IsVisited    = isVisited,
            IsRequired   = isRequired,
            IsExpandable = isExpandable,
            IsExpanded   = isExpanded,
            IsFocusable  = isFocusable,
            TextRange    = (textStart, textEnd),
            Parent       = parent,
        };

        parent?.Children.Add(node);
        return node;
    }

    /// <summary>
    /// Returns the exclusive end offset of the text span covered by all nodes in the subtree.
    /// </summary>
    private static int OldSubtreeTextEnd(List<VBufferNode> subtreeNodes)
    {
        int end = 0;
        foreach (var n in subtreeNodes)
            if (n.TextRange.End > end)
                end = n.TextRange.End;
        return end;
    }

    /// <summary>Rebuilds the doubly-linked NextInOrder/PrevInOrder list.</summary>
    private static void RelinkDocumentOrder(List<VBufferNode> nodes)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            nodes[i].PrevInOrder = i > 0               ? nodes[i - 1] : null;
            nodes[i].NextInOrder = i < nodes.Count - 1 ? nodes[i + 1] : null;
        }
    }

    /// <summary>
    /// Repairs the parent's Children list: replaces <paramref name="oldRoot"/> with the
    /// new subtree root (if any), or removes it (if deletion).
    /// </summary>
    private static void RepairParentChildPointers(
        VBufferNode oldRoot,
        List<VBufferNode> newSubtreeNodes)
    {
        var parent = oldRoot.Parent;
        if (parent is null) return;

        int childIndex = parent.Children.IndexOf(oldRoot);
        if (childIndex < 0) return;

        if (newSubtreeNodes.Count > 0)
        {
            var newRoot = newSubtreeNodes[0];
            parent.Children[childIndex] = newRoot;
            newRoot.Parent = parent;
        }
        else
        {
            parent.Children.RemoveAt(childIndex);
        }
    }
}
