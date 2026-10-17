#pragma once

using namespace System;

namespace Vox
{
namespace NativeHelper
{
    /// Captures a Firefox document's IAccessible2 tree into JSON (Vox.Core's IA2SnapshotReader reads it).
    /// Every IA2 object is released before this returns; none reaches managed code.
    public ref class IA2Capture abstract sealed
    {
    public:
        /// True when IAccessible2 can be queried on the window's root accessible (the proxy/stub is available).
        static bool IsAvailable(IntPtr window);

        /// The snapshot of the document under `window` (a Firefox content window), or null on any failure.
        /// The tree is cut off after `maxNodes` nodes (0: 20000), never failing half-way.
        static String^ CaptureDocument(IntPtr window, int maxNodes);
    };
}
}
