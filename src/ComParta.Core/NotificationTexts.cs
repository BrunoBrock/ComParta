namespace ComParta.Core;

/// <summary>The words of system notifications (CAP-20), copied from the UI's catalogs.</summary>
public static partial class NotificationTexts
{
    /// <summary>The person's chosen language, else the system's, else English; as the UI picks it.</summary>
    public static string PickLanguage(string? saved, string? system) => Known(saved) ?? Known(system) ?? "en";

    /// <summary>The sentence for a key with {name}, {count} and {percent} filled in, in that language.</summary>
    public static string Format(string language, string key, string name, int count = 0, int percent = 0)
    {
        var catalog = Catalogs.GetValueOrDefault(language) ?? Catalogs["en"];
        return catalog[key]
            .Replace("{name}", name, StringComparison.Ordinal)
            .Replace("{count}", count.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{percent}", percent.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static string? Known(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
            return null;
        var exact = Catalogs.Keys.FirstOrDefault(k => string.Equals(k, tag, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;
        if (tag.Split('-')[0].Equals("pt", StringComparison.OrdinalIgnoreCase))
            return "pt-BR";
        return tag.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : null;
    }
}
