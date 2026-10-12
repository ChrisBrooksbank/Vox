namespace Vox.Core.Ocr;

/// <summary>
/// Recognises text in a picture of the screen, on this computer (nothing is sent anywhere).
/// Safe to call from any thread.
/// </summary>
public interface IOcrEngine
{
    /// <summary>Text recognition can be used (Windows has a recognition language installed).</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Recognises the text in <paramref name="image"/>, with word positions on the screen.
    /// <paramref name="language"/> is a BCP 47 tag ("en-US", "fr"); null: the user's languages.
    /// </summary>
    Task<OcrResult> RecognizeAsync(CapturedImage image, string? language, CancellationToken cancellationToken = default);
}

/// <summary>An <see cref="IOcrEngine"/> for where there is none (not Windows, no language).</summary>
public sealed class NoOcrEngine : IOcrEngine
{
    public bool IsAvailable => false;

    public Task<OcrResult> RecognizeAsync(CapturedImage image, string? language, CancellationToken cancellationToken = default) =>
        Task.FromResult(OcrResult.Empty);
}

/// <summary>How much to enlarge a capture before recognising it.</summary>
public static class OcrScaling
{
    // Larger captures are already big enough to read, and scaling them up would be slow
    private const int UpscaleMaxPixels = 2_000_000;

    /// <summary>
    /// The scale for an image: 2 for small captures (screen text is small for recognition),
    /// less than 1 when the image is larger than the engine's <paramref name="maxDimension"/>,
    /// otherwise 1.
    /// </summary>
    public static double FactorFor(int width, int height, int maxDimension)
    {
        int longest = Math.Max(width, height);
        if (longest > maxDimension)
            return maxDimension / (double)longest;
        if (longest * 2 <= maxDimension && (long)width * height <= UpscaleMaxPixels)
            return 2;
        return 1;
    }
}
