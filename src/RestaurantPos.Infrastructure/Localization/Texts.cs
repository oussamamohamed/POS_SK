using System.Collections;
using System.Globalization;
using System.Resources;

namespace RestaurantPos.Infrastructure.Localization;

/// <summary>Textes serveur par clé sémantique (`zone.nom`). Repli : anglais, puis la clé elle-même.</summary>
public static class Texts
{
    public static readonly string[] SupportedLanguages = ["en", "fr", "ar"];

    private static readonly ResourceManager Resources =
        new("RestaurantPos.Infrastructure.Localization.SharedResource", typeof(SharedResource).Assembly);

    public static string T(string key, params (string Name, object? Value)[] args) =>
        Get(CultureInfo.CurrentUICulture, key, args);

    public static string Get(CultureInfo culture, string key, params (string Name, object? Value)[] args)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var text = Resources.GetString(key, culture) ?? key;
        foreach (var (name, value) in args)
        {
            text = text.Replace("{" + name + "}", Convert.ToString(value, CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
        return text;
    }

    public static IReadOnlySet<string> Keys(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        // L'anglais est le .resx neutre : pas d'assembly satellite « en ».
        var target = culture.TwoLetterISOLanguageName == "en" ? CultureInfo.InvariantCulture : culture;
        var set = Resources.GetResourceSet(target, createIfNotExists: true, tryParents: false);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (set is null) return keys;
        foreach (DictionaryEntry entry in set) keys.Add((string)entry.Key);
        return keys;
    }
}
