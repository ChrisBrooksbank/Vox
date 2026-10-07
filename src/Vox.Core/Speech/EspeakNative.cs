using System.Runtime.InteropServices;
using System.Text;

namespace Vox.Core.Speech;

/// <summary>
/// <see cref="IEspeakApi"/> over libespeak-ng.dll, loaded from the component folder (eSpeak NG is
/// GPL and installed separately; Vox only calls it). eSpeak keeps global state, so the library is
/// initialized once per process: use <see cref="Load"/>.
/// </summary>
public sealed class EspeakNative : IEspeakApi
{
    private const int AUDIO_OUTPUT_SYNCHRONOUS = 2;
    private const int POS_CHARACTER = 1;
    private const uint espeakCHARS_UTF8 = 1;
    private const int espeakRATE = 1;
    private const int espeakVOLUME = 2;
    private const int espeakPITCH = 3;
    private const int EE_OK = 0;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SynthCallback(IntPtr wav, int numSamples, IntPtr events);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitializeFn(int output, int bufferLength, IntPtr path, int options);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetSynthCallbackFn(SynthCallback callback);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SynthFn(IntPtr text, UIntPtr size, uint position, int positionType, uint endPosition, uint flags, IntPtr uniqueIdentifier, IntPtr userData);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetParameterFn(int parameter, int value, int relative);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetVoiceByNameFn(IntPtr name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ListVoicesFn(IntPtr voiceSpec);

    private static readonly object LoadLock = new();
    private static EspeakNative? _instance;

    private readonly SynthFn _synth;
    private readonly SetParameterFn _setParameter;
    private readonly SetVoiceByNameFn _setVoiceByName;
    private readonly ListVoicesFn _listVoices;
    // Kept alive for as long as eSpeak may call it
    private readonly SynthCallback _callback;
    private Func<short[], bool>? _onSamples;

    public int SampleRate { get; }

    private EspeakNative(IntPtr library, string directory)
    {
        T Export<T>(string name) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

        var initialize = Export<InitializeFn>("espeak_Initialize");
        var setSynthCallback = Export<SetSynthCallbackFn>("espeak_SetSynthCallback");
        _synth = Export<SynthFn>("espeak_Synth");
        _setParameter = Export<SetParameterFn>("espeak_SetParameter");
        _setVoiceByName = Export<SetVoiceByNameFn>("espeak_SetVoiceByName");
        _listVoices = Export<ListVoicesFn>("espeak_ListVoices");

        var path = Utf8(directory);
        try
        {
            SampleRate = initialize(AUDIO_OUTPUT_SYNCHRONOUS, 0, path, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(path);
        }
        if (SampleRate <= 0)
            throw new InvalidOperationException("eSpeak NG could not be initialized");
        _callback = OnSynth;
        setSynthCallback(_callback);
    }

    /// <summary>Loads and initializes eSpeak NG from <paramref name="directory"/> (once per process).</summary>
    public static EspeakNative Load(string directory)
    {
        lock (LoadLock)
        {
            if (_instance is not null)
                return _instance;
            var library = NativeLibrary.Load(Path.Combine(directory, EspeakComponent.LibraryFileName));
            _instance = new EspeakNative(library, directory);
            return _instance;
        }
    }

    public IReadOnlyList<SynthesizerVoice> ListVoices()
    {
        var voices = new List<SynthesizerVoice>();
        var list = _listVoices(IntPtr.Zero);
        if (list == IntPtr.Zero)
            return voices;
        for (int i = 0; ; i++)
        {
            var voice = Marshal.ReadIntPtr(list, i * IntPtr.Size);
            if (voice == IntPtr.Zero)
                break;
            // espeak_VOICE: const char *name; const char *languages; const char *identifier; ...
            var name = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(voice, 0));
            var languages = Marshal.ReadIntPtr(voice, IntPtr.Size);
            if (!string.IsNullOrEmpty(name))
                voices.Add(new SynthesizerVoice(name, FirstLanguage(languages)));
        }
        return voices;
    }

    /// <summary>The first language of an espeak_VOICE languages list (a priority byte, then the name).</summary>
    private static string FirstLanguage(IntPtr languages) =>
        languages == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(languages + 1) ?? string.Empty;

    public void SetVoice(string name)
    {
        var utf8 = Utf8(name);
        try
        {
            if (_setVoiceByName(utf8) != EE_OK)
                throw new InvalidOperationException("eSpeak NG has no such voice");
        }
        finally
        {
            Marshal.FreeHGlobal(utf8);
        }
    }

    public void SetRate(int wpm) => _setParameter(espeakRATE, wpm, 0);
    public void SetPitch(int pitch) => _setParameter(espeakPITCH, pitch, 0);
    public void SetVolume(int volume) => _setParameter(espeakVOLUME, volume, 0);

    public void Synthesize(string text, Func<short[], bool> onSamples)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\0");
        var utf8 = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, utf8, bytes.Length);
            _onSamples = onSamples;
            // Synchronous output: returns once all the audio has been handed to the callback
            _synth(utf8, (UIntPtr)bytes.Length, 0, POS_CHARACTER, 0, espeakCHARS_UTF8, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            _onSamples = null;
            Marshal.FreeHGlobal(utf8);
        }
    }

    private int OnSynth(IntPtr wav, int numSamples, IntPtr events)
    {
        if (wav == IntPtr.Zero || numSamples <= 0)
            return 0;
        var samples = new short[numSamples];
        Marshal.Copy(wav, samples, 0, numSamples);
        // 1 aborts synthesis
        return _onSamples?.Invoke(samples) == false ? 1 : 0;
    }

    private static IntPtr Utf8(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\0");
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return pointer;
    }
}
