using System.Globalization;
using System.Runtime.CompilerServices;
using Vox.Core.Localization;

namespace Vox.Core.Tests.TestSupport;

/// <summary>
/// Tests assert English output: Vox's strings are English whatever the Windows display language
/// of the machine running them. Tests of other languages put theirs in place with
/// <see cref="VoxStrings.Use"/> (one async flow only) or <see cref="InCulture"/>.
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void UseEnglish() => VoxStrings.Culture = CultureInfo.InvariantCulture;

    /// <summary>A localizer reading the resources in <paramref name="culture"/>, for <see cref="VoxStrings.Use"/>.</summary>
    public static IDisposable InCulture(string culture) => VoxStrings.Use(new CultureLocalizer(CultureInfo.GetCultureInfo(culture)));

    private sealed class CultureLocalizer(CultureInfo culture) : Microsoft.Extensions.Localization.IStringLocalizer
    {
        public Microsoft.Extensions.Localization.LocalizedString this[string name]
        {
            get
            {
                var value = VoxStrings.Resources.GetString(name, culture);
                return new(name, value ?? name, value is null);
            }
        }

        public Microsoft.Extensions.Localization.LocalizedString this[string name, params object[] arguments]
        {
            get
            {
                var value = VoxStrings.Resources.GetString(name, culture);
                return new(name, string.Format(culture, value ?? name, arguments), value is null);
            }
        }

        public IEnumerable<Microsoft.Extensions.Localization.LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
