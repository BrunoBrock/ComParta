namespace ComParta.Core.UseCases;

/// <summary>
/// Invites the operating system handed to the app, such as a clicked <c>comparta:</c> link
/// (AD-14, AD-16). The newest one waits here until the screen takes it.
/// </summary>
public sealed class OpenedInvites
{
    private readonly object _lock = new();
    private string? _pending;

    /// <summary>Raised when an invite is waiting.</summary>
    public event Action? Opened;

    /// <summary>
    /// Looks for a ComParta link in what the app was started with. Anything else is ignored, so a
    /// stray argument never opens the receive step.
    /// </summary>
    public bool Open(IEnumerable<string> arguments)
    {
        var link = arguments.Select(InviteCodec.FindLink).LastOrDefault(found => found is not null);
        if (link is null)
            return false;
        lock (_lock)
            _pending = link;
        Opened?.Invoke();
        return true;
    }

    /// <summary>The waiting invite, once; null when there is none.</summary>
    public string? Take()
    {
        lock (_lock)
        {
            var link = _pending;
            _pending = null;
            return link;
        }
    }
}
