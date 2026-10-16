namespace Vox.Core.Magnifier;

/// <summary>How the magnifier changes the screen's colours.</summary>
public enum MagnifierColorFilter
{
    /// <summary>The screen's own colours.</summary>
    None,
    /// <summary>Every colour inverted (light text on a dark background).</summary>
    Inverted,
    /// <summary>Shades of grey.</summary>
    Greyscale,
    /// <summary>Shades of grey, inverted.</summary>
    InvertedGreyscale,
    /// <summary>High contrast: what is dark becomes yellow, what is light becomes black.</summary>
    YellowOnBlack,
}

/// <summary>
/// The colour matrices the Windows Magnification API applies (<c>MAGCOLOREFFECT</c>: 5×5, row
/// major, a colour as the row vector [R G B A 1] multiplied by the matrix).
/// </summary>
public static class MagnifierColorMatrices
{
    // Rec. 709 luminance: how bright each channel looks
    private const float R = 0.2126f, G = 0.7152f, B = 0.0722f;

    /// <summary>The matrix that leaves colours as they are.</summary>
    public static float[] Identity =>
    [
        1, 0, 0, 0, 0,
        0, 1, 0, 0, 0,
        0, 0, 1, 0, 0,
        0, 0, 0, 1, 0,
        0, 0, 0, 0, 1,
    ];

    /// <summary>The matrix that turns everything black (the screen curtain's).</summary>
    public static float[] Black =>
    [
        0, 0, 0, 0, 0,
        0, 0, 0, 0, 0,
        0, 0, 0, 0, 0,
        0, 0, 0, 1, 0,
        0, 0, 0, 0, 1,
    ];

    /// <summary>The matrix for <paramref name="filter"/>.</summary>
    public static float[] For(MagnifierColorFilter filter) => filter switch
    {
        MagnifierColorFilter.Inverted =>
        [
            -1, 0, 0, 0, 0,
            0, -1, 0, 0, 0,
            0, 0, -1, 0, 0,
            0, 0, 0, 1, 0,
            1, 1, 1, 0, 1,
        ],
        MagnifierColorFilter.Greyscale =>
        [
            R, R, R, 0, 0,
            G, G, G, 0, 0,
            B, B, B, 0, 0,
            0, 0, 0, 1, 0,
            0, 0, 0, 0, 1,
        ],
        MagnifierColorFilter.InvertedGreyscale =>
        [
            -R, -R, -R, 0, 0,
            -G, -G, -G, 0, 0,
            -B, -B, -B, 0, 0,
            0, 0, 0, 1, 0,
            1, 1, 1, 0, 1,
        ],
        // Inverted brightness in red and green, no blue: dark text on a light page becomes yellow on black
        MagnifierColorFilter.YellowOnBlack =>
        [
            -R, -R, 0, 0, 0,
            -G, -G, 0, 0, 0,
            -B, -B, 0, 0, 0,
            0, 0, 0, 1, 0,
            1, 1, 0, 0, 1,
        ],
        _ => Identity,
    };

    /// <summary>
    /// <paramref name="color"/> (red, green, blue from 0 to 1) through <paramref name="matrix"/>,
    /// clamped to 0–1 as the screen shows it.
    /// </summary>
    public static (float R, float G, float B) Apply(float[] matrix, (float R, float G, float B) color)
    {
        float[] row = [color.R, color.G, color.B, 1, 1];
        float Channel(int column)
        {
            float sum = 0;
            for (int i = 0; i < 5; i++)
                sum += row[i] * matrix[i * 5 + column];
            return Math.Clamp(sum, 0, 1);
        }
        return (Channel(0), Channel(1), Channel(2));
    }
}
