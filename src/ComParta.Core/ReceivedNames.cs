using System.Text;

namespace ComParta.Core;

/// <summary>
/// The one function every name taken from received metadata passes through before it
/// becomes a path (AD-6).
/// </summary>
public static class ReceivedNames
{
    private const int MaxLength = 150;
    private const string Fallback = "Received file";

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    // Fixed list so the result is the same on every platform.
    private const string InvalidChars = "<>:\"/\\|?*";

    /// <summary>Turns any received name into a single safe file or folder name.</summary>
    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Fallback;

        // Keep only the last segment: a name can never carry a directory.
        var lastSeparator = name.LastIndexOfAny(['/', '\\']);
        if (lastSeparator >= 0)
            name = name[(lastSeparator + 1)..];

        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsControl(c) || InvalidChars.Contains(c))
                builder.Append('_');
            else
                builder.Append(c);
        }

        var clean = builder.ToString().Trim().TrimEnd('.', ' ').TrimStart(' ');
        if (clean.Length == 0 || clean.All(c => c == '.' || c == '_'))
            return Fallback;

        if (clean.Length > MaxLength)
        {
            var extension = Path.GetExtension(clean);
            if (extension.Length > 20)
                extension = "";
            clean = clean[..(MaxLength - extension.Length)].TrimEnd('.', ' ') + extension;
        }

        var stem = clean.Split('.')[0];
        if (ReservedNames.Contains(stem))
            clean = "_" + clean;

        return clean;
    }

    /// <summary>
    /// A path directly under <paramref name="root"/> for a sanitised name that does not exist yet,
    /// adding " (2)", " (3)"… when needed.
    /// </summary>
    public static string UniquePathUnder(string root, string? receivedName, Func<string, bool> exists)
    {
        var name = Sanitize(receivedName);
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        var candidate = Path.Combine(root, name);
        for (var n = 2; exists(candidate); n++)
            candidate = Path.Combine(root, $"{stem} ({n}){extension}");
        return candidate;
    }

    /// <summary>True when <paramref name="path"/> is inside <paramref name="root"/> after resolving it.</summary>
    public static bool IsInside(string root, string path)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }
}
