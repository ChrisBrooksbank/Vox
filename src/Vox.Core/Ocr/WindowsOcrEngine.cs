using Microsoft.Extensions.Logging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using WinOcrEngine = Windows.Media.Ocr.OcrEngine;

namespace Vox.Core.Ocr;

/// <summary>
/// <see cref="IOcrEngine"/> over Windows.Media.Ocr: recognition on this computer, in the
/// languages whose Windows OCR component is installed (Settings, Time and language, Language:
/// a language's optional "optical character recognition" feature).
/// </summary>
public sealed class WindowsOcrEngine : IOcrEngine
{
    private readonly ILogger<WindowsOcrEngine> _logger;
    private readonly Lazy<bool> _available;

    public WindowsOcrEngine(ILogger<WindowsOcrEngine> logger)
    {
        _logger = logger;
        _available = new Lazy<bool>(() =>
        {
            try
            {
                return WinOcrEngine.AvailableRecognizerLanguages.Count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Windows text recognition is not available");
                return false;
            }
        });
    }

    public bool IsAvailable => _available.Value;

    /// <summary>The languages Windows can recognise (BCP 47 tags).</summary>
    public static IReadOnlyList<string> Languages()
    {
        try
        {
            return WinOcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<OcrResult> RecognizeAsync(CapturedImage image, string? language, CancellationToken cancellationToken = default)
    {
        var engine = CreateEngine(language) ?? throw new InvalidOperationException("No text recognition language is installed");

        int maxDimension = (int)WinOcrEngine.MaxImageDimension;
        var scaled = image.Scaled(OcrScaling.FactorFor(image.Width, image.Height, maxDimension));

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            CryptographicBuffer.CreateFromByteArray(scaled.Pixels), BitmapPixelFormat.Bgra8, scaled.Width, scaled.Height,
            BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);

        var lines = result.Lines
            .Select(line => new OcrLine(line.Words
                .Select(w => new OcrWord(w.Text, scaled.ToScreen(w.BoundingRect.X, w.BoundingRect.Y, w.BoundingRect.Width, w.BoundingRect.Height)))
                .ToList()))
            .Where(line => line.Words.Count > 0)
            .ToList();
        _logger.LogDebug("Recognised {Lines} lines in a {Width}x{Height} image", lines.Count, image.Width, image.Height);
        return new OcrResult(lines);
    }

    /// <summary>
    /// The engine for <paramref name="language"/> (or one for the same language in another
    /// region), else for the user's profile languages.
    /// </summary>
    private static WinOcrEngine? CreateEngine(string? language)
    {
        if (!string.IsNullOrWhiteSpace(language))
        {
            var tag = Languages().FirstOrDefault(l => l.Equals(language, StringComparison.OrdinalIgnoreCase))
                ?? Languages().FirstOrDefault(l => SameLanguage(l, language));
            if (tag is not null && WinOcrEngine.TryCreateFromLanguage(new Language(tag)) is { } engine)
                return engine;
        }
        return WinOcrEngine.TryCreateFromUserProfileLanguages();
    }

    private static bool SameLanguage(string a, string b) =>
        string.Equals(a.Split('-')[0], b.Split('-')[0], StringComparison.OrdinalIgnoreCase);
}
