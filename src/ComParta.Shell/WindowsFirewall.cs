using System.Diagnostics;
using ComParta.Core.Ports;
using Microsoft.Win32;

namespace ComParta.Shell;

/// <summary>
/// Reads what Windows Firewall decided about this app (AD-15). The rules Windows writes when it
/// asks "allow ComParta to use the network?" live in the registry, readable without elevation.
/// </summary>
public sealed class WindowsFirewall(string executablePath) : IFirewall
{
    private const string PolicyKey = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";

    public bool? AllowsIncoming()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            using var policy = Registry.LocalMachine.OpenSubKey(PolicyKey);
            if (policy is null)
                return null;
            if (AllProfilesOff(policy))
                return true;
            using var rules = policy.OpenSubKey("FirewallRules");
            if (rules is null)
                return null;
            return Decide(ReadStrings(rules), executablePath, Environment.ExpandEnvironmentVariables);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>Opens Windows' "allow an app through the firewall" screen.</summary>
    public Task OpenSettingsAsync(CancellationToken ct = default)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("control.exe")
            {
                ArgumentList = { "/name", "Microsoft.WindowsFirewall", "/page", "pageConfigureApps" },
                UseShellExecute = false,
            })?.Dispose();
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// True when an enabled rule lets connections in to the app, false when only blocking rules
    /// exist, null when Windows has not asked yet. Allowing wins: when the person allowed one kind
    /// of network and not the other, connections can still arrive.
    /// </summary>
    internal static bool? Decide(IEnumerable<string> rules, string executablePath, Func<string, string> expand)
    {
        bool? decided = null;
        foreach (var rule in rules)
        {
            var fields = Parse(rule);
            if (!fields.TryGetValue("App", out var app)
                || !string.Equals(Normalize(expand(app)), Normalize(executablePath), StringComparison.OrdinalIgnoreCase)
                || !Is(fields, "Dir", "In")
                || !Is(fields, "Active", "TRUE"))
                continue;
            if (Is(fields, "Action", "Allow"))
                return true;
            if (Is(fields, "Action", "Block"))
                decided = false;
        }
        return decided;
    }

    private static Dictionary<string, string> Parse(string rule)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in rule.Split('|'))
        {
            var equals = part.IndexOf('=');
            if (equals > 0)
                fields.TryAdd(part[..equals], part[(equals + 1)..]);
        }
        return fields;
    }

    private static bool Is(Dictionary<string, string> fields, string key, string value) =>
        fields.TryGetValue(key, out var actual) && string.Equals(actual, value, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path.Trim().Trim('"')); }
        catch (Exception) { return path; }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static List<string> ReadStrings(RegistryKey key) =>
        [.. key.GetValueNames().Select(name => key.GetValue(name)).OfType<string>()];

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool AllProfilesOff(RegistryKey policy)
    {
        foreach (var profile in new[] { "DomainProfile", "StandardProfile", "PublicProfile" })
        {
            using var key = policy.OpenSubKey(profile);
            if (key?.GetValue("EnableFirewall") is not int enabled || enabled != 0)
                return false;
        }
        return true;
    }
}
