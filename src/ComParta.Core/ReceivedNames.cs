using System.Globalization;
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
        // Superscript digits count as device names too, and the console's own names.
        "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³", "CONIN$", "CONOUT$",
    };

    /// <summary>
    /// Received files that can run something when opened (AD-13): programs, installers, scripts,
    /// shortcuts, disk images, and Office formats that carry macros. Opening one asks twice.
    /// </summary>
    private static readonly HashSet<string> RiskyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Programs and installers.
        ".exe", ".com", ".scr", ".pif", ".cpl", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".msp", ".mst",
        ".appref-ms", ".application", ".gadget", ".dll", ".sys", ".drv", ".ocx", ".jar", ".msc",
        // Scripts.
        ".bat", ".cmd", ".ps1", ".psm1", ".psd1", ".ps1xml", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".wsc",
        ".hta", ".sct", ".py", ".pyw", ".reg", ".inf", ".scf", ".chm", ".htm", ".html", ".mht", ".mhtml", ".svg", ".xht",
        // Shortcuts and things that point somewhere else.
        ".lnk", ".url", ".website", ".library-ms", ".settingcontent-ms", ".search-ms", ".searchconnector-ms",
        // Disk images and archives Windows opens as a drive.
        ".iso", ".img", ".vhd", ".vhdx",
        // Office formats with macros.
        ".docm", ".dotm", ".xlsm", ".xltm", ".xlam", ".xla", ".pptm", ".potm", ".ppam", ".ppsm", ".sldm", ".xll",
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
            // Invisible format characters, such as the right-to-left override that makes
            // "photo\u202Egpj.exe" read as "photoexe.jpg", are dropped so the real extension shows.
            if (char.GetUnicodeCategory(c) is UnicodeCategory.Format)
                continue;
            if (char.IsControl(c) || InvalidChars.Contains(c))
                builder.Append('_');
            // Long runs of spaces could push the real extension out of sight.
            else if (char.IsWhiteSpace(c))
            {
                if (builder.Length == 0 || builder[^1] != ' ')
                    builder.Append(' ');
            }
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

        // Windows ignores spaces before the first dot when it looks for device names.
        var stem = clean.Split('.')[0].TrimEnd(' ');
        if (ReservedNames.Contains(stem))
            clean = "_" + clean;

        return clean;
    }

    /// <summary>True when opening a received file with this name could run something (AD-13).</summary>
    public static bool IsRisky(string? name) =>
        !string.IsNullOrEmpty(name) && RiskyExtensions.Contains(Path.GetExtension(Sanitize(name)));

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
        // A drive's own root, such as D:\, already ends in a separator; any other folder gets one.
        var fullRoot = Path.GetFullPath(root);
        if (!Path.EndsInDirectorySeparator(fullRoot))
            fullRoot += Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }
}
