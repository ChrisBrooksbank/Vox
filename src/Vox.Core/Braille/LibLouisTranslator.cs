using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Vox.Core.Braille;

/// <summary>
/// <see cref="IBrailleTranslator"/> over liblouis, loaded from the component folder (liblouis is
/// LGPL and installed separately; Vox only calls it). The table list is fixed at construction; a
/// table change in settings makes a new translator. liblouis keeps global state and is not
/// thread-safe, so every call is made under one process-wide lock. Use <see cref="TryLoad"/>.
/// </summary>
public sealed class LibLouisTranslator : IBrailleTranslator
{
    /// <summary>The library's file name on Windows.</summary>
    public const string LibraryFileName = "liblouis.dll";

    // translationModes in liblouis.h
    private const int CompbrlAtCursor = 2;
    private const int DotsIO = 4;
    private const int UcBrl = 64;
    private const int UnicodeBrailleBase = 0x2800;
    // LOU_DOTS: a widechar holding a dot pattern, the form dotsIO back-translation reads
    private const int LouDots = 0x8000;
    private const int MaxAttempts = 8;

    // Library names tried in the folder, Windows first; the others let the integration test run elsewhere
    private static readonly string[] LibraryNames = [LibraryFileName, "liblouis.so.20", "liblouis.so", "liblouis.dylib"];

    // Table folders tried inside the component folder
    private static readonly string[] TableFolders = ["tables", Path.Combine("share", "liblouis", "tables")];

    private static readonly object CallLock = new();
    private static readonly Dictionary<string, Api> LoadedLibraries = new(StringComparer.OrdinalIgnoreCase);

    private readonly Api _api;
    private readonly IntPtr _tableList;
    private readonly bool _expandWordAtCursor;
    private readonly ILogger? _logger;

    private LibLouisTranslator(Api api, string tableList, bool expandWordAtCursor, ILogger? logger)
    {
        _api = api;
        Table = tableList;
        // Freed never: a translator lives as long as its table setting, and liblouis caches the table anyway
        _tableList = Marshal.StringToCoTaskMemUTF8(tableList);
        _expandWordAtCursor = expandWordAtCursor;
        _logger = logger;
    }

    /// <summary>The table list passed to liblouis (a table name, or its full path inside the component folder).</summary>
    public string Table { get; }

    /// <summary>%LOCALAPPDATA%\Vox\components\liblouis: the library and its <c>tables</c> folder.</summary>
    public static string DefaultDirectory => Path.Combine(Lifecycle.VoxPaths.Components, "liblouis");

    /// <summary>True when the liblouis library is in <paramref name="directory"/>.</summary>
    public static bool IsInstalled(string directory) =>
        LibraryNames.Any(name => File.Exists(Path.Combine(directory, name)));

