using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Vox.Core.MathSpeech;

/// <summary>
/// <see cref="IMathSpeech"/> over MathCAT's C library (libmathcat_c.dll), installed separately
/// in the component folder with its Rules folder (docs/mathcat.md). MathCAT keeps global state,
/// so there is one instance per process (<see cref="Load"/>).
/// </summary>
public sealed class MathCatSpeech : IMathSpeech
{
    // Every MathCAT call returns a string it allocated (empty on error, the message then from GetError)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr StringFn(IntPtr argument);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr NoArgumentFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PreferenceFn(IntPtr name, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeFn(IntPtr text);

    private static readonly object LoadLock = new();
    private static MathCatSpeech? _instance;

    private readonly StringFn _setMathMl;
    private readonly NoArgumentFn _getSpokenText;
    private readonly StringFn _doNavigateCommand;
    private readonly NoArgumentFn _getError;
    private readonly FreeFn _free;
    private readonly ILogger _logger;

    private MathCatSpeech(IntPtr library, string directory, ILogger logger)
    {
        T Export<T>(string name) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

        _logger = logger;
        var setRulesDir = Export<StringFn>("SetRulesDir");
        var setPreference = Export<PreferenceFn>("SetPreference");
        _setMathMl = Export<StringFn>("SetMathML");
        _getSpokenText = Export<NoArgumentFn>("GetSpokenText");
        _doNavigateCommand = Export<StringFn>("DoNavigateCommand");
        _getError = Export<NoArgumentFn>("GetError");
        _free = Export<FreeFn>("FreeMathCATString");

        Call(() => setRulesDir(Utf8(Path.Combine(directory, MathCatComponent.RulesFolderName))));
        // Speech for a screen reader that says the expression's structure ("fraction ... over ...")
        Call(() => setPreference(Utf8("TTS"), Utf8("None")));
        Call(() => setPreference(Utf8("Verbosity"), Utf8("Medium")));
        Call(() => setPreference(Utf8("NavMode"), Utf8("Enhanced")));
    }

    public bool IsAvailable => true;

    /// <summary>Loads MathCAT from <paramref name="directory"/> (once per process).</summary>
    public static MathCatSpeech Load(string directory, ILogger logger)
    {
        lock (LoadLock)
        {
            if (_instance is not null)
                return _instance;
            var library = NativeLibrary.Load(Path.Combine(directory, MathCatComponent.LibraryFileName));
            _instance = new MathCatSpeech(library, directory, logger);
            return _instance;
        }
    }

    public string? SetExpression(string mathMl)
    {
        if (Call(() => _setMathMl(Utf8(mathMl))) is null)
            return null;
        return NonEmpty(Call(() => _getSpokenText()));
    }

    public string? Navigate(string command) => NonEmpty(Call(() => _doNavigateCommand(Utf8(command))));

    private static string? NonEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Runs a call, frees its UTF-8 arguments and result, and returns the result (null on error).</summary>
    private string? Call(Func<IntPtr> call)
    {
        var result = IntPtr.Zero;
        try
        {
            result = call();
            var text = result == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(result);
            if (string.IsNullOrEmpty(text) && Error() is { Length: > 0 } error)
            {
                _logger.LogDebug("MathCAT: {Error}", error);
                return null;
            }
            return text ?? string.Empty;
        }
        finally
        {
            if (result != IntPtr.Zero)
                _free(result);
            FreeArguments();
        }
    }

    private string? Error()
    {
        var error = _getError();
        if (error == IntPtr.Zero)
            return null;
        try { return Marshal.PtrToStringUTF8(error); }
        finally { _free(error); }
    }

    // Arguments allocated for the call in progress (pipeline thread only)
    private readonly List<IntPtr> _arguments = [];

    private IntPtr Utf8(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text + "\0");
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        _arguments.Add(pointer);
        return pointer;
    }

    private void FreeArguments()
    {
        foreach (var pointer in _arguments)
            Marshal.FreeHGlobal(pointer);
        _arguments.Clear();
    }
}

/// <summary>Where the optional MathCAT component is installed, and whether it is.</summary>
public static class MathCatComponent
{
    public const string LibraryFileName = "libmathcat_c.dll";
    public const string RulesFolderName = "Rules";

    /// <summary>%LOCALAPPDATA%\Vox\components\mathcat: the library and its Rules folder.</summary>
    public static string DefaultDirectory => Path.Combine(Lifecycle.VoxPaths.Components, "mathcat");

    public static bool IsInstalled(string directory) =>
        File.Exists(Path.Combine(directory, LibraryFileName)) && Directory.Exists(Path.Combine(directory, RulesFolderName));
}
