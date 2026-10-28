namespace ComParta.Core.Ports;

/// <summary>What the system's own protection said about one received file (AD-13).</summary>
public enum ReceivedFileVerdict
{
    /// <summary>The file was marked as coming from another computer, or the system has no check.</summary>
    Passed,
    /// <summary>The protection removed, quarantined or refused the file. ComParta will not open it.</summary>
    Blocked,
}

/// <summary>
/// Hands one finished received file to the system's own protection (AD-13): on Windows the
/// attachment services, which write the "came from another computer" mark and let the installed
/// antivirus look at the file. It may take seconds; core calls it off the UI thread, once per file.
/// It never opens or runs the file.
/// </summary>
public interface IReceivedFileGuard
{
    /// <param name="fullPath">The full path of one received file, inside its Received folder.</param>
    ReceivedFileVerdict Check(string fullPath);
}

/// <summary>
/// For systems without such a check (Android, where the platform's own sandbox applies): every file
/// passes and nothing is marked. It protects nothing; never use it on Windows.
/// </summary>
public sealed class NoReceivedFileCheck : IReceivedFileGuard
{
    public static readonly NoReceivedFileCheck Instance = new();

    private NoReceivedFileCheck() { }

    public ReceivedFileVerdict Check(string fullPath) => ReceivedFileVerdict.Passed;
}