    /// <summary>
    /// liblouis from <paramref name="directory"/> with <paramref name="table"/> (a liblouis table
    /// list, as in "en-ueb-g2.ctb"), or null (logged) when the library or the table can't be loaded.
    /// Tables are looked for in the folder's <c>tables</c> (or <c>share/liblouis/tables</c>) folder,
    /// then where liblouis itself looks. With <paramref name="expandWordAtCursor"/>, the word at the
    /// cursor is written uncontracted (liblouis's compbrlAtCursor).
    /// </summary>
    public static LibLouisTranslator? TryLoad(string directory, string table, ILogger? logger = null, bool expandWordAtCursor = false)
    {
        try
        {
            lock (CallLock)
            {
                var api = LoadApi(directory, logger);
                if (api is null)
                    return null;

                var tableList = ResolveTables(directory, table);
                var tableListUtf8 = Marshal.StringToCoTaskMemUTF8(tableList);
                try
                {
                    if (api.GetTable(tableListUtf8) == IntPtr.Zero)
                    {
                        logger?.LogWarning("liblouis could not load braille table {Table}", tableList);
                        return null;
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(tableListUtf8);
                }
                return new LibLouisTranslator(api, tableList, expandWordAtCursor, logger);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger?.LogWarning(ex, "liblouis could not be loaded from {Directory}", directory);
            return null;
        }
    }

    public BrailleTranslation Translate(string text, int? cursorOffset = null, bool expandWordAtCursor = false)
    {
        if (text.Length == 0)
            return cursorOffset is null ? BrailleTranslation.Empty : BrailleTranslation.Empty with { CursorCell = 0 };

        // The text as liblouis widechars (UTF-16 units, or code points when widechar is 32-bit),
        // with the string offset of each
        var (input, inputOffsets) = Encode(text);
        int? cursor = null;
        if (cursorOffset is { } offset && offset >= 0 && offset < text.Length)
        {
            cursor = Array.BinarySearch(inputOffsets, offset);
            if (cursor < 0)
                cursor = ~cursor - 1; // the low half of a surrogate pair: its code point
        }

        var mode = DotsIO | UcBrl;
        if ((expandWordAtCursor || _expandWordAtCursor) && cursor is not null)
            mode |= CompbrlAtCursor;

        var outputCapacity = input.Length * 2 + 16;
        for (var attempt = 0; attempt < MaxAttempts; attempt++, outputCapacity *= 2)
        {
            var result = TranslateOnce(input, outputCapacity, cursor, mode);
            if (result is null)
                break;
            var (output, outputLength, outputPos, inputPos, cursorPos, consumed) = result.Value;
            // A full buffer ends translation early: liblouis reports the input it used
            if (consumed < input.Length || outputLength >= outputCapacity)
                continue;

            var cells = new byte[outputLength];
            var charForCell = new int[outputLength];
            for (var i = 0; i < outputLength; i++)
            {
                cells[i] = (byte)((output[i] - UnicodeBrailleBase) & 0xFF);
                charForCell[i] = inputOffsets[Math.Clamp(inputPos[i], 0, input.Length - 1)];
            }

            var cellForChar = new int[text.Length];
            for (var i = 0; i < input.Length; i++)
            {
                var start = inputOffsets[i];
                var end = i + 1 < input.Length ? inputOffsets[i + 1] : text.Length;
                var cell = Math.Clamp(outputPos[i], 0, Math.Max(outputLength - 1, 0));
                for (var j = start; j < end; j++)
                    cellForChar[j] = cell;
            }

            int? cursorCell = null;
            if (cursorOffset is { } requested)
            {
                cursorCell = cursor is null
                    ? (requested <= 0 ? 0 : outputLength)
                    : Math.Clamp(cursorPos, 0, outputLength);
            }
            return new BrailleTranslation(cells, cellForChar, charForCell, cursorCell);
        }

        _logger?.LogWarning("liblouis could not translate a {Length}-character text with {Table}; using computer braille", text.Length, Table);
        return new ComputerBrailleTranslator().Translate(text, cursorOffset);
    }

    public string BackTranslate(ReadOnlyMemory<byte> cells)
    {
        if (cells.Length == 0)
            return string.Empty;

        var span = cells.Span;
        var input = new int[span.Length];
        for (var i = 0; i < span.Length; i++)
            input[i] = LouDots | span[i];

        var outputCapacity = input.Length * 4 + 16;
        for (var attempt = 0; attempt < MaxAttempts; attempt++, outputCapacity *= 2)
        {
            var result = BackTranslateOnce(input, outputCapacity);
            if (result is null)
                break;
            var (text, consumed, outputLength) = result.Value;
            if (consumed < input.Length || outputLength >= outputCapacity)
                continue;
            return text;
        }

        _logger?.LogWarning("liblouis could not back-translate {Count} cells with {Table}", cells.Length, Table);
        return new ComputerBrailleTranslator().BackTranslate(cells);
    }

    private (int[] Output, int OutputLength, int[] OutputPos, int[] InputPos, int CursorPos, int Consumed)? TranslateOnce(
        int[] input, int outputCapacity, int? cursor, int mode)
    {
        var charSize = _api.CharSize;
        var inBuffer = Marshal.AllocHGlobal(input.Length * charSize);
        var outBuffer = Marshal.AllocHGlobal(outputCapacity * charSize);
        try
        {
            WriteWide(inBuffer, input, charSize);
            var inLength = input.Length;
            var outLength = outputCapacity;
            var outputPos = new int[input.Length];
            var inputPos = new int[outputCapacity];
            var cursorPos = cursor ?? -1;
            int ok;
            lock (CallLock)
            {
                unsafe
                {
                    fixed (int* outputPosPtr = outputPos)
                    fixed (int* inputPosPtr = inputPos)
                    {
                        ok = _api.Translate(_tableList, inBuffer, ref inLength, outBuffer, ref outLength,
                            IntPtr.Zero, IntPtr.Zero, (IntPtr)outputPosPtr, (IntPtr)inputPosPtr,
                            cursor is null ? IntPtr.Zero : (IntPtr)(&cursorPos), mode);
                    }
                }
            }
            if (ok == 0)
                return null;
            outLength = Math.Clamp(outLength, 0, outputCapacity);
            return (ReadWide(outBuffer, outLength, charSize), outLength, outputPos, inputPos, cursorPos, inLength);
        }
        finally
        {
            Marshal.FreeHGlobal(inBuffer);
            Marshal.FreeHGlobal(outBuffer);
        }
    }

    private (string Text, int Consumed, int OutputLength)? BackTranslateOnce(int[] input, int outputCapacity)
    {
        var charSize = _api.CharSize;
        var inBuffer = Marshal.AllocHGlobal(input.Length * charSize);
        var outBuffer = Marshal.AllocHGlobal(outputCapacity * charSize);
        try
        {
            WriteWide(inBuffer, input, charSize);
            var inLength = input.Length;
            var outLength = outputCapacity;
            int ok;
            lock (CallLock)
            {
                ok = _api.BackTranslateString(_tableList, inBuffer, ref inLength, outBuffer, ref outLength,
                    IntPtr.Zero, IntPtr.Zero, DotsIO);
            }
            if (ok == 0)
                return null;
            outLength = Math.Clamp(outLength, 0, outputCapacity);
            return (Decode(ReadWide(outBuffer, outLength, charSize), charSize), inLength, outLength);
        }
        finally
        {
            Marshal.FreeHGlobal(inBuffer);
            Marshal.FreeHGlobal(outBuffer);
        }
    }

    /// <summary>
    /// The text as liblouis widechars and the string offset of each: UTF-16 units when widechar is
    /// 16-bit, code points (a surrogate pair as one) when it is 32-bit.
    /// </summary>
    private (int[] Units, int[] Offsets) Encode(string text)
    {
        if (_api.CharSize == 2)
        {
            var units = new int[text.Length];
            var offsets = new int[text.Length];
            for (var i = 0; i < text.Length; i++)
            {
                units[i] = text[i];
                offsets[i] = i;
            }
            return (units, offsets);
        }

        var points = new List<int>(text.Length);
        var starts = new List<int>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            starts.Add(i);
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                points.Add(char.ConvertToUtf32(text[i], text[i + 1]));
                i++;
            }
            else
            {
                points.Add(text[i]);
            }
        }
        return (points.ToArray(), starts.ToArray());
    }

