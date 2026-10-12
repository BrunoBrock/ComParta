using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <summary>
/// Turns what the engine observes about being reachable, and what the firewall says, into one
/// answer: can another device reach this computer (AD-15)? These rules are the only place that
/// decides what the facts mean.
/// </summary>
public sealed class ConnectionUseCases
{
    private readonly IFirewall? _firewall;
    private readonly object _gate = new();
    private ReachabilityFacts? _facts;
    private ConnectionHealth? _health;

    public ConnectionUseCases(ITransferEngine engine, IFirewall? firewall = null)
    {
        _firewall = firewall;
        engine.ReachabilityChanged += OnReachabilityChanged;
    }

    /// <summary>Raised whenever the health really changes, including when it becomes unknown.</summary>
    public event Action? Changed;

    /// <summary>The health as last worked out, or null while nothing can be measured.</summary>
    public ConnectionHealth? Current()
    {
        lock (_gate)
            return _health;
    }

    /// <summary>
    /// Works the health out again from the last facts and what the firewall says now, because the
    /// person can change the firewall without the engine noticing. Raises <see cref="Changed"/> when
    /// it differs from the last answer.
    /// </summary>
    public ConnectionHealth? Refresh()
    {
        ReachabilityFacts? facts;
        lock (_gate)
            facts = _facts;
        return facts is null ? Current() : Update(facts);
    }

    public Task OpenSettingsAsync(CancellationToken ct = default) =>
        _firewall is null ? Task.CompletedTask : _firewall.OpenSettingsAsync(ct);

    /// <summary>
    /// The rules, in order; the first that applies wins. Null means nothing can be told yet: nothing
    /// is shared or received, or the router has not answered. A firewall that refuses incoming
    /// connections (<paramref name="firewallAllows"/> false) makes it blocked whatever the router says.
    /// </summary>
    public static ConnectionHealth? Reduce(ReachabilityFacts facts, bool? firewallAllows = null)
    {
        if (!facts.Listening && !facts.ListenPortBusy)
            return null;
        if (facts.ListenPortBusy || firewallAllows == false)
            return ConnectionHealth.Blocked;
        if (facts.IncomingConnectionSeenRecently)
            return ConnectionHealth.Good;

        var mapped = facts.PortMapping == PortMappingState.Obtained;
        if ((mapped && (facts.ExternalAddress is null || NetworkAddresses.IsPublic(facts.ExternalAddress)))
            || (facts.GlobalIPv6Present && facts.ListeningOnIPv6))
            return ConnectionHealth.Good;
        if (facts.PortMapping == PortMappingState.Pending)
            return null;
        if (mapped)
            return ConnectionHealth.Limited;
        return ConnectionHealth.Blocked;
    }

    private void OnReachabilityChanged(ReachabilityFacts facts)
    {
        lock (_gate)
            _facts = facts;
        Update(facts);
    }

    private ConnectionHealth? Update(ReachabilityFacts facts)
    {
        var health = Reduce(facts, AskFirewall());
        lock (_gate)
        {
            if (_health == health)
                return health;
            _health = health;
        }
        Changed?.Invoke();
        return health;
    }

    private bool? AskFirewall()
    {
        try { return _firewall?.AllowsIncoming(); }
        catch (Exception) { return null; }
    }
}
