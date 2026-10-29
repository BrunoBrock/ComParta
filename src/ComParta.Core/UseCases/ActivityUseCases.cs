using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <summary>
/// What the speed bar shows: the totals, and whether sending or receiving is stopped (CAP-27), with
/// the speeds of each file that is moving, for its card.
/// </summary>
public sealed record Activity(
    long ReceivingBytesPerSecond, long SendingBytesPerSecond, bool SendingStopped, bool ReceivingStopped,
    IReadOnlyList<ItemSpeed> Items)
{
    public bool Equals(Activity? other) =>
        other is not null
        && ReceivingBytesPerSecond == other.ReceivingBytesPerSecond && SendingBytesPerSecond == other.SendingBytesPerSecond
        && SendingStopped == other.SendingStopped && ReceivingStopped == other.ReceivingStopped
        && Items.SequenceEqual(other.Items);

    public override int GetHashCode() => HashCode.Combine(ReceivingBytesPerSecond, SendingBytesPerSecond, SendingStopped, ReceivingStopped, Items.Count);
}

/// <summary>How fast one shared or received file moves now; files that do not move are left out.</summary>
/// <param name="Id">The share's or transfer's id.</param>
public sealed record ItemSpeed(string Id, long ReceivingBytesPerSecond, long SendingBytesPerSecond);

/// <summary>
/// The total speeds and the two Stop flags (AD-22). The flags are kept in the settings store, so a
/// stop survives a restart, and the engine applies them.
/// </summary>
public sealed class ActivityUseCases
{
    private const string SendingStoppedKey = "activity.sendingStopped";
    private const string ReceivingStoppedKey = "activity.receivingStopped";

    private readonly ITransferEngine _engine;
    private readonly ISettingsRepository _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private bool _sendingStopped;
    private bool _receivingStopped;
    private Activity? _lastRaised;

    public ActivityUseCases(ITransferEngine engine, ISettingsRepository settings)
    {
        _engine = engine;
        _settings = settings;
        _engine.ActivityChanged += () => RaiseIfChanged();
    }

    /// <summary>Raised with the new totals when they or the flags change.</summary>
    public event Action<Activity>? Changed;

    /// <summary>Applies the stop the person chose before the app last closed.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        var sending = await _settings.GetAsync(SendingStoppedKey, ct) == "true";
        var receiving = await _settings.GetAsync(ReceivingStoppedKey, ct) == "true";
        await ApplyAsync(sending, receiving, store: false, ct);
    }

    public Activity Current()
    {
        var activity = _engine.GetActivity();
        var items = activity.Shares.Select(s => Speed(s.Key.ToString(), s.Value))
            .Concat(activity.Transfers.Select(t => Speed(t.Key.ToString(), t.Value)))
            .Where(i => i.ReceivingBytesPerSecond > 0 || i.SendingBytesPerSecond > 0)
            .OrderBy(i => i.Id, StringComparer.Ordinal)
            .ToList();
        lock (_lock)
            return new Activity(activity.ReceivingBytesPerSecond, activity.SendingBytesPerSecond, _sendingStopped, _receivingStopped, items);
    }

    /// <summary>Stops or starts sending and receiving. Nothing is cancelled or removed.</summary>
    public Task SetStoppedAsync(bool sending, bool receiving, CancellationToken ct = default) =>
        ApplyAsync(sending, receiving, store: true, ct);

    /// <summary>
    /// Stops sending and receiving without remembering it, as when the phone allowed no more time
    /// in the background; <see cref="StartAsync"/> brings back what the person chose.
    /// </summary>
    public Task StopForNowAsync(CancellationToken ct = default) => ApplyAsync(true, true, store: false, ct);

    /// <summary>What one share is doing now.</summary>
    public ItemActivity For(ShareId id) => _engine.GetActivity().Shares.GetValueOrDefault(id) ?? ItemActivity.None;

    /// <summary>What one transfer is doing now.</summary>
    public ItemActivity For(TransferId id) => _engine.GetActivity().Transfers.GetValueOrDefault(id) ?? ItemActivity.None;

    private static ItemSpeed Speed(string id, ItemActivity activity) =>
        new(id, activity.ReceivingBytesPerSecond, activity.SendingBytesPerSecond);

    private async Task ApplyAsync(bool sending, bool receiving, bool store, CancellationToken ct)
    {
        // The speed bar shows the new state at once; the engine catches up behind it, which can take
        // a moment with many files.
        lock (_lock)
        {
            _sendingStopped = sending;
            _receivingStopped = receiving;
        }
        RaiseIfChanged();
        await _gate.WaitAsync(ct);
        try
        {
            if (store)
            {
                await _settings.SetAsync(SendingStoppedKey, sending ? "true" : null, ct);
                await _settings.SetAsync(ReceivingStoppedKey, receiving ? "true" : null, ct);
            }
            bool latest;
            lock (_lock)
                latest = _sendingStopped == sending && _receivingStopped == receiving;
            // A later click already changed it again; that one applies its own choice.
            if (latest)
                await _engine.SetStoppedAsync(sending, receiving, ct);
        }
        finally { _gate.Release(); }
        RaiseIfChanged();
    }

    private void RaiseIfChanged()
    {
        Activity current;
        try { current = Current(); }
        catch (Exception) { return; }
        lock (_lock)
        {
            if (current == _lastRaised)
                return;
            _lastRaised = current;
        }
        Changed?.Invoke(current);
    }
}
