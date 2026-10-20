using ComParta.Core;
using ComParta.Core.Ports;
using MonoTorrent.Client;

namespace ComParta.Engine.MonoTorrent;

// Reachability of the engine; the class is split by area, see MonoTorrentTransferEngine.cs.
public sealed partial class MonoTorrentTransferEngine
{
    /// <summary>The facts about being reachable as they are right now. For tests and the report loop.</summary>
    internal ReachabilityFacts ReadReachability() =>
        _probe.Read(_policy, _engine, _portMapper?.ExternalAddress) with
        {
            ReachableThroughHelper = _helper is not null && _helperHoldsEverything,
        };

    /// <summary>Raises <see cref="ReachabilityChanged"/> when the facts differ from the last report.</summary>
    internal void ReportReachability()
    {
        // One report at a time, so reports reach listeners in order and a direct call always reports.
        lock (_reachabilityGate)
        {
            try
            {
                var facts = ReadReachability();
                if (facts == _lastReachability)
                    return;
                _lastReachability = facts;
                ReachabilityChanged?.Invoke(facts);
            }
            catch (Exception)
            {
                // A report must never take the timer down. The facts are stored before the event is
                // raised and an unchanged answer is not raised again, so a delivery that failed is
                // dropped, not retried: the bridge's connection.get always has the current answer.
            }
        }
    }

    /// <summary>Tells the probe about connections other devices make to this manager's transfer.</summary>
    private void WatchIncoming(TorrentManager manager) =>
        manager.PeerConnected += (_, args) => _probe.NoteConnection(args.Direction, args.Peer.Uri);

    private void OnNetworkAddressChanged(object? sender, EventArgs e) => ReportReachability();
}
