using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Vox.Core.Braille.Hid;

/// <summary>
/// P/Invoke for HID devices: hid.dll (the HidD_ device calls and the HidP_ report parser),
/// setupapi.dll (finding HID device interfaces) and kernel32 overlapped file I/O. Layouts follow
/// hidpi.h, hidsdi.h, setupapi.h and minwinbase.h of the Windows 10 SDK.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class HidNative
{
    // ---- hidpi.h ----

    /// <summary>HIDP_REPORT_TYPE.</summary>
    public enum HidpReportType
    {
        Input = 0,
        Output = 1,
        Feature = 2,
    }

    /// <summary>HIDP_STATUS_SUCCESS (FACILITY_HID_ERROR_CODE 0x11, code 0).</summary>
    public const int HidpStatusSuccess = 0x00110000;

    /// <summary>HIDP_STATUS_BUFFER_TOO_SMALL.</summary>
    public const int HidpStatusBufferTooSmall = unchecked((int)0xC0110007);

    /// <summary>HIDP_CAPS (64 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        public fixed ushort Reserved[17];
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    /// <summary>
    /// The Range / NotRange union at the end of HIDP_BUTTON_CAPS and HIDP_VALUE_CAPS (16 bytes). In the
    /// NotRange form Usage, StringIndex, DesignatorIndex and DataIndex sit where UsageMin, StringMin,
    /// DesignatorMin and DataIndexMin are, and the *Max fields are reserved.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HidpRange
    {
        public ushort UsageMin;
        public ushort UsageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;

        /// <summary>NotRange.Usage.</summary>
        public readonly ushort Usage => UsageMin;

        /// <summary>NotRange.DataIndex.</summary>
        public readonly ushort DataIndex => DataIndexMin;
    }

    /// <summary>HIDP_BUTTON_CAPS (72 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct HidpButtonCaps
    {
        public ushort UsagePage;          // 0
        public byte ReportID;             // 2
        public byte IsAlias;              // 3  BOOLEAN
        public ushort BitField;           // 4
        public ushort LinkCollection;     // 6
        public ushort LinkUsage;          // 8
        public ushort LinkUsagePage;      // 10
        public byte IsRange;              // 12 BOOLEAN
        public byte IsStringRange;        // 13
        public byte IsDesignatorRange;    // 14
        public byte IsAbsolute;           // 15
        public ushort ReportCount;        // 16 (Windows 10 and later; reserved, so 0, before)
        public ushort Reserved2;          // 18
        public fixed uint Reserved[9];    // 20
        public HidpRange Range;           // 56
    }

    /// <summary>HIDP_VALUE_CAPS (72 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct HidpValueCaps
    {
        public ushort UsagePage;          // 0
        public byte ReportID;             // 2
        public byte IsAlias;              // 3
        public ushort BitField;           // 4
        public ushort LinkCollection;     // 6
        public ushort LinkUsage;          // 8
        public ushort LinkUsagePage;      // 10
        public byte IsRange;              // 12
        public byte IsStringRange;        // 13
        public byte IsDesignatorRange;    // 14
        public byte IsAbsolute;           // 15
        public byte HasNull;              // 16
        public byte Reserved;             // 17
        public ushort BitSize;            // 18
        public ushort ReportCount;        // 20
        public fixed ushort Reserved2[5]; // 22
        public uint UnitsExp;             // 32
        public uint Units;                // 36
        public int LogicalMin;            // 40
        public int LogicalMax;            // 44
        public int PhysicalMin;           // 48
        public int PhysicalMax;           // 52
        public HidpRange Range;           // 56
    }

    /// <summary>HIDP_DATA (8 bytes): for a button, the union's BOOLEAN On is the low byte of RawValue.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HidpData
    {
        public ushort DataIndex;
        public ushort Reserved;
        public uint RawValue;

        public readonly bool On => (RawValue & 0xFF) != 0;
    }

    [DllImport("hid.dll")]
    public static extern int HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);

    [DllImport("hid.dll")]
    public static extern unsafe int HidP_GetButtonCaps(
        HidpReportType reportType, HidpButtonCaps* buttonCaps, ref ushort buttonCapsLength, IntPtr preparsedData);

    [DllImport("hid.dll")]
    public static extern unsafe int HidP_GetValueCaps(
        HidpReportType reportType, HidpValueCaps* valueCaps, ref ushort valueCapsLength, IntPtr preparsedData);

    [DllImport("hid.dll")]
    public static extern uint HidP_MaxDataListLength(HidpReportType reportType, IntPtr preparsedData);

    [DllImport("hid.dll")]
    public static extern unsafe int HidP_GetData(
        HidpReportType reportType, HidpData* dataList, ref uint dataLength, IntPtr preparsedData,
        byte* report, uint reportLength);

    [DllImport("hid.dll")]
    public static extern unsafe int HidP_InitializeReportForID(
        HidpReportType reportType, byte reportId, IntPtr preparsedData, byte* report, uint reportLength);

    [DllImport("hid.dll")]
    public static extern unsafe int HidP_SetUsageValueArray(
        HidpReportType reportType, ushort usagePage, ushort linkCollection, ushort usage,
        byte* usageValue, ushort usageValueByteLength, IntPtr preparsedData, byte* report, uint reportLength);

    [DllImport("hid.dll")]
    public static extern unsafe int HidP_SetUsageValue(
        HidpReportType reportType, ushort usagePage, ushort linkCollection, ushort usage,
        uint usageValue, IntPtr preparsedData, byte* report, uint reportLength);

    // ---- hidsdi.h (these return BOOLEAN, one byte) ----

    /// <summary>HIDD_ATTRIBUTES (12 bytes); set Size before the call.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HiddAttributes
    {
        public uint Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [DllImport("hid.dll")]
    public static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetAttributes(SafeFileHandle device, ref HiddAttributes attributes);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern unsafe bool HidD_GetProductString(SafeFileHandle device, char* buffer, uint bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern unsafe bool HidD_SetOutputReport(SafeFileHandle device, byte* reportBuffer, uint reportBufferLength);

    // ---- setupapi.h ----

    public const uint DigcfPresent = 0x00000002;
    public const uint DigcfDeviceInterface = 0x00000010;

    /// <summary>SP_DEVICE_INTERFACE_DATA (28 bytes on x86, 32 on 64-bit).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SpDeviceInterfaceData
    {
        public uint CbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public UIntPtr Reserved;
    }

    /// <summary>
    /// cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: a DWORD and one WCHAR, 6 bytes on x86 (packed) and 8
    /// on 64-bit. DevicePath starts at offset 4 either way.
    /// </summary>
    public static uint DeviceInterfaceDetailDataSize => IntPtr.Size == 8 ? 8u : 6u;

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData, IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    // ---- kernel32 ----

    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint FileShareRead = 0x00000001;
    public const uint FileShareWrite = 0x00000002;
    public const uint OpenExisting = 3;
    public const uint FileFlagOverlapped = 0x40000000;
    public const int ErrorIoPending = 997;
    public const int ErrorOperationAborted = 995;

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern unsafe bool ReadFile(
        SafeFileHandle file, byte* buffer, uint numberOfBytesToRead, IntPtr numberOfBytesRead, NativeOverlapped* overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern unsafe bool WriteFile(
        SafeFileHandle file, byte* buffer, uint numberOfBytesToWrite, IntPtr numberOfBytesWritten, NativeOverlapped* overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern unsafe bool GetOverlappedResult(
        SafeFileHandle file, NativeOverlapped* overlapped, out uint numberOfBytesTransferred,
        [MarshalAs(UnmanagedType.Bool)] bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern unsafe bool CancelIoEx(SafeFileHandle file, NativeOverlapped* overlapped);
}
