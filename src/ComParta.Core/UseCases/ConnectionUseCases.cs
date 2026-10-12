using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <summary>
/// This computer's connection health, from the engine's facts and the firewall's answer (AD-15).
/// </summary>
public sealed class ConnectionUseCases
{
    /// <summary>An incoming connection this recent proves others can reach this computer.</summary>
    public static readonly TimeSpan RecentIncoming = TimeSpan.FromHours(1);

    private readonly ITransferEngine _engine;
    private readonly IFirewall? _firewall;
    private readonly TimeProvider _clock;
    private readonly object _lock = new();
    private ConnectionHealth? _last;

    public ConnectionUseCases(ITransferEngine engine, IFirewall? firewall = null, TimeProvider? clock = null)
    {
        _engine = engine;
        _firewall = firewall;
        _clock = clock ?? TimeProvider.System;
        _engine.ReachabilityChanged += () => Refresh();
    }

    /// <summary>Raised with the new health when it changes.</summary>
    public event Action<ConnectionHealth>? Changed;

    /// <summary>The health now. Raises <see cref="Changed"/> when it differs from the last one reported.</summary>
    public ConnectionHealth Refresh()
    {
        var health = Decide(_engine.GetReachability(), AskFirewall(), _clock.GetUtcNow());
        bool changed;
        lock (_lock)
        {
            changed = _last is not null && _last != health;
            _last = health;
        }
        if (changed)
            Changed?.Invoke(health);
        return health;
    }

    public Task OpenSettingsAsync(CancellationToken ct = default) =>
        _firewall is null ? Task.CompletedTask : _firewall.OpenSettingsAsync(ct);

    /// <summary>
    /// Blocked when the app cannot accept connections at all; good when the router opened a way in or
    /// someone connected in the last hour; limited otherwise. An address on the newer internet
    /// protocol alone is not enough, because many home routers still refuse connections to it.
    /// </summary>
    public static ConnectionHealth Decide(ReachabilityFacts facts, bool? firewallAllows, DateTimeOffset now)
    {
        if (!facts.Listening || firewallAllows == false)
            return ConnectionHealth.Blocked;
        if (facts.LastIncomingAt is { } seen && now - seen <= RecentIncoming)
            return ConnectionHealth.Good;
        return facts.PortMapped ? ConnectionHealth.Good : ConnectionHealth.Limited;
    }

    private bool? AskFirewall()
    {
        try { return _firewall?.AllowsIncoming(); }
        catch (Exception) { return null; }
    }
}
