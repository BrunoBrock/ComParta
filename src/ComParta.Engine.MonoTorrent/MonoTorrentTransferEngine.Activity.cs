using ComParta.Core;
using ComParta.Core.Ports;
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
                shares[offer.Id] = ActivityOf(manager);
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

    public async Task SetStoppedAsync(bool sending, bool receiving, CancellationToken ct = default)
    {
        _sendingStopped = sending;
        _receivingStopped = receiving;
        foreach (var offer in _offers.Values)
        {
            if (offer.Manager is not { } manager || offer.Invite is null)
                continue;
            try
            {
                // Stopping, not pausing: a paused offer still answers the computers already connected.
                if (sending)
                    await StopFullyAsync(manager);
                else if (!sending && manager.State == TorrentState.Stopped)
                    await manager.StartAsync();
            }
            catch (Exception) { /* the next change tries again */ }
        }
        foreach (var incoming in _incoming.Values)
        {
            if (incoming.Finishing)
                continue;
            try
            {
                // Stopping, not pausing: a transfer still fetching the file's details cannot pause.
                if (receiving)
                    await StopFullyAsync(incoming.Manager);
                else if (!receiving && !incoming.Paused)
                {
                    // A transfer the person paused on its own stays paused.
                    await incoming.Manager.StartAsync();
                    await TryHintsAsync(incoming);
                }
            }
            catch (Exception) { /* the next change tries again */ }
        }
        RaiseActivityChanged();
    }

    /// <summary>
    /// Stops a manager and makes sure it stayed stopped. MonoTorrent's stop can throw while a
    /// stop it started is still running and leave the manager going, so it is asked again.
    /// </summary>
    private static async Task StopFullyAsync(TorrentManager manager)
    {
        for (var attempt = 0; attempt < 5 && manager.State != TorrentState.Stopped; attempt++)
        {
            try { await manager.StopAsync(); }
            catch (ObjectDisposedException) { await Task.Delay(100); }
        }
    }

    private void RaiseActivityChanged()
    {
        try { ActivityChanged?.Invoke(); }
        catch (Exception) { /* a listener's problem must not stop the engine */ }
    }
}
