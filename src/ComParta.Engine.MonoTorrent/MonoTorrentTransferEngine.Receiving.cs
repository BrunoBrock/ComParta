using System.Net;
using System.Net.Sockets;
using ComParta.Core;
using ComParta.Core.Ports;
using MonoTorrent;
using MonoTorrent.Client;

namespace ComParta.Engine.MonoTorrent;

// Receiving of the engine; the class is split by area, see MonoTorrentTransferEngine.cs.
public sealed partial class MonoTorrentTransferEngine
{
    public string? ReadInviteKey(string inviteText) =>
        TryReadInvite(inviteText, out var link, out _) ? KeyOf(link.InfoHashes) : null;

    public InviteSummary? DescribeInvite(string inviteText) =>
        TryReadInvite(inviteText, out var link, out _) ? new InviteSummary(link.Name, link.Size) : null;

    public string? GetReceiveKey(TransferId id) =>
        _incoming.TryGetValue(id, out var incoming) ? KeyOf(incoming.Manager.InfoHashes) : null;

    public async Task<bool> ReceiveAsync(TransferId id, string inviteText, CancellationToken ct = default)
    {
        if (_engine is null || !TryReadInvite(inviteText, out var link, out var hints))
            return false;

        var invitePath = InvitePath(id);
        var cleaned = link.ToV1String() + string.Concat(hints.Select(h => $"&{AddressHint}={h}"));
        await File.WriteAllTextAsync(invitePath, cleaned, ct);
        if (await StartIncomingAsync(id, link, hints))
            return true;
        DeleteQuietly(invitePath);
        return false;
    }

    public string? ReadDownloadFileKey(byte[] content) =>
        TryLoadDownloadFile(content, out var torrent) ? KeyOf(torrent.InfoHashes) : null;

    public async Task<bool> ReceiveFromFileAsync(TransferId id, byte[] content, CancellationToken ct = default)
    {
        if (_engine is null || !TryLoadDownloadFile(content, out var torrent))
            return false;

        var filePath = DownloadFilePath(id);
        await File.WriteAllBytesAsync(filePath, content, ct);
        if (await StartIncomingAsync(id, torrent))
            return true;
        DeleteQuietly(filePath);
        return false;
    }

