using System.Text;
using System.Text.RegularExpressions;

namespace ComParta.Contracts;

/// <summary>
/// Copies the notification sentences from the UI's catalogs into core (CAP-20), so a notification
/// can be worded while no window is open. The catalogs stay the one place words are written.
/// </summary>
public static partial class NotificationTextsWriter
{
    public const string GeneratedFile = "src/ComParta.Core/NotificationTexts.g.cs";
    private const string Prefix = "notify.";
    private static readonly (string Language, string File)[] Catalogs =
        [("en", "src/ui/src/i18n/en.ts"), ("pt-BR", "src/ui/src/i18n/pt-BR.ts")];

    public static string Write(string repositoryRoot)
    {
        var cs = new StringBuilder();
        cs.Append("// Generated from the UI catalogs by tools/ComParta.Contracts.TypeGen. Do not edit.\n");
        cs.Append("namespace ComParta.Core;\n\n");
        cs.Append("public static partial class NotificationTexts\n{\n");
        cs.Append("    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs = new()\n    {\n");
        foreach (var (language, file) in Catalogs)
        {
            cs.Append($"        [\"{language}\"] = new()\n        {{\n");
            foreach (var (key, text) in Read(Path.Combine(repositoryRoot, file)))
                cs.Append($"            [\"{key}\"] = \"{Escape(text)}\",\n");
            cs.Append("        },\n");
        }
        cs.Append("    };\n}\n");
        return cs.ToString();
    }

    private static IEnumerable<(string Key, string Text)> Read(string catalog)
    {
        foreach (var line in File.ReadLines(catalog))
        {
            if (!line.TrimStart().StartsWith("'" + Prefix, StringComparison.Ordinal))
                continue;
            var match = Entry().Match(line);
            if (!match.Success)
                throw new InvalidOperationException($"Keep each notification sentence on one line in single quotes: {line.Trim()}");
            yield return (match.Groups["key"].Value, match.Groups["text"].Value);
        }
    }

    private static string Escape(string text) => text.Replace("\\'", "'").Replace("\\", "\\\\").Replace("\"", "\\\"");

    [GeneratedRegex(@"^\s*'(?<key>notify\.[\w.]+)':\s*'(?<text>(?:[^'\\]|\\.)*)',\s*$")]
    private static partial Regex Entry();
}
