using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Localization;

namespace Vox.Core.Localization;

/// <summary>
/// Vox's spoken and UI strings (<c>Localization/Strings.resx</c> and its translations
/// <c>Strings.&lt;language&gt;.resx</c>), for static code as well as constructed classes. Strings
/// are looked up when they are used, in <see cref="Culture"/>, so a change of the language
/// setting applies at once. A string a translation lacks falls back to English.
/// </summary>
public static class VoxStrings
{
    /// <summary>The resources: <c>Vox.Core.Localization.Strings</c> and its satellite assemblies.</summary>
    public static ResourceManager Resources { get; } = new("Vox.Core.Localization.Strings", typeof(VoxStrings).Assembly);

    private static CultureInfo? _culture;
    private static readonly AsyncLocal<IStringLocalizer?> Scoped = new();

    /// <summary>
    /// The language Vox speaks and shows (the <c>Language</c> setting); null follows the Windows
    /// display language (<see cref="CultureInfo.CurrentUICulture"/>).
    /// </summary>
    public static CultureInfo? Culture
    {
        get => Volatile.Read(ref _culture);
        set => Volatile.Write(ref _culture, value);
    }

    /// <summary>The language strings are looked up in now.</summary>
    public static CultureInfo EffectiveCulture => Culture ?? CultureInfo.CurrentUICulture;

    /// <summary>
    /// True when the language in use is written right to left (Arabic, Hebrew...): dialogs are
    /// laid out right to left.
    /// </summary>
    public static bool IsRightToLeft => EffectiveCulture.TextInfo.IsRightToLeft;

    /// <summary>The resources in <see cref="EffectiveCulture"/>.</summary>
    public static IStringLocalizer ResourceLocalizer { get; } = new ResourceStringLocalizer();

    /// <summary>
    /// The localizer every Vox string goes through: <see cref="ResourceLocalizer"/>, unless
    /// <see cref="Use"/> put another in place for the current async flow (tests: a pseudo locale).
    /// Resolved on each lookup, so it can be kept.
    /// </summary>
    public static IStringLocalizer Localizer { get; } = new ForwardingLocalizer();

    /// <summary>
    /// Puts <paramref name="localizer"/> in place for the current async flow (and what it
    /// starts) until the result is disposed; other threads keep theirs. For tests.
    /// </summary>
    public static IDisposable Use(IStringLocalizer localizer)
    {
        var previous = Scoped.Value;
        Scoped.Value = localizer;
        return new Restore(() => Scoped.Value = previous);
    }

    /// <summary>The string <paramref name="name"/>; the name itself when there is no such string.</summary>
    public static string Get(string name) => Localizer[name].Value;

    /// <summary>The string <paramref name="name"/> with its placeholders ({0}, {1}...) filled in.</summary>
    public static string Format(string name, params object?[] arguments) => Localizer[name, arguments!].Value;

    /// <summary>The string <paramref name="name"/>, or <paramref name="fallback"/> when there is none.</summary>
    public static string GetOrDefault(string name, string fallback)
    {
        var value = Localizer[name];
        return value.ResourceNotFound ? fallback : value.Value;
    }

    /// <summary>True when there is a string <paramref name="name"/>.</summary>
    public static bool Has(string name) => !Localizer[name].ResourceNotFound;

    private static IStringLocalizer Current => Scoped.Value ?? ResourceLocalizer;

    private sealed class ForwardingLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => Current[name];
        public LocalizedString this[string name, params object[] arguments] => Current[name, arguments];
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Current.GetAllStrings(includeParentCultures);
    }

    private sealed class ResourceStringLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name]
        {
            get
            {
                var value = Resources.GetString(name, EffectiveCulture);
                return new LocalizedString(name, value ?? name, resourceNotFound: value is null);
            }
        }

        public LocalizedString this[string name, params object[] arguments]
        {
            get
            {
                var culture = EffectiveCulture;
                var format = Resources.GetString(name, culture);
                return new LocalizedString(name, string.Format(culture, format ?? name, arguments), resourceNotFound: format is null);
            }
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var culture = EffectiveCulture; ; culture = culture.Parent)
            {
                var set = Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
                if (set is not null)
                {
                    foreach (System.Collections.DictionaryEntry entry in set)
                    {
                        if (entry.Key is string key && entry.Value is string value && seen.Add(key))
                            yield return new LocalizedString(key, value);
                    }
                }
                if (!includeParentCultures || culture.Equals(CultureInfo.InvariantCulture))
                    break;
            }
        }
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        private Action? _restore = restore;
        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
    }
}

/// <summary>
/// Hands out <see cref="VoxStrings.Localizer"/> for every type, so a DI-injected
/// <see cref="IStringLocalizer{T}"/> reads the same resources as Vox's static code.
/// </summary>
public sealed class VoxStringLocalizerFactory : IStringLocalizerFactory
{
    public IStringLocalizer Create(Type resourceSource) => VoxStrings.Localizer;
    public IStringLocalizer Create(string baseName, string location) => VoxStrings.Localizer;
}
