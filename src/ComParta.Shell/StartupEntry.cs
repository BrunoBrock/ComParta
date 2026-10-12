using Microsoft.Win32;

namespace ComParta.Shell;

/// <summary>
/// Starts ComParta hidden in the notification area when the person signs in, only when they chose
/// it (AD-14). The unpackaged build writes a per-user Run entry; the MSIX package (epic 5) uses its
/// startup task extension instead.
/// </summary>
public static class StartupEntry
{
    /// <summary>The argument that starts the app with its window hidden.</summary>
    public const string BackgroundArgument = "--background";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ComParta";

    /// <summary>What Windows runs at sign-in.</summary>
    public static string CommandFor(string executablePath) => $"\"{executablePath}\" {BackgroundArgument}";

    /// <summary>True when this launch came from the start-up entry, so the window stays hidden.</summary>
    public static bool IsBackgroundLaunch(IEnumerable<string> arguments) =>
        arguments.Any(argument => string.Equals(argument, BackgroundArgument, StringComparison.OrdinalIgnoreCase));

    /// <summary>Writes or removes the entry for the current user. Returns false when it could not.</summary>
    public static bool Set(string executablePath, bool on)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey);
            if (!on)
            {
                run.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }
            var wanted = CommandFor(executablePath);
            if (!Equals(run.GetValue(ValueName), wanted))
                run.SetValue(ValueName, wanted);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
