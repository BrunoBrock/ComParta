using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <summary>What a share is doing for other people now.</summary>
public enum SendingActivity
{
    /// <summary>Nobody is getting it and nobody has finished since the app started.</summary>
    Idle,
    /// <summary>One or more devices are getting it now.</summary>
    Sending,
    /// <summary>Nobody is getting it now, and someone finished since the app started.</summary>
    Sent,
}

/// <summary>The sender's view of a share (CAP-8): counts only, never who or where.</summary>
/// <param name="Percent">How much the devices getting it have, on average, 0 to 100.</param>
/// <param name="SecondsLeft">Roughly how long until they have all of it, or null when it cannot be told.</param>
public sealed record Sending(SendingActivity Activity, int ReceiverCount, int Percent, int FinishedCount, long? SecondsLeft)
{
    public static Sending Idle { get; } = new(SendingActivity.Idle, 0, 0, 0, null);
}

/// <summary>
/// Keeps, for each share, who is getting it as the engine reports it. Live state only: nothing is
/// stored, so after a restart a share shows ready until a device connects again.
/// </summary>
public sealed class SendingUseCases
{
    private readonly ITransferEngine _engine;
    private readonly ShareUseCases _shares;
    private readonly TimeProvider _clock;
    private readonly object _lock = new();
    private readonly Dictionary<ShareId, TimeLeft> _timeLeft = [];
    private Dictionary<ShareId, Sending> _current = [];

    public SendingUseCases(ITransferEngine engine, ShareUseCases shares, TimeProvider? clock = null)
    {
        _engine = engine;
        _shares = shares;
        _clock = clock ?? TimeProvider.System;
        _engine.ActivityChanged += Refresh;
    }

    /// <summary>Raised when what any share is doing for other people changed.</summary>
    public event Action? Changed;

    public Sending For(ShareId id)
    {
        lock (_lock)
            return _current.GetValueOrDefault(id) ?? Sending.Idle;
    }

    private void Refresh()
    {
        EngineActivity activity;
        try { activity = _engine.GetActivity(); }
        catch (Exception) { return; }
        var next = new Dictionary<ShareId, Sending>();
        lock (_lock)
        {
            foreach (var view in _shares.Current())
            {
                var id = view.Share.Id;
                if (activity.Shares.GetValueOrDefault(id) is not { } item || (item.Receivers == 0 && item.Finished == 0))
                {
                    _timeLeft.Remove(id);
                    continue;
                }
                if (item.Receivers == 0)
                {
                    _timeLeft.Remove(id);
                    next[id] = new Sending(SendingActivity.Sent, 0, 0, item.Finished, null);
                    continue;
                }
                if (!_timeLeft.TryGetValue(id, out var timeLeft))
                    _timeLeft[id] = timeLeft = new TimeLeft(_clock);
                var percent = Math.Clamp(item.ReceiversPercent, 0, 100);
                var remaining = (long)Math.Ceiling(view.Share.SizeBytes * (100 - percent) / 100.0) * item.Receivers;
                next[id] = new Sending(
                    SendingActivity.Sending, item.Receivers, percent, item.Finished, timeLeft.Update(item.BytesSent, remaining));
            }
            if (next.Count == _current.Count && next.All(pair => _current.GetValueOrDefault(pair.Key) == pair.Value))
                return;
            _current = next;
        }
        Changed?.Invoke();
    }
}
