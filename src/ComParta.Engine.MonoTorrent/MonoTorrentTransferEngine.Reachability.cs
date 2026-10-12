using System.Net.Sockets;
using ComParta.Core.Ports;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Connections;

namespace ComParta.Engine.MonoTorrent;

// Reachability of the engine; the class is split by area, see MonoTorrentTransferEngine.cs.
public sealed partial class MonoTorrentTransferEngine
{
    public ReachabilityFacts GetReachability()
    {
        // MonoTorrent opens its listeners only while something is shared or received, so an idle
        // engine counts as listening unless every listener it opened found its port taken.
        bool listening;
        lock (_listeners)
        {
            listening = _engine is { } engine && engine.Settings.ListenEndPoints.Count > 0
                && (_listeners.Count == 0 || _listeners.Any(l => l.Status != ListenerStatus.PortNotFree));
        }
        var mappedPort = _portMapper?.MappedPort(_policy.ListenPort);
        var lastIncoming = Interlocked.Read(ref _lastIncomingTicks);
        return new ReachabilityFacts(
            Listening: listening,
            PortMapped: mappedPort is not null,
            MappedPort: mappedPort,
            GlobalIPv6: AcceptIncoming && Socket.OSSupportsIPv6 && GlobalIPv6Addresses().Any(),
            LastIncomingAt: lastIncoming == 0 ? null : new DateTimeOffset(lastIncoming, TimeSpan.Zero));
    }

    private void WatchIncoming(TorrentManager manager) =>
        manager.PeerConnected += (_, e) =>
        {
            if (e.Direction != Direction.Incoming)
                return;
            Interlocked.Exchange(ref _lastIncomingTicks, DateTimeOffset.UtcNow.UtcTicks);
            RaiseReachabilityChanged();
        };

    private void OnNetworkAddressChanged(object? sender, EventArgs e) => RaiseReachabilityChanged();

    private void RaiseReachabilityChanged()
    {
        try { ReachabilityChanged?.Invoke(); }
        catch (Exception) { /* a listener's problem must not stop the engine */ }
    }
}
