using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <summary>What the speed bar shows: the totals, and whether sending or receiving is stopped (CAP-27).</summary>
public sealed record Activity(long ReceivingBytesPerSecond, long SendingBytesPerSecond, bool SendingStopped, bool ReceivingStopped);

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
        lock (_lock)
            return new Activity(activity.ReceivingBytesPerSecond, activity.SendingBytesPerSecond, _sendingStopped, _receivingStopped);
    }

    /// <summary>Stops or starts sending and receiving. Nothing is cancelled or removed.</summary>
    public Task SetStoppedAsync(bool sending, bool receiving, CancellationToken ct = default) =>
        ApplyAsync(sending, receiving, store: true, ct);

    /// <summary>What one share is doing now.</summary>
    public ItemActivity For(ShareId id) => _engine.GetActivity().Shares.GetValueOrDefault(id) ?? ItemActivity.None;

    /// <summary>What one transfer is doing now.</summary>
    public ItemActivity For(TransferId id) => _engine.GetActivity().Transfers.GetValueOrDefault(id) ?? ItemActivity.None;

    private async Task ApplyAsync(bool sending, bool receiving, bool store, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (store)
            {
                await _settings.SetAsync(SendingStoppedKey, sending ? "true" : null, ct);
                await _settings.SetAsync(ReceivingStoppedKey, receiving ? "true" : null, ct);
            }
            await _engine.SetStoppedAsync(sending, receiving, ct);
            lock (_lock)
            {
                _sendingStopped = sending;
                _receivingStopped = receiving;
            }
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
