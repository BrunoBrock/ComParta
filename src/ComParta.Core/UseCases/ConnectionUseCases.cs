using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <summary>
/// Turns what the engine observes about being reachable into one answer: can another device
/// reach this computer (AD-15)? These rules are the only place that decides what the facts mean.
/// </summary>
public sealed class ConnectionUseCases
{
    private readonly object _gate = new();
    private ConnectionHealth? _health;

    public ConnectionUseCases(ITransferEngine engine)
    {
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
    /// The rules, in order; the first that applies wins. Null means nothing can be told yet: nothing
    /// is shared or received, or the router has not answered.
    /// </summary>
    public static ConnectionHealth? Reduce(ReachabilityFacts facts)
    {
        if (!facts.Listening && !facts.ListenPortBusy)
            return null;
        if (facts.ListenPortBusy)
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
        var health = Reduce(facts);
        lock (_gate)
        {
            if (_health == health)
                return;
            _health = health;
        }
        Changed?.Invoke();
    }
}
