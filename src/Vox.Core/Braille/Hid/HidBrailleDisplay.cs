using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32.SafeHandles;
using static Vox.Core.Braille.Hid.HidNative;

namespace Vox.Core.Braille.Hid;

/// <summary>
/// Driver for displays that follow the Windows HID braille standard (usage page 0x41), modelled on
/// NVDA's hidBrailleStandard driver. <see cref="TryConnect"/> opens the first HID device whose
/// top-level collection is a Braille Display with an 8-dot or 6-dot cell output; a background thread
/// reads its input reports and raises <see cref="KeyPressed"/> for each chord (<see cref="HidChordTracker"/>),
/// and a read or write failure (the display unplugged) closes it and raises <see cref="Disconnected"/>.
/// The device is opened shared, so other software can use it too.
/// </summary>
public sealed class HidBrailleDisplay : IBrailleDisplayDriver
{
    /// <summary>How long enumerating and opening devices may take before <see cref="TryConnect"/> gives up.</summary>
    private static readonly TimeSpan ConnectBudget = TimeSpan.FromSeconds(1);

    /// <summary>How long a write may take before the display is taken as gone.</summary>
    private const int WriteTimeoutMs = 1000;

    private readonly ILogger _logger;
    private readonly object _connectLock = new();
    private volatile Session? _session;
    private bool _disposed;

    public HidBrailleDisplay(ILogger<HidBrailleDisplay>? logger = null)
    {
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public string Id => "HID";

    /// <inheritdoc />
    public string Name => "HID braille display";

    /// <summary>The connected display's product string from the device, or null.</summary>
    public string? ProductName => _session is { IsOpen: true } session ? session.ProductName : null;

    /// <inheritdoc />
    public bool IsConnected => _session is { IsOpen: true };

    /// <inheritdoc />
    public int CellCount => _session is { IsOpen: true } session ? session.Layout.CellCount : 0;

    /// <inheritdoc />
    public event EventHandler<BrailleKeyEventArgs>? KeyPressed;

    /// <inheritdoc />
    public event EventHandler? Disconnected;

    /// <inheritdoc />
    public bool TryConnect()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        lock (_connectLock)
        {
            if (_disposed)
                return false;
            if (IsConnected)
                return true;
            try
            {
                var session = FindDisplay();
                if (session is null)
                    return false;
                _session = session;
                session.Start();
                _logger.LogInformation(
                    "HID braille display connected: {Product} ({Cells} {Dots}-dot cells)",
                    session.ProductName ?? "unnamed", session.Layout.CellCount, session.Layout.IsSixDot ? 6 : 8);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Looking for a HID braille display failed");
                return false;
            }
        }
    }

    /// <inheritdoc />
    public void Write(ReadOnlyMemory<byte> cells, int? cursorPosition)
    {
        // The HID braille report has no cursor field; the cells already show it.
        var session = _session;
        if (session is not { IsOpen: true })
            return;
        try
        {
            session.Write(cells.Span);
        }
        catch (Exception ex)
        {
            // Not the cells: they may hold typed text.
            _logger.LogWarning(ex, "Writing to the HID braille display failed");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Session? session;
        lock (_connectLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            session = _session;
            _session = null;
        }
        session?.Close();
    }

    private void OnKeys(BrailleKeyEventArgs chord)
    {
        try
        {
            KeyPressed?.Invoke(this, chord);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A braille key handler failed");
        }
    }

    private void OnLost(Session session)
    {
        // Raised once per connection: only the session still current is reported.
        if (Interlocked.CompareExchange(ref _session, null, session) != session)
            return;
        _logger.LogInformation("HID braille display disconnected");
        try
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A braille disconnect handler failed");
        }
    }

