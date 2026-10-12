using System.Globalization;
using System.Text;
using Microsoft.Extensions.Localization;
using Vox.Core.Localization;

namespace Vox.Core.Tests.TestSupport;

/// <summary>
/// A pseudo locale (like Windows' qps-ploc): every string from the resources comes back with
/// its ASCII letters swapped for accented look-alikes ("checked" → "ćĥéćķéď") and its
/// placeholders kept, so any English left in output did not come from the resources. A string
/// the resources lack comes back as its name, unchanged, so it shows up too.
/// </summary>
public sealed class PseudoLocalizer : IStringLocalizer
{
    private const string Plain = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Pseudo = "àƀćďéƒĝĥîĵķļɱñöƥʠŕšţûṽŵẋýžÀƁĆĎÉƑĜĤÎĴĶĻṀÑÖƤɊŔŠŢÛṼŴẊÝŽ";

    public LocalizedString this[string name]
    {
        get
        {
            var value = VoxStrings.Resources.GetString(name, CultureInfo.InvariantCulture);
            return value is null ? new(name, name, true) : new(name, Transform(value));
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var format = this[name];
            return new(name, string.Format(CultureInfo.InvariantCulture, format.Value, arguments), format.ResourceNotFound);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];

    /// <summary>The pseudo-localized text: letters outside {placeholders} swapped.</summary>
    public static string Transform(string text)
    {
        var result = new StringBuilder(text.Length);
        int depth = 0;
        foreach (char c in text)
        {
            if (c == '{') depth++;
            else if (c == '}' && depth > 0) depth--;
            int index = depth == 0 ? Plain.IndexOf(c) : -1;
            result.Append(index >= 0 ? Pseudo[index] : c);
        }
        return result.ToString();
    }
}
