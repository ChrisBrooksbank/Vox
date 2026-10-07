using Vox.Core.Buffer;

namespace Vox.Core.Tests.TestSupport;

/// <summary>How a <see cref="FaultyElement"/> misbehaves.</summary>
public enum ElementFault
{
    /// <summary>Reading its name throws (like a UIA element that has gone away).</summary>
    ThrowOnName,

    /// <summary>Listing its children throws.</summary>
    ThrowOnChildren,

    /// <summary>Listing its children blocks until <see cref="FaultyElement.Release"/> is set (a hung provider).</summary>
    HangOnChildren,
}

/// <summary>
/// Fault injection for the virtual buffer: an <see cref="IVBufferElement"/> that throws or hangs,
/// standing in for an unresponsive or failing accessibility provider.
/// </summary>
public sealed class FaultyElement : IVBufferElement
{
    private readonly List<IVBufferElement> _children = new();

    public FaultyElement(ElementFault fault, ManualResetEventSlim? release = null)
    {
        Fault = fault;
        Release = release ?? new ManualResetEventSlim();
    }

    public ElementFault Fault { get; }

    /// <summary>Unblocks a <see cref="ElementFault.HangOnChildren"/> element.</summary>
    public ManualResetEventSlim Release { get; }

    /// <summary>Set once a hanging element has started blocking.</summary>
    public ManualResetEventSlim Entered { get; } = new();

    public int[] RuntimeId { get; init; } = [99];
    public string ControlType { get; init; } = "Text";
    public string AriaRole { get; init; } = string.Empty;
    public string AriaProperties { get; init; } = string.Empty;
    public bool IsFocusable { get; init; }

    public string Name => Fault == ElementFault.ThrowOnName
        ? throw new System.Runtime.InteropServices.COMException("Element not available", unchecked((int)0x80040201))
        : "Faulty element";

    public FaultyElement AddChild(IVBufferElement child)
    {
        _children.Add(child);
        return this;
    }

    public IReadOnlyList<IVBufferElement> GetChildren()
    {
        switch (Fault)
        {
            case ElementFault.ThrowOnChildren:
                throw new System.Runtime.InteropServices.COMException("Element not available", unchecked((int)0x80040201));
            case ElementFault.HangOnChildren:
                Entered.Set();
                Release.Wait();
                break;
        }
        return _children;
    }
}