    private static string Decode(int[] units, int charSize)
    {
        var text = new StringBuilder(units.Length);
        foreach (var unit in units)
        {
            if (charSize == 2 || unit < 0x10000)
                text.Append((char)unit);
            else if (unit <= 0x10FFFF)
                text.Append(char.ConvertFromUtf32(unit));
            else
                text.Append('?');
        }
        return text.ToString();
    }

    private static void WriteWide(IntPtr buffer, int[] units, int charSize)
    {
        for (var i = 0; i < units.Length; i++)
        {
            if (charSize == 2)
                Marshal.WriteInt16(buffer, i * 2, unchecked((short)units[i]));
            else
                Marshal.WriteInt32(buffer, i * 4, units[i]);
        }
    }

    private static int[] ReadWide(IntPtr buffer, int length, int charSize)
    {
        var units = new int[length];
        for (var i = 0; i < length; i++)
        {
            units[i] = charSize == 2
                ? (ushort)Marshal.ReadInt16(buffer, i * 2)
                : Marshal.ReadInt32(buffer, i * 4);
        }
        return units;
    }

    /// <summary>
    /// The table list with each table found in the component's tables folder given as its full path
    /// (liblouis then finds the tables it includes beside it); the others are left for liblouis's own search.
    /// </summary>
    private static string ResolveTables(string directory, string table)
    {
        var folder = TableFolders.Select(f => Path.Combine(directory, f)).FirstOrDefault(Directory.Exists);
        if (folder is null)
            return table;
        return string.Join(",", table.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(name =>
            {
                var path = Path.Combine(folder, name);
                return !Path.IsPathRooted(name) && File.Exists(path) ? path : name;
            }));
    }

