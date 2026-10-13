using ComParta.Core;
using ComParta.Core.Ports;
using MonoTorrent;
using MonoTorrent.Client;

namespace ComParta.Engine.MonoTorrent;

// Activity (AD-22) of the engine; the class is split by area, see MonoTorrentTransferEngine.cs.
public sealed partial class MonoTorrentTransferEngine
{
    public EngineActivity GetActivity()
    {
        if (_engine is not { } engine)
            return EngineActivity.None;
        var shares = new Dictionary<ShareId, ItemActivity>();
        foreach (var offer in _offers.Values)
        {
            if (offer.Manager is { } manager)
            {
                var receivers = offer.Receivers;
                int finished;
                lock (offer.Finished)
                    finished = offer.Finished.Count;
                shares[offer.Id] = ActivityOf(manager) with
                {
                    Receivers = receivers.Count,
                    ReceiversPercent = receivers.Percent,
                    Finished = finished,
                };
            }
        }
        var transfers = new Dictionary<TransferId, ItemActivity>();
        foreach (var incoming in _incoming.Values)
            transfers[incoming.Id] = ActivityOf(incoming.Manager);
        return new EngineActivity(engine.TotalDownloadRate, engine.TotalUploadRate, shares, transfers);
    }

    private static ItemActivity ActivityOf(TorrentManager manager) => new(
        manager.Monitor.DownloadRate,
        manager.Monitor.UploadRate,
        manager.OpenConnections,
        manager.Monitor.DataBytesReceived,
        manager.Monitor.DataBytesSent);

    /// <summary>
    /// How long a stop waits for the servers a public download names to hear goodbye. Without a limit
    /// MonoTorrent waits for each of them, one file after another, and Stop looks broken.
    /// </summary>
    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(2);

    public async Task SetStoppedAsync(bool sending, bool receiving, CancellationToken ct = default)
    {
        _sendingStopped = sending;
        _receivingStopped = receiving;
        // Every file at once, so many files stop as quickly as one.
        var work = new List<Task>();
        foreach (var offer in _offers.Values)
        {
            if (offer.Manager is not { } manager || offer.Invite is null)
                continue;
            // Stopping, not pausing: a paused offer still answers the computers already connected.
            if (sending)
                work.Add(StopFullyAsync(manager));
            else if (!offer.Paused && manager.State == TorrentState.Stopped)
                work.Add(manager.StartAsync());
        }
        foreach (var incoming in _incoming.Values)
        {
            if (incoming.Finishing)
                continue;
            // Stopping, not pausing: a transfer still fetching the file's details cannot pause.
            if (receiving)
                work.Add(StopFullyAsync(incoming.Manager));
            else if (!incoming.Paused)
            {
                // A transfer the person paused on its own stays paused.
                work.Add(StartIncomingAgainAsync(incoming));
            }
        }
        foreach (var task in work)
        {
            try { await task; }
            catch (Exception) { /* the next change tries again */ }
        }
        RaiseActivityChanged();
    }

    private async Task StartIncomingAgainAsync(Incoming incoming)
    {
        await incoming.Manager.StartAsync();
        await TryHintsAsync(incoming);
    }

    /// <summary>
    /// Stops a manager and makes sure it stayed stopped. MonoTorrent's stop can throw while a
    /// stop it started is still running and leave the manager going, so it is asked again.
    /// </summary>
    private static async Task StopFullyAsync(TorrentManager manager)
    {
        for (var attempt = 0; attempt < 5 && manager.State != TorrentState.Stopped; attempt++)
        {
            try { await manager.StopAsync(StopWait); }
            catch (ObjectDisposedException) { await Task.Delay(100); }
            catch (TorrentException) { await Task.Delay(100); }
        }
    }

    private async Task TickActivityAsync()
    {
        foreach (var offer in _offers.Values)
        {
            if (offer.Manager is { } manager)
            {
                try { await CountReceiversAsync(offer, manager); }
                catch (Exception) { /* the next tick tries again */ }
            }
        }
        RaiseActivityChanged();
    }

    /// <summary>
    /// A connected device that lacks part of the file is getting it. One counts as finished once it
    /// has all of the file and some of it came from this device, so another sender is never counted.
    /// </summary>
    private static async Task CountReceiversAsync(Offer offer, TorrentManager manager)
    {
        var count = 0;
        double percent = 0;
        foreach (var peer in await manager.GetPeersAsync())
        {
            if (peer.BitField is not { } have)
                continue;
            if (have.AllTrue)
                CountIfFinished(offer, peer, 0);
            else
            {
                count++;
                percent += have.PercentComplete;
            }
        }
        offer.Receivers = count == 0 ? ReceiverCount.None : new ReceiverCount(count, (int)Math.Floor(percent / count));
    }

    private static void CountIfFinished(Offer offer, PeerId peer, long size)
    {
        if (peer.BitField is not { } have || peer.Monitor.DataBytesSent <= 0)
            return;
        // A device that leaves right after finishing often has not told which last parts it has, so
        // what was sent to it counts too.
        var missing = (long)Math.Ceiling(size * (1 - have.PercentComplete / 100));
        if (!have.AllTrue && peer.Monitor.DataBytesSent < missing)
            return;
        lock (offer.Finished)
            offer.Finished.Add(peer.PeerID.ToHex());
    }

    /// <summary>A device that finishes usually leaves at once, often between two ticks.</summary>
    private static void WatchReceivers(Offer offer, TorrentManager manager) =>
        manager.PeerDisconnected += (_, e) => CountIfFinished(offer, e.Peer, manager.Torrent?.Size ?? long.MaxValue);

    private void RaiseActivityChanged()
    {
        try { ActivityChanged?.Invoke(); }
        catch (Exception) { /* a listener's problem must not stop the engine */ }
    }
}
