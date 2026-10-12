namespace ComParta.Core;

/// <summary>The words of system notifications (CAP-20), copied from the UI's catalogs.</summary>
public static partial class NotificationTexts
{
    /// <summary>The person's chosen language, else the system's, else English; as the UI picks it.</summary>
    public static string PickLanguage(string? saved, string? system) => Known(saved) ?? Known(system) ?? "en";

    /// <summary>The sentence for a key with {name} filled in, in that language.</summary>
    public static string Format(string language, string key, string name)
    {
        var catalog = Catalogs.GetValueOrDefault(language) ?? Catalogs["en"];
        return catalog[key].Replace("{name}", name, StringComparison.Ordinal);
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
