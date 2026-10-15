using System.Net;

namespace ComParta.Helper;

/// <summary>
/// Everything a helper is told, read from <c>helper.json</c>, environment variables
/// (<c>Helper__Key</c>) or the command line (<c>--Helper:Key=...</c>), in that order of strength.
/// </summary>
public sealed class HelperOptions
{
    /// <summary>The section of the settings file these come from.</summary>
    public const string Section = "Helper";

    /// <summary>The shortest key accepted, so a key cannot be guessed by trying.</summary>
    public const int MinimumKeyLength = 16;

    /// <summary>The address everyone on the internet reaches this computer at. Torrent apps are sent here.</summary>
    public string PublicAddress { get; set; } = "";

    /// <summary>The secret a ComParta install sends to add or remove a file. Nobody without it can add anything.</summary>
    public string Key { get; set; } = "";

    /// <summary>Where torrent apps connect to get the files (the peer port).</summary>
    public int PeerPort { get; set; } = 6881;

    /// <summary>The public server (tracker), on HTTP and UDP, that tells every app where the files are.</summary>
    public int TrackerPort { get; set; } = 6969;

    /// <summary>Where ComParta installs add and remove files, with the key.</summary>
    public int ControlPort { get; set; } = 6970;

    /// <summary>The biggest single file the helper takes.</summary>
    public long MaxFileBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    /// <summary>The space all files together may take; the oldest are dropped first to make room.</summary>
    public long MaxTotalBytes { get; set; } = 20L * 1024 * 1024 * 1024;

    /// <summary>Where the files and their descriptions are kept. Relative to the program's folder.</summary>
    public string DataFolder { get; set; } = "data";

    /// <summary>A PEM certificate for the control port. Without one the control port speaks plain HTTP.</summary>
    public string CertificatePath { get; set; } = "";

    /// <summary>The PEM private key of <see cref="CertificatePath"/>.</summary>
    public string CertificateKeyPath { get; set; } = "";

    /// <summary>What is wrong with these settings, or nothing when the helper can start.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!IPAddress.TryParse(PublicAddress, out var address)
            || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            problems.Add("PublicAddress must be the address others reach this computer at, such as 203.0.113.7.");
        if (Key.Length < MinimumKeyLength)
            problems.Add($"Key must be at least {MinimumKeyLength} characters long.");
        foreach (var (name, port) in new[] { ("PeerPort", PeerPort), ("TrackerPort", TrackerPort), ("ControlPort", ControlPort) })
        {
            if (port is < 1 or > 65535)
                problems.Add($"{name} must be between 1 and 65535.");
        }
        if (new[] { PeerPort, TrackerPort, ControlPort }.Distinct().Count() != 3)
            problems.Add("PeerPort, TrackerPort and ControlPort must all be different.");
        if (MaxFileBytes <= 0 || MaxTotalBytes <= 0)
            problems.Add("MaxFileBytes and MaxTotalBytes must be more than zero.");
        if (string.IsNullOrWhiteSpace(DataFolder))
            problems.Add("DataFolder must name a folder.");
        if (string.IsNullOrEmpty(CertificatePath) != string.IsNullOrEmpty(CertificateKeyPath))
            problems.Add("CertificatePath and CertificateKeyPath go together: set both or neither.");
        return problems;
    }

    internal IPAddress Address => IPAddress.Parse(PublicAddress);

    internal bool UsesCertificate => !string.IsNullOrEmpty(CertificatePath);
}
