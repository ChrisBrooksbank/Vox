using Microsoft.Extensions.Logging;

namespace Vox.Core.Braille;

/// <summary>
/// Makes the translator for the braille table setting: liblouis when it is installed and has the
/// table, else the built-in <see cref="ComputerBrailleTranslator"/>.
/// </summary>
public static class BrailleTranslatorFactory
{
    private static int _fallbackLogged;

    /// <summary>
    /// liblouis from <paramref name="liblouisDirectory"/> (default <see cref="LibLouisTranslator.DefaultDirectory"/>)
    /// with <paramref name="table"/>, or computer braille when it can't be loaded (logged once per process).
    /// </summary>
    public static IBrailleTranslator Create(string table, string? liblouisDirectory, ILogger? logger, bool expandWordAtCursor = false)
    {
        var directory = liblouisDirectory ?? LibLouisTranslator.DefaultDirectory;
        if (LibLouisTranslator.TryLoad(directory, table, logger, expandWordAtCursor) is { } liblouis)
            return liblouis;

        if (Interlocked.Exchange(ref _fallbackLogged, 1) == 0)
            logger?.LogInformation("Braille table {Table} is not available; using US computer braille", table);
        return new ComputerBrailleTranslator();
    }
}