    /// <summary>
    /// A .torrent file is accepted only when the policy allows the trackers it names, because the
    /// engine would contact them (AD-8, amended).
    /// </summary>
    private bool TryLoadDownloadFile(byte[] content, out Torrent torrent)
    {
        torrent = null!;
        if (!_policy.AllowServersInDownloadLinks)
            return false;
        try
        {
            return Torrent.TryLoad(content, out torrent!) && torrent is not null && torrent.Files.Count <= MaxFiles;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task PauseReceivingAsync(TransferId id, CancellationToken ct = default)
    {
        if (_incoming.TryGetValue(id, out var incoming))
        {
            incoming.Paused = true;
            try { await incoming.Manager.PauseAsync(); }
            catch (Exception) { /* a transfer that cannot pause stays stopped by the flag */ }
        }
    }

    public async Task ContinueReceivingAsync(TransferId id, CancellationToken ct = default)
    {
        if (_incoming.TryGetValue(id, out var incoming))
        {
            incoming.Paused = false;
            // While everything is stopped it stays still; the global continue starts it.
            if (_receivingStopped)
                return;
            // One that stopped on a full disk may count parts it never wrote, so what is on disk is
            // checked again before it continues.
            if (incoming.Manager.State == TorrentState.Error)
            {
                await StopFullyAsync(incoming.Manager);
                await incoming.Manager.HashCheckAsync(autoStart: false);
            }
            await incoming.Manager.StartAsync();
            await TryHintsAsync(incoming);
        }
    }

    public async Task<bool> ResumeReceivingAsync(TransferId id, CancellationToken ct = default)
    {
        if (_engine is null)
            return false;
        var filePath = DownloadFilePath(id);
        if (File.Exists(filePath))
        {
            return TryLoadDownloadFile(await File.ReadAllBytesAsync(filePath, ct), out var torrent)
                && await StartIncomingAsync(id, torrent);
        }
        var invitePath = InvitePath(id);
        if (!File.Exists(invitePath))
            return false;
        return TryReadInvite(await File.ReadAllTextAsync(invitePath, ct), out var link, out var hints)
            && await StartIncomingAsync(id, link, hints);
    }

    private Task<bool> StartIncomingAsync(TransferId id, MagnetLink link, IReadOnlyList<IPEndPoint> hints) =>
        StartIncomingAsync(id, hints, (engine, folder) =>
            engine.AddAsync(link, folder, ToTorrentSettings(publicDownload: link.AnnounceUrls.Count > 0)));

    private Task<bool> StartIncomingAsync(TransferId id, Torrent torrent) =>
        StartIncomingAsync(id, [], (engine, folder) => engine.AddAsync(torrent, folder, ToTorrentSettings(publicDownload: true)));

    private async Task<bool> StartIncomingAsync(
        TransferId id, IReadOnlyList<IPEndPoint> hints, Func<ClientEngine, string, Task<TorrentManager>> add)
    {
        var folder = Path.Combine(_inProgressRoot, id.ToString());
        try
        {
            Directory.CreateDirectory(folder);
            File.SetAttributes(_inProgressRoot, File.GetAttributes(_inProgressRoot) | FileAttributes.Hidden);

            var manager = await add(_engine!, folder);
            WatchIncoming(manager);
            var incoming = new Incoming(id, manager, folder, hints);
            _incoming[id] = incoming;
            manager.TorrentStateChanged += (_, _) => CheckContained(incoming);
            // While receiving is stopped a new transfer waits, unstarted, for the global continue.
            if (!_receivingStopped)
            {
                await manager.StartAsync();
                await TryHintsAsync(incoming);
            }
            return true;
        }
        catch (Exception)
        {
            // Not something this engine can receive, for example content it already holds.
            DeleteFolderQuietly(folder);
            return false;
        }
    }

    public async Task StopReceivingAsync(TransferId id, CancellationToken ct = default)
    {
        if (_incoming.TryRemove(id, out var incoming))
        {
            await RemoveFromEngineAsync(incoming.Manager);
            DeleteFolderQuietly(incoming.Folder);
        }
        DeleteQuietly(InvitePath(id));
        DeleteQuietly(DownloadFilePath(id));
    }

    /// <summary>For tests: the engine's state for a transfer.</summary>
    internal TorrentState StateOf(TransferId id) => _incoming[id].Manager.State;

    /// <summary>For tests: connect a transfer straight to another engine instead of discovering it.</summary>
    internal Task ConnectAsync(TransferId id, IPEndPoint other) => _incoming[id].Manager.AddPeerAsync(ToPeerInfo(other));

    /// <summary>
    /// For tests: the sharing side connects out to a receiver, as it does when the lookup network
    /// tells it where a receiver is. This is how a transfer works when only the receiver is reachable.
    /// </summary>
    internal Task ConnectOfferAsync(ShareId id, IPEndPoint receiver) => _offers[id].Manager!.AddPeerAsync(ToPeerInfo(receiver));

    private static PeerInfo ToPeerInfo(IPEndPoint endPoint) =>
        new(new Uri($"{(endPoint.AddressFamily == AddressFamily.InterNetworkV6 ? "ipv6" : "ipv4")}://{endPoint}"));

    /// <summary>Tries the addresses the invite carried, again and again while nothing is connected.</summary>
    private static async Task TryHintsAsync(Incoming incoming)
    {
        incoming.HintsTriedAt = Environment.TickCount64;
        foreach (var hint in incoming.Hints)
        {
            try { await incoming.Manager.AddPeerAsync(ToPeerInfo(hint)); }
            catch (Exception) { /* discovery may still find the other device */ }
        }
    }

    private void ReportProgress()
    {
        if (Interlocked.Exchange(ref _reporting, 1) == 1)
            return;
        try
        {
            foreach (var incoming in _incoming.Values)
            {
                if (incoming.Finishing || incoming.Paused || _receivingStopped)
                    continue;
                var manager = incoming.Manager;
                if (manager.State == TorrentState.Error && IsDiskFull(manager.Error?.Exception) && manager.Torrent is { } full)
                {
                    ReceiveProgressed?.Invoke(new ReceiveProgress(
                        incoming.Id, incoming.Name, full.Size, (long)(full.Size * (manager.Progress / 100.0)), false, NoSpace: true));
                    continue;
                }
                if (!CheckContained(incoming) || manager.State == TorrentState.Error)
                {
                    ReceiveProgressed?.Invoke(new ReceiveProgress(incoming.Id, incoming.Name, incoming.Size, 0, false, Failed: true));
                    continue;
                }
                if (incoming.Hints.Count > 0 && manager.OpenConnections == 0
                    && Environment.TickCount64 - incoming.HintsTriedAt > HintRetryInterval.TotalMilliseconds)
                {
                    _ = TryHintsAsync(incoming);
                }
                if (!manager.HasMetadata || manager.Torrent is not { } torrent)
                {
                    ReceiveProgressed?.Invoke(new ReceiveProgress(incoming.Id, null, null, 0, false));
                    continue;
                }

                incoming.Name = ReceivedNames.Sanitize(torrent.Name);
                incoming.Size = torrent.Size;
                if (manager.Complete && manager.State == TorrentState.Seeding)
                {
                    incoming.Finishing = true;
                    _ = FinishAsync(incoming);
                    continue;
                }
                var received = (long)(torrent.Size * (manager.Progress / 100.0));
                ReceiveProgressed?.Invoke(new ReceiveProgress(
                    incoming.Id, incoming.Name, incoming.Size, received, false, BytesPerSecond: manager.Monitor.DownloadRate));
            }
        }
        catch (Exception)
        {
            // A report must never take the timer down; the next tick tries again.
        }
        finally
        {
            Volatile.Write(ref _reporting, 0);
        }
    }

    // ERROR_HANDLE_DISK_FULL and ERROR_DISK_FULL on Windows; ENOSPC elsewhere.
    private static readonly int[] DiskFullCodes = [unchecked((int)0x80070027), unchecked((int)0x80070070), 28];

    /// <summary>True when a write failed because the disk is full, however deep the cause sits.</summary>
    internal static bool IsDiskFull(Exception? error)
    {
        for (var e = error; e is not null; e = e.InnerException)
        {
            if (e is IOException && DiskFullCodes.Contains(e.HResult))
                return true;
        }
        return false;
    }

    /// <summary>
    /// More files than any family sends at once. Content announcing more is refused, so hostile
    /// metadata cannot flood the disk with entries (CAP-9).
    /// </summary>
    internal const int MaxFiles = 10_000;

    /// <summary>
    /// Stops a transfer whose metadata names a path outside its own folder, or more files than
    /// <see cref="MaxFiles"/>. MonoTorrent refuses such paths itself; this is the second check AD-6 asks for.
    /// </summary>
    private bool CheckContained(Incoming incoming)
    {
        if (incoming.Rejected)
            return false;
        if (!incoming.Manager.HasMetadata)
            return true;
        if (incoming.Manager.Files.Count <= MaxFiles
            && incoming.Manager.Files.All(f => ReceivedNames.IsInside(incoming.Folder, f.FullPath)))
            return true;

        incoming.Rejected = true;
        _ = RemoveFromEngineAsync(incoming.Manager);
        return false;
    }

    private async Task FinishAsync(Incoming incoming)
    {
        try
        {
            await RemoveFromEngineAsync(incoming.Manager);

            // The engine wrote one top-level entry into the transfer's folder; give it its final,
            // sanitised name directly under the Received root without overwriting anything.
            string? finalName = null;
            foreach (var entry in Directory.GetFileSystemEntries(incoming.Folder))
            {
                var target = ReceivedNames.UniquePathUnder(_receivedRoot, Path.GetFileName(entry), p => File.Exists(p) || Directory.Exists(p));
                if (Directory.Exists(entry))
                    Directory.Move(entry, target);
                else
                    File.Move(entry, target);
                finalName ??= Path.GetFileName(target);
            }
            DeleteFolderQuietly(incoming.Folder);
            DeleteQuietly(InvitePath(incoming.Id));
            DeleteQuietly(DownloadFilePath(incoming.Id));
            _incoming.TryRemove(incoming.Id, out _);

            ReceiveProgressed?.Invoke(new ReceiveProgress(incoming.Id, finalName ?? incoming.Name, incoming.Size, incoming.Size ?? 0, true));
        }
        catch (Exception)
        {
            ReceiveProgressed?.Invoke(new ReceiveProgress(incoming.Id, incoming.Name, incoming.Size, 0, false, Failed: true));
        }
    }

    private async Task RemoveFromEngineAsync(TorrentManager manager)
    {
        try
        {
            await manager.StopAsync();
            if (_engine is { } engine)
                await engine.RemoveAsync(manager);
        }
        catch (Exception) { /* it is gone either way */ }
    }
}
