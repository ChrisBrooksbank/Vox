using Vox.Core.Buffer;

namespace Vox.Core.Navigation;

/// <summary>
/// What to say when the browse cursor crosses a list boundary while reading: "out of list" for
/// each list left, then "list with 3 items" for each list entered, with "nesting level 2" for a
/// list inside another. Lists are those <see cref="PageElements.IsList"/> finds (not list boxes,
/// trees or menus); items are counted down to, not into, nested lists. Pure.
/// </summary>
public static class ListAnnouncer
{
    /// <summary>
    /// The list transition from the node the cursor was on to the node it is on now, or null when
    /// it stayed in the same lists (or either node is unknown).
    /// </summary>
    public static string? Transition(VBufferNode? from, VBufferNode? to)
    {
        if (from is null || to is null || ReferenceEquals(from, to))
            return null;

        var before = ListsAround(from);
        var after = ListsAround(to);
        int common = 0;
        while (common < before.Count && common < after.Count && ReferenceEquals(before[common], after[common]))
            common++;
        if (common == before.Count && common == after.Count)
            return null;

        var parts = new List<string>();
        for (int i = common; i < before.Count; i++)
            parts.Add("out of list");
        for (int i = common; i < after.Count; i++)
        {
            int items = ItemCount(after[i]);
            var text = $"list with {items} {(items == 1 ? "item" : "items")}";
            parts.Add(i > 0 ? $"{text}, nesting level {i + 1}" : text);
        }
        return string.Join(", ", parts);
    }

    /// <summary>The lists around <paramref name="node"/> (itself included), outermost first.</summary>
    public static IReadOnlyList<VBufferNode> ListsAround(VBufferNode? node)
    {
        var lists = new List<VBufferNode>();
        for (var n = node; n is not null; n = n.Parent)
        {
            if (PageElements.IsList(n))
                lists.Add(n);
        }
        lists.Reverse();
        return lists;
    }

    /// <summary>The list's own items: list items below it but not inside a nested list.</summary>
    public static int ItemCount(VBufferNode list)
    {
        int count = 0;
        var stack = new Stack<VBufferNode>(list.Children);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (PageElements.IsListItem(node))
                count++;
            // An item's own children may hold a nested list, whose items are its own
            if (PageElements.IsList(node))
                continue;
            foreach (var child in node.Children)
                stack.Push(child);
        }
        return count;
    }
}
