using Microsoft.Win32;

namespace ComParta.Shell;

/// <summary>
/// Registers the <c>comparta:</c> scheme for this user, so a clicked invite link opens ComParta
/// (AD-16, CAP-16). The unpackaged build writes it at every start, which also follows the app
/// when its folder moves; the MSIX package declares it in its manifest instead.
/// </summary>
public static class LinkScheme
{
    public const string Scheme = "comparta";

    /// <summary>What Windows runs for a link: the app with the link as its one argument.</summary>
    public static string CommandFor(string executablePath) => $"\"{executablePath}\" \"%1\"";

    /// <summary>Writes the scheme under the current user. Returns false when it could not.</summary>
    public static bool Register(string executablePath)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            using var root = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}");
            root.SetValue(null, "URL:ComParta invite");
            root.SetValue("URL Protocol", "");
            using (var icon = root.CreateSubKey("DefaultIcon"))
                icon.SetValue(null, $"\"{executablePath}\",0");
            using var command = root.CreateSubKey(@"shell\open\command");
            var wanted = CommandFor(executablePath);
            if (!Equals(command.GetValue(null), wanted))
                command.SetValue(null, wanted);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