    [SupportedOSPlatform("windows")]
    private Session? FindDisplay()
    {
        var clock = Stopwatch.StartNew();
        HidD_GetHidGuid(out var hidGuid);
        var infoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (infoSet == new IntPtr(-1))
        {
            _logger.LogDebug("Listing HID devices failed ({Error})", Marshal.GetLastPInvokeError());
            return null;
        }
        try
        {
            for (uint index = 0; clock.Elapsed < ConnectBudget; index++)
            {
                var interfaceData = new SpDeviceInterfaceData { CbSize = (uint)Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                    return null; // ERROR_NO_MORE_ITEMS
                var path = DevicePath(infoSet, ref interfaceData);
                if (path is null)
                    continue;
                var session = TryOpen(path);
                if (session is not null)
                    return session;
            }
            _logger.LogDebug("Gave up looking for a HID braille display after {Ms} ms", clock.ElapsedMilliseconds);
            return null;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(infoSet);
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? DevicePath(IntPtr infoSet, ref SpDeviceInterfaceData interfaceData)
    {
        // The first call fails with ERROR_INSUFFICIENT_BUFFER and gives the size.
        SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, IntPtr.Zero, 0, out var size, IntPtr.Zero);
        if (size < DeviceInterfaceDetailDataSize)
            return null;
        var detail = Marshal.AllocHGlobal((int)size);
        try
        {
            Marshal.WriteInt32(detail, (int)DeviceInterfaceDetailDataSize);
            if (!SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, detail, size, out _, IntPtr.Zero))
                return null;
            return Marshal.PtrToStringUni(detail + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(detail);
        }
    }

    [SupportedOSPlatform("windows")]
    private unsafe Session? TryOpen(string path)
    {
        // Keyboards and mice are opened exclusively by Windows: CreateFile fails and they are skipped.
        var handle = CreateFile(
            path, GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero,
            OpenExisting, FileFlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        var preparsed = IntPtr.Zero;
        var keep = false;
        try
        {
            if (!HidD_GetPreparsedData(handle, out preparsed))
                return null;
            if (HidP_GetCaps(preparsed, out var caps) != HidpStatusSuccess)
                return null;
            if (caps.UsagePage != HidBrailleUsages.UsagePage || caps.Usage != HidBrailleUsages.BrailleDisplay)
                return null;

            var outputValues = GetValueCaps(HidpReportType.Output, caps.NumberOutputValueCaps, preparsed);
            HidpValueCaps? cellCaps = null;
            foreach (var valueCaps in outputValues)
            {
                if (!IsCellCaps(valueCaps))
                    continue;
                if (cellCaps is null || (valueCaps.Range.Usage == HidBrailleUsages.EightDotCell
                    && cellCaps.Value.Range.Usage != HidBrailleUsages.EightDotCell))
                    cellCaps = valueCaps;
            }
            if (cellCaps is not { } cell)
            {
                _logger.LogDebug("HID braille device without an 8-dot or 6-dot cell output: {Path}", path);
                return null;
            }

            var inputButtons = GetButtonCaps(HidpReportType.Input, caps.NumberInputButtonCaps, preparsed);
            var inputValues = GetValueCaps(HidpReportType.Input, caps.NumberInputValueCaps, preparsed);
            var layout = HidBrailleLayout.Create(
                cell.ReportID, caps.OutputReportByteLength, cell.Range.Usage, cell.LinkCollection,
                cell.ReportCount, cell.BitSize,
                ButtonRanges(inputButtons, inputValues, caps.NumberInputDataIndices));

            var maxData = HidP_MaxDataListLength(HidpReportType.Input, preparsed);
            var session = new Session(
                this, handle, preparsed, layout, caps.InputReportByteLength, maxData, ProductString(handle));
            keep = true;
            return session;
        }
        finally
        {
            if (!keep)
            {
                if (preparsed != IntPtr.Zero)
                    HidD_FreePreparsedData(preparsed);
                handle.Dispose();
            }
        }
    }

    private static bool IsCellCaps(in HidpValueCaps caps) =>
        caps.UsagePage == HidBrailleUsages.UsagePage
        && (caps.IsRange == 0 || caps.Range.UsageMin == caps.Range.UsageMax)
        && caps.Range.Usage is HidBrailleUsages.EightDotCell or HidBrailleUsages.SixDotCell
        && caps.ReportCount > 0
        && caps.BitSize is >= 1 and <= 8;

    /// <summary>The input button capabilities as ranges of data indices; aliases are left out.</summary>
    private static List<HidButtonRange> ButtonRanges(
        HidpButtonCaps[] buttons, HidpValueCaps[] values, int totalDataIndices)
    {
        var starts = buttons.Select(b => (int)b.Range.DataIndexMin)
            .Concat(values.Select(v => (int)v.Range.DataIndexMin))
            .Distinct()
            .ToList();
        var ranges = new List<HidButtonRange>(buttons.Length);
        foreach (var b in buttons)
        {
            // An aliased usage shares the data index of the entry that follows it.
            if (b.IsAlias != 0)
                continue;
            var first = b.Range.DataIndexMin;
            int count = b.IsRange != 0
                ? b.Range.DataIndexMax - b.Range.DataIndexMin + 1
                : HidBrailleLayout.NotRangeDataIndexCount(first, b.ReportCount, starts, totalDataIndices);
            if (count <= 0)
                continue;
            var usageMin = b.Range.UsageMin;
            var usageMax = b.IsRange != 0 ? b.Range.UsageMax : usageMin;
            ranges.Add(new HidButtonRange(
                b.UsagePage, b.LinkCollection, b.LinkUsagePage, b.LinkUsage,
                usageMin, usageMax, first, (ushort)(first + count - 1)));
        }
        return ranges;
    }

    [SupportedOSPlatform("windows")]
    private static unsafe HidpValueCaps[] GetValueCaps(HidpReportType type, ushort count, IntPtr preparsed)
    {
        if (count == 0)
            return [];
        var caps = new HidpValueCaps[count];
        var length = count;
        fixed (HidpValueCaps* p = caps)
        {
            if (HidP_GetValueCaps(type, p, ref length, preparsed) != HidpStatusSuccess)
                return [];
        }
        return length < count ? caps[..length] : caps;
    }

    [SupportedOSPlatform("windows")]
    private static unsafe HidpButtonCaps[] GetButtonCaps(HidpReportType type, ushort count, IntPtr preparsed)
    {
        if (count == 0)
            return [];
        var caps = new HidpButtonCaps[count];
        var length = count;
        fixed (HidpButtonCaps* p = caps)
        {
            if (HidP_GetButtonCaps(type, p, ref length, preparsed) != HidpStatusSuccess)
                return [];
        }
        return length < count ? caps[..length] : caps;
    }

    [SupportedOSPlatform("windows")]
    private static unsafe string? ProductString(SafeFileHandle handle)
    {
        // USB string descriptors hold at most 126 characters; the buffer leaves room for the terminator.
        const int Chars = 256;
        var buffer = stackalloc char[Chars];
        if (!HidD_GetProductString(handle, buffer, Chars * sizeof(char)))
            return null;
        var text = new string(buffer, 0, Chars);
        var end = text.IndexOf('\0');
        text = (end >= 0 ? text[..end] : text).Trim();
        return text.Length > 0 ? text : null;
    }

    /// <summary>
    /// One open display: the handle, its preparsed data, the read thread and the write buffers. The read
    /// thread owns the release of the native resources (it is their last user); writes hold
    /// <see cref="_writeLock"/>, which the release takes too, so they never see freed memory.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private sealed unsafe class Session
    {
        private readonly HidBrailleDisplay _owner;
        private readonly SafeFileHandle _handle;
        private readonly IntPtr _preparsed;
        private readonly int _inputReportLength;
        private readonly uint _maxDataListLength;
        private readonly object _writeLock = new();
        private readonly ManualResetEvent _writeEvent = new(false);
        private readonly NativeOverlapped* _writeOverlapped;
        private readonly byte* _writeBuffer;
        private Thread? _readThread;
        private volatile bool _closing;
        private volatile bool _lost;
        private volatile bool _released;
        private byte[]? _lastValues;
        private bool _useSetOutputReport;

        public Session(
            HidBrailleDisplay owner, SafeFileHandle handle, IntPtr preparsed, HidBrailleLayout layout,
            int inputReportLength, uint maxDataListLength, string? productName)
        {
            _owner = owner;
            _handle = handle;
            _preparsed = preparsed;
            Layout = layout;
            _inputReportLength = inputReportLength;
            _maxDataListLength = Math.Max(1, maxDataListLength);
            ProductName = productName;
            _writeOverlapped = (NativeOverlapped*)NativeMemory.AllocZeroed((nuint)sizeof(NativeOverlapped));
            _writeBuffer = (byte*)NativeMemory.AllocZeroed((nuint)Math.Max(1, layout.OutputReportLength));
            _writeOverlapped->EventHandle = _writeEvent.SafeWaitHandle.DangerousGetHandle();
        }

        public HidBrailleLayout Layout { get; }

        public string? ProductName { get; }

        public bool IsOpen => !_closing && !_lost && !_released;

        private ILogger Logger => _owner._logger;

        public void Start()
        {
            if (_inputReportLength <= 0)
                return; // no keys: nothing to read, and a lost display shows as a failed write
            _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "HID braille input" };
            _readThread.Start();
        }

        /// <summary>Stops reading and releases the device; waits up to a second for the read thread.</summary>
        public void Close()
        {
            _closing = true;
            lock (_writeLock)
            {
                if (_released)
                    return;
                if (_readThread is null)
                {
                    Release();
                    return;
                }
                CancelIoEx(_handle, null);
            }
            if (_readThread != Thread.CurrentThread)
                _readThread.Join(TimeSpan.FromSeconds(1));
        }

        public void Write(ReadOnlySpan<byte> cells)
        {
            var values = Layout.UsageValueArray(cells);
            var lost = false;
            lock (_writeLock)
            {
                if (!IsOpen)
                    return;
                if (_lastValues is not null && _lastValues.AsSpan().SequenceEqual(values))
                    return;
                if (!BuildReport(values))
                    return;
                if (Send())
                {
                    _lastValues = values;
                    return;
                }

                // The display is gone (or no longer answers): stop the read thread, which reports it.
                _lost = true;
                if (_readThread is null)
                {
                    Release();
                    lost = true;
                }
                else
                {
                    CancelIoEx(_handle, null);
                }
            }
            if (lost)
                _owner.OnLost(this);
        }

        private bool BuildReport(byte[] values)
        {
            var length = (uint)Layout.OutputReportLength;
            int status = HidP_InitializeReportForID(
                HidpReportType.Output, Layout.OutputReportId, _preparsed, _writeBuffer, length);
            if (status != HidpStatusSuccess)
            {
                Logger.LogWarning("Building a HID braille output report failed (0x{Status:X8})", status);
                return false;
            }
            fixed (byte* v = values)
            {
                status = Layout.CellCount == 1
                    ? HidP_SetUsageValue(
                        HidpReportType.Output, HidBrailleUsages.UsagePage, Layout.CellLinkCollection, Layout.CellUsage,
                        values[0], _preparsed, _writeBuffer, length)
                    : HidP_SetUsageValueArray(
                        HidpReportType.Output, HidBrailleUsages.UsagePage, Layout.CellLinkCollection, Layout.CellUsage,
                        v, (ushort)values.Length, _preparsed, _writeBuffer, length);
            }
            if (status != HidpStatusSuccess)
            {
                Logger.LogWarning("Putting the cells in a HID braille output report failed (0x{Status:X8})", status);
                return false;
            }
            return true;
        }

        /// <summary>Sends the report in the write buffer: WriteFile, else HidD_SetOutputReport (control pipe).</summary>
        private bool Send()
        {
            var length = (uint)Layout.OutputReportLength;
            if (!_useSetOutputReport)
            {
                if (WriteOverlapped(length, out var error))
                    return true;
                if (HidD_SetOutputReport(_handle, _writeBuffer, length))
                {
                    // Some displays take output reports only through the control pipe.
                    Logger.LogInformation(
                        "HID braille display refused WriteFile ({Error}); using HidD_SetOutputReport", error);
                    _useSetOutputReport = true;
                    return true;
                }
                Logger.LogInformation("Writing to the HID braille display failed ({Error})", error);
                return false;
            }
            if (HidD_SetOutputReport(_handle, _writeBuffer, length))
                return true;
            Logger.LogInformation("HidD_SetOutputReport failed ({Error})", Marshal.GetLastPInvokeError());
            return false;
        }

        private bool WriteOverlapped(uint length, out int error)
        {
            error = 0;
            _writeEvent.Reset();
            if (!WriteFile(_handle, _writeBuffer, length, IntPtr.Zero, _writeOverlapped))
            {
                error = Marshal.GetLastPInvokeError();
                if (error != ErrorIoPending)
                    return false;
            }
            if (!_writeEvent.WaitOne(WriteTimeoutMs))
            {
                // Cancel, then wait for the cancellation so the buffer and OVERLAPPED are free again.
                CancelIoEx(_handle, _writeOverlapped);
                GetOverlappedResult(_handle, _writeOverlapped, out _, wait: true);
                error = ErrorOperationAborted;
                return false;
            }
            if (!GetOverlappedResult(_handle, _writeOverlapped, out var written, wait: false))
            {
                error = Marshal.GetLastPInvokeError();
                return false;
            }
            return written > 0;
        }

        private void ReadLoop()
        {
            var buffer = (byte*)NativeMemory.AllocZeroed((nuint)_inputReportLength);
            var overlapped = (NativeOverlapped*)NativeMemory.AllocZeroed((nuint)sizeof(NativeOverlapped));
            var readEvent = new ManualResetEvent(false);
            overlapped->EventHandle = readEvent.SafeWaitHandle.DangerousGetHandle();
            var data = new HidpData[_maxDataListLength];
            var tracker = new HidChordTracker(Layout);
            var onByReport = new Dictionary<byte, int[]>();
            try
            {
                while (!_closing && !_lost)
                {
                    new Span<byte>(buffer, _inputReportLength).Clear();
                    readEvent.Reset();
                    if (!ReadFile(_handle, buffer, (uint)_inputReportLength, IntPtr.Zero, overlapped))
                    {
                        var error = Marshal.GetLastPInvokeError();
                        if (error != ErrorIoPending)
                        {
                            LogReadError(error);
                            break;
                        }
                    }
                    // Waits for the report, or for CancelIoEx from Close or a failed write.
                    if (!GetOverlappedResult(_handle, overlapped, out var read, wait: true))
                    {
                        LogReadError(Marshal.GetLastPInvokeError());
                        break;
                    }
                    if (read == 0 || _closing)
                        continue;
                    try
                    {
                        HandleReport(buffer, data, tracker, onByReport);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Handling a HID braille input report failed");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "HID braille input stopped");
            }
            finally
            {
                // Every read has completed here (GetOverlappedResult waited, or ReadFile failed at once).
                NativeMemory.Free(buffer);
                NativeMemory.Free(overlapped);
                readEvent.Dispose();
                lock (_writeLock)
                    Release();
                if (!_closing)
                    _owner.OnLost(this);
            }
        }

        private void LogReadError(int error)
        {
            if (!_closing && !_lost)
                Logger.LogInformation("Reading from the HID braille display failed ({Error})", error);
        }

        private void HandleReport(
            byte* report, HidpData[] data, HidChordTracker tracker, Dictionary<byte, int[]> onByReport)
        {
            var count = (uint)data.Length;
            int status;
            fixed (HidpData* p = data)
                status = HidP_GetData(HidpReportType.Input, p, ref count, _preparsed, report, (uint)_inputReportLength);
            if (status != HidpStatusSuccess)
                return; // a report this parser doesn't describe (or with nothing for us)

            var on = new List<int>((int)count);
            for (var i = 0; i < count; i++)
            {
                if (data[i].On)
                    on.Add(data[i].DataIndex);
            }
            // Keys may be spread over several input reports (report ids); each report only says its own.
            onByReport[report[0]] = on.ToArray();
            var chord = tracker.Update(onByReport.Values.SelectMany(indices => indices));
            if (chord is not null)
                _owner.OnKeys(chord);
        }

        /// <summary>Frees the native resources once; callers hold <see cref="_writeLock"/>.</summary>
        private void Release()
        {
            if (_released)
                return;
            _released = true;
            HidD_FreePreparsedData(_preparsed);
            _handle.Dispose();
            NativeMemory.Free(_writeOverlapped);
            NativeMemory.Free(_writeBuffer);
            _writeEvent.Dispose();
        }
    }
}
