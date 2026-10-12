namespace Vox.Core.Ocr;

/// <summary>A recognised word and where it is on the screen.</summary>
public sealed record OcrWord(string Text, ScreenRect Bounds);

/// <summary>A recognised line of text, its words in reading order.</summary>
public sealed record OcrLine(IReadOnlyList<OcrWord> Words);

/// <summary>What text recognition found, line by line, in reading order.</summary>
public sealed record OcrResult(IReadOnlyList<OcrLine> Lines)
{
    public static readonly OcrResult Empty = new([]);

    /// <summary>No text was found.</summary>
    public bool IsEmpty => Lines.All(l => l.Words.Count == 0);
}
