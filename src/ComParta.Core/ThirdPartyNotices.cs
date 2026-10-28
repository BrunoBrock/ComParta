namespace ComParta.Core.Ports;

/// <summary>
/// The licenses of the parts ComParta uses (epic 5 story 1), as the plain text shipped with the
/// app. The shell reads its own copy; the screen never sees where it lives (AD-7).
/// </summary>
public interface IThirdPartyNotices
{
    /// <summary>The whole text, or null when the shipped file cannot be found.</summary>
    Task<string?> ReadAsync(CancellationToken ct = default);
}

/// <summary>The name the notices file ships under, next to the app and in the package.</summary>
public static class NoticesFile
{
    public const string Name = "THIRD-PARTY-NOTICES.txt";
}
