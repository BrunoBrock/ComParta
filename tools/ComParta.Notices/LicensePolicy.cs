namespace ComParta.Notices;

/// <summary>
/// Which licenses ComParta can ship under its own Zero-Clause BSD license, and which packages fail.
/// A package passes when its SPDX expression is satisfied by the allow-list, or when a reviewed
/// exception below names it.
/// </summary>
public static class LicensePolicy
{
    /// <summary>Permissive licenses: they ask for notices at most, never for ComParta's own code to change license.</summary>
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "MIT", "MIT-0", "BSD-2-Clause", "BSD-3-Clause", "Apache-2.0", "ISC", "0BSD", "Zlib", "MS-PL",
        "Unicode-3.0", "Unicode-DFS-2016", "CC0-1.0", "BlueOak-1.0.0", "PostgreSQL",
    };

    /// <summary>
    /// Packages whose license is a file or a link instead of an expression, each read and reviewed by a
    /// person. A trailing * matches every package whose name starts with what comes before it. To add one,
    /// read the package's license file, then say here what it allows and why that is enough.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string License, string Why)> Exceptions =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            // The download engine and the parts it brings: their license files are the MIT license text.
            ["MonoTorrent"] = ("MIT", "The LICENSE file in the package (3.0.2) is the MIT license."),
            ["Mono.Nat"] = ("MIT", "The LICENSE.md file in the package (3.0.4) is the MIT license."),
            ["ReusableTasks"] = ("MIT", "The LICENSE.md file in the package (4.0.0) is the MIT license."),
            ["Microsoft.Web.WebView2"] = (
                "LicenseRef-Microsoft-WebView2-SDK (BSD-style terms in the package's LICENSE.txt)",
                "The LICENSE.txt in the package allows redistribution in binary form with the notice kept."),
            ["Microsoft.WindowsAppSDK*"] = (
                "LicenseRef-Microsoft-WindowsAppSDK (Microsoft software license terms for the Windows App SDK)",
                "Microsoft's terms in the package's license.txt let an app ship the Windows App SDK runtime with it."),
            ["Microsoft.Windows.SDK.BuildTools*"] = (
                "LicenseRef-Microsoft-Windows-SDK (Microsoft Windows SDK license terms)",
                "Tools that build and package the app; only the parts the Windows SDK terms let apps ship end up in it."),
        };

    /// <summary>The part with the license a reviewed exception gives it, when it declares none.</summary>
    public static Part Resolve(Part part) =>
        string.IsNullOrWhiteSpace(part.License) && ExceptionFor(part.Name) is { } exception
            ? part with { License = exception.License }
            : part;

    /// <summary>One message per failing package, naming it; empty when every package can ship.</summary>
    public static IReadOnlyList<string> Problems(IEnumerable<Part> parts)
    {
        var problems = new List<string>();
        foreach (var part in parts)
        {
            var what = $"{part.Name} {part.Version} ({(part.Source == PartSource.NuGet ? "NuGet" : "npm")})";
            if (string.IsNullOrWhiteSpace(part.License))
            {
                if (ExceptionFor(part.Name) is null)
                    problems.Add($"{what} has no license");
            }
            else if (!IsAllowed(part.License) && ExceptionFor(part.Name)?.License != part.License)
                problems.Add($"{what} uses {part.License}, which ComParta cannot ship");
        }
        return problems;
    }

    public static (string License, string Why)? ExceptionFor(string name)
    {
        foreach (var (pattern, exception) in Exceptions)
        {
            var matches = pattern.EndsWith('*')
                ? name.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
                : name.Equals(pattern, StringComparison.OrdinalIgnoreCase);
            if (matches)
                return exception;
        }
        return null;
    }

    /// <summary>
    /// True when the SPDX expression can be met with allowed licenses only: one choice of an OR is enough,
    /// every side of an AND is needed, and "X WITH exception" counts as X. An expression that cannot be read fails.
    /// </summary>
    public static bool IsAllowed(string expression)
    {
        try
        {
            var reader = new ExpressionReader(expression);
            var allowed = reader.Or();
            return reader.AtEnd && allowed;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>The allowed license ids an expression names, for the license texts at the end of the notices.</summary>
    public static IEnumerable<string> AllowedIds(string expression) =>
        expression.Replace("(", " ").Replace(")", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.TrimEnd('+'))
            .Where(Allowed.Contains)
            .Select(token => Allowed.First(id => id.Equals(token, StringComparison.OrdinalIgnoreCase)));

    private sealed class ExpressionReader(string expression)
    {
        private readonly List<string> _tokens = expression.Replace("(", " ( ").Replace(")", " ) ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        private int _at;

        public bool AtEnd => _at == _tokens.Count;

        public bool Or()
        {
            var allowed = And();
            while (Take("OR"))
                allowed = And() | allowed;
            return allowed;
        }

        private bool And()
        {
            var allowed = License();
            while (Take("AND"))
                allowed = License() & allowed;
            return allowed;
        }

        private bool License()
        {
            if (Take("("))
            {
                var allowed = Or();
                if (!Take(")"))
                    throw new FormatException();
                return allowed;
            }
            if (AtEnd || _tokens[_at] is "(" or ")" || IsOperator(_tokens[_at]))
                throw new FormatException();
            var id = _tokens[_at++].TrimEnd('+');
            if (Take("WITH"))
            {
                if (AtEnd || IsOperator(_tokens[_at]))
                    throw new FormatException();
                _at++;
            }
            return Allowed.Contains(id);
        }

        private bool Take(string token)
        {
            if (AtEnd || !_tokens[_at].Equals(token, StringComparison.OrdinalIgnoreCase))
                return false;
            _at++;
            return true;
        }

        private static bool IsOperator(string token) =>
            token.ToUpperInvariant() is "AND" or "OR" or "WITH";
    }
}
