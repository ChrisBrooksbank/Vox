namespace Vox.Core.Magnifier;

/// <summary>The zoom steps the magnifier moves through (1× to 16×), finer at low zoom where a step shows most.</summary>
public static class MagnifierZoom
{
    /// <summary>Every step, as percentages (100 = 1×).</summary>
    public static readonly IReadOnlyList<int> StepsPercent =
        [100, 125, 150, 175, 200, 250, 300, 400, 500, 600, 800, 1000, 1200, 1400, 1600];

    public const int MinPercent = 100;
    public const int MaxPercent = 1600;

    /// <summary>A zoom percentage kept between 1× and 16×.</summary>
    public static int Clamp(int percent) => Math.Clamp(percent, MinPercent, MaxPercent);

    /// <summary>The next step up from <paramref name="percent"/>, or null at 16×.</summary>
    public static int? In(int percent)
    {
        foreach (var step in StepsPercent)
        {
            if (step > percent)
                return step;
        }
        return null;
    }

    /// <summary>The next step down from <paramref name="percent"/>, or null at 1×.</summary>
    public static int? Out(int percent)
    {
        for (int i = StepsPercent.Count - 1; i >= 0; i--)
        {
            if (StepsPercent[i] < percent)
                return StepsPercent[i];
        }
        return null;
    }

    /// <summary>A zoom percentage as a factor (250 = 2.5).</summary>
    public static double Factor(int percent) => Clamp(percent) / 100.0;

    /// <summary>How a zoom is said: "2.5 times", with the numbers of Vox's language.</summary>
    public static string Describe(int percent) =>
        Localization.VoxStrings.Format("Magnifier.Zoom", Factor(percent).ToString("0.##", Localization.VoxStrings.Culture));
}
