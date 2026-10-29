namespace ComParta.Core.UseCases;

/// <summary>
/// Invites the operating system handed to the app, such as a clicked <c>comparta:</c> link, a
/// download link clicked in the browser, or a download file opened in Windows (AD-14, AD-16,
/// epic 14 story 5). The newest one waits here until the screen takes it.
/// </summary>
/// <param name="findLink">The engine's own link inside an argument, or null; core never reads one itself (AD-2).</param>
/// <param name="downloadFileExtensions">The endings of download files, as the engine names them.</param>
public sealed class OpenedInvites(Func<string, string?>? findLink = null, IReadOnlyList<string>? downloadFileExtensions = null)
{
    private readonly object _lock = new();
    private string? _pending;
    private string? _pendingFile;
    private bool _noInvite;

    /// <summary>Raised when an invite is waiting.</summary>
    public event Action? Opened;

    /// <summary>
    /// Looks for a ComParta link, a download link or a download file in what the app was started
    /// with. Anything else is ignored, so a stray argument never opens the receive step.
    /// </summary>
    public bool Open(IEnumerable<string> arguments)
    {
        string? link = null;
        string? file = null;
        foreach (var argument in arguments)
        {
            if ((InviteCodec.FindLink(argument) ?? FindEngineLink(argument)) is { } found)
            {
                link = found;
                file = null;
            }
            else if (IsDownloadFile(argument))
            {
                file = argument;
                link = null;
            }
        }
        if (link is null && file is null)
            return false;
        lock (_lock)
        {
            _pending = link;
            _pendingFile = file;
            _noInvite = false;
        }
        Opened?.Invoke();
        return true;
    }

    /// <summary>
    /// A whole message another app handed over, such as a chat message shared to ComParta from a
    /// phone's Share menu (epic 7 story 6). Its invite or download link waits like a clicked one; a
    /// message with neither waits too, so the screen can say so instead of doing nothing.
    /// </summary>
    public bool OpenMessage(string? text)
    {
        if (text is not null && Open([text]))
            return true;
        lock (_lock)
        {
            _pending = null;
            _pendingFile = null;
            _noInvite = true;
        }
        Opened?.Invoke();
        return false;
    }

    /// <summary>True, once, when the last message handed over had no invite in it.</summary>
    public bool TakeNoInvite()
    {
        lock (_lock)
        {
            var none = _noInvite;
            _noInvite = false;
            return none;
        }
    }

    private string? FindEngineLink(string argument)
    {
        if (findLink is null || string.IsNullOrWhiteSpace(argument))
            return null;
        try { return findLink(argument); }
        catch (Exception) { return null; }
    }

    private bool IsDownloadFile(string argument) =>
        downloadFileExtensions is { } endings
        && Path.IsPathFullyQualified(argument)
        && endings.Any(ending => argument.EndsWith(ending, StringComparison.OrdinalIgnoreCase));

    /// <summary>The waiting invite or download link, once; null when there is none.</summary>
    public string? Take()
    {
        lock (_lock)
        {
            var link = _pending;
            _pending = null;
            return link;
        }
    }

    /// <summary>
    /// Where the waiting download file is, once; null when there is none. The path stays in the app
    /// and never reaches the screen (AD-18).
    /// </summary>
    public string? TakeFile()
    {
        lock (_lock)
        {
            var file = _pendingFile;
            _pendingFile = null;
            return file;
        }
    }
}