    /// <summary>The library in <paramref name="directory"/>, loaded once per folder. Called under <see cref="CallLock"/>.</summary>
    private static Api? LoadApi(string directory, ILogger? logger)
    {
        var fullDirectory = Path.GetFullPath(directory);
        if (LoadedLibraries.TryGetValue(fullDirectory, out var loaded))
            return loaded;

        if (!Directory.Exists(fullDirectory))
        {
            logger?.LogInformation("liblouis is not installed ({Directory} does not exist)", fullDirectory);
            return null;
        }

        IntPtr library = IntPtr.Zero;
        foreach (var name in LibraryNames)
        {
            var path = Path.Combine(fullDirectory, name);
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out library))
                break;
        }
        if (library == IntPtr.Zero)
        {
            logger?.LogInformation("liblouis is not installed (no loadable library in {Directory})", fullDirectory);
            return null;
        }

        var api = new Api(library);
        var tables = TableFolders.Select(f => Path.Combine(fullDirectory, f)).FirstOrDefault(Directory.Exists);
        if (tables is not null && Path.GetFileName(Path.GetDirectoryName(tables)) is "liblouis" && api.SetDataPath is { } setDataPath)
        {
            // share/liblouis/tables: liblouis looks for <data path>/liblouis/tables
            var dataPath = Marshal.StringToCoTaskMemUTF8(Path.GetDirectoryName(Path.GetDirectoryName(tables)));
            try
            {
                setDataPath(dataPath);
            }
            finally
            {
                Marshal.FreeCoTaskMem(dataPath);
            }
        }
        if (api.CharSize is not (2 or 4))
        {
            logger?.LogWarning("liblouis in {Directory} has an unsupported character size {Size}", fullDirectory, api.CharSize);
            return null;
        }
        LoadedLibraries[fullDirectory] = api;
        return api;
    }

    // liblouis uses __stdcall on 32-bit Windows; Winapi is the platform default everywhere else
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CharSizeFn();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GetTableFn(IntPtr tableList);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr SetDataPathFn(IntPtr path);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int TranslateFn(IntPtr tableList, IntPtr inbuf, ref int inlen, IntPtr outbuf, ref int outlen,
        IntPtr typeform, IntPtr spacing, IntPtr outputPos, IntPtr inputPos, IntPtr cursorPos, int mode);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int BackTranslateStringFn(IntPtr tableList, IntPtr inbuf, ref int inlen, IntPtr outbuf, ref int outlen,
        IntPtr typeform, IntPtr spacing, int mode);

    /// <summary>The liblouis functions Vox calls.</summary>
    private sealed class Api
    {
        public Api(IntPtr library)
        {
            T Export<T>(string name) where T : Delegate =>
                Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

            CharSize = Export<CharSizeFn>("lou_charSize")();
            GetTable = Export<GetTableFn>("lou_getTable");
            Translate = Export<TranslateFn>("lou_translate");
            BackTranslateString = Export<BackTranslateStringFn>("lou_backTranslateString");
            SetDataPath = NativeLibrary.TryGetExport(library, "lou_setDataPath", out var setDataPath)
                ? Marshal.GetDelegateForFunctionPointer<SetDataPathFn>(setDataPath)
                : null;
        }

        public int CharSize { get; }
        public GetTableFn GetTable { get; }
        public TranslateFn Translate { get; }
        public BackTranslateStringFn BackTranslateString { get; }
        public SetDataPathFn? SetDataPath { get; }
    }
}
