using System.Text.RegularExpressions;

namespace ComParta.Notices;

/// <summary>Finds the copyright line in a package's license file.</summary>
public static partial class CopyrightLine
{
    // Case matters: a license's own words, such as Apache's "copyright notice that is included",
    // start with a small letter, and a package's own line does not.
    [GeneratedRegex(@"^(Copyright\b|COPYRIGHT\b|\([cC]\)|©)")]
    private static partial Regex Start();

    public static string? FromText(string text) =>
        text.ReplaceLineEndings("\n").Split('\n')
            // License files written as code comments start their lines with // or *.
            .Select(line => line.Trim().TrimStart('/', '*', '#').Trim())
            // Leaves out the template line of a license text, such as "Copyright (c) <year> <owner>" or
            // Apache's "Copyright [yyyy] [name of copyright owner]".
            .FirstOrDefault(line => Start().IsMatch(line) && !line.Contains('<') && !line.Contains('[') && line.Length > "Copyright".Length + 2);

    /// <summary>The first copyright line of the license files among <paramref name="paths"/>, in name order.</summary>
    public static string? FromFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(path => IsLicenseFile(path)).Order(StringComparer.Ordinal))
        {
            if (File.Exists(path) && FromText(File.ReadAllText(path)) is { } line)
                return line;
        }
        return null;
    }

    public static bool IsLicenseFile(string path) =>
        Path.GetFileNameWithoutExtension(path).ToUpperInvariant() is "LICENSE" or "LICENCE" or "LICENSE-MIT" or "COPYING" or "NOTICE";
}
