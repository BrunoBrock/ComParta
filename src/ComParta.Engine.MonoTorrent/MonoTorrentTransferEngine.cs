using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using ComParta.Core;
using ComParta.Core.Ports;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Connections;

[assembly: InternalsVisibleTo("ComParta.Engine.MonoTorrent.Tests")]

namespace ComParta.Engine.MonoTorrent;

/// <summary>
/// <see cref="ITransferEngine"/> on MonoTorrent. Everything torrent-shaped stays in this project (AD-2):
/// a share is a single-file torrent seeded in place, an invite is its magnet link, and a transfer
/// is a download of that magnet link. A "download link" is any other magnet link and a "download
/// file" is a .torrent file (AD-21); for those the trackers they name are kept (AD-8, amended).
/// </summary>
public sealed class MonoTorrentTransferEngine : ITransferEngine, IAsyncDisposable
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReachabilityInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HintRetryInterval = TimeSpan.FromSeconds(20);

    // The magnet-link parameter for "a peer address to try directly" (BEP 9).
    private const string AddressHint = "x.pe";
    private const int MaxAddressHints = 6;

    /// <summary>What the shell's file dialog shows for "open a download file".</summary>
    public static IReadOnlyList<string> DownloadFileExtensions { get; } = [".torrent"];

    private readonly string _sharesFolder;
    private readonly string _transfersFolder;
    private readonly string _cacheFolder;
    private readonly string _receivedRoot;
    private readonly string _inProgressRoot;
    private readonly ConcurrentDictionary<ShareId, Offer> _offers = new();
    private readonly ConcurrentDictionary<TransferId, Incoming> _incoming = new();
    private ClientEngine? _engine;
    private SharingPolicy _policy = SharingPolicy.Closed;
    private Timer? _progressTimer;
    private Timer? _reachabilityTimer;
    private int _reporting;
    private readonly object _reachabilityGate = new();
    private ReachabilityFacts? _lastReachability;
    private readonly Factories _factories;
    private readonly ReachabilityProbe _probe;

    public MonoTorrentTransferEngine(IAppPaths paths)
        : this(paths, Factories.Default, TimeProvider.System)
    {
    }

    /// <param name="factories">What the engine builds its parts from; tests swap in a fake router.</param>
    /// <param name="clock">Times the one-hour window of an incoming connection from outside.</param>
    /// <param name="isOutsideLocalNetwork">Which addresses count as outside the local network; the usual rule when null.</param>
    internal MonoTorrentTransferEngine(
        IAppPaths paths, Factories factories, TimeProvider clock, Func<IPAddress, bool>? isOutsideLocalNetwork = null)
    {
        _probe = new ReachabilityProbe(clock, isOutsideLocalNetwork);
        // Every listener the engine creates is remembered so its status can be read (AD-15).
        _factories = factories.WithPeerConnectionListenerCreator(
            endPoint => _probe.Track(endPoint, factories.CreatePeerConnectionListener(endPoint)));
        _sharesFolder = Path.Combine(paths.EngineRoot, "shares");
        _transfersFolder = Path.Combine(paths.EngineRoot, "transfers");
        _cacheFolder = Path.Combine(paths.EngineRoot, "cache");
        _receivedRoot = paths.ReceivedRoot;
        // Unfinished data also lives under the Received root so nothing received is written elsewhere (AD-6).
        _inProgressRoot = Path.Combine(paths.ReceivedRoot, ".in-progress");
    }

    public event Action<ShareId, OfferState>? OfferChanged;
    public event Action<ReceiveProgress>? ReceiveProgressed;
    public event Action<ReachabilityFacts>? ReachabilityChanged;

    /// <summary>The endpoint this engine was told to accept connections on. For tests.</summary>
    internal IPEndPoint? ListenEndPoint => _engine?.Settings.ListenEndPoints.Values.FirstOrDefault();

    public Task StartAsync(SharingPolicy policy, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_sharesFolder);
        Directory.CreateDirectory(_transfersFolder);
        Directory.CreateDirectory(_cacheFolder);

        // A port of 0 means "choose one and keep it", so invites given out earlier stay good.
        _policy = policy.ListenPort == 0 ? policy with { ListenPort = LoadOrChoosePort() } : policy;
        _probe.Reset();
        lock (_reachabilityGate)
            _lastReachability = null;
        _engine = new ClientEngine(ToEngineSettings(_policy, _cacheFolder), _factories);
        _progressTimer = new Timer(_ => ReportProgress(), null, ProgressInterval, ProgressInterval);
        _reachabilityTimer = new Timer(_ => ReportReachability(), null, ReachabilityInterval, ReachabilityInterval);
        return Task.CompletedTask;
    }

    /// <summary>The facts about being reachable as they are right now. For tests and the report loop.</summary>
    internal ReachabilityFacts ReadReachability() => _probe.Read(_policy, _engine);

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

    /// <summary>The only place network options are set, and only from the policy (AD-8).</summary>
    internal static EngineSettings ToEngineSettings(SharingPolicy policy, string cacheFolder)
    {
        var builder = new EngineSettingsBuilder
        {
            AllowLocalPeerDiscovery = policy.AllowLocalDiscovery,
            AllowPortForwarding = policy.AllowPortForwarding,
            DhtEndPoint = policy.AllowPublicLookup ? new IPEndPoint(IPAddress.Any, policy.ListenPort) : null,
            AutoSaveLoadDhtCache = policy.AllowPublicLookup,
            AutoSaveLoadFastResume = true,
            AutoSaveLoadMagnetLinkMetadata = true,
            CacheDirectory = cacheFolder,
            ListenEndPoints = new Dictionary<string, IPEndPoint> { ["ipv4"] = new IPEndPoint(IPAddress.Any, policy.ListenPort) },
        };
        if (policy.RequireEncryption)
            builder.AllowedEncryption = [EncryptionType.RC4Full, EncryptionType.RC4Header];
        return builder.ToSettings();
    }

    private int LoadOrChoosePort()
    {
        var portFile = Path.Combine(Path.GetDirectoryName(_sharesFolder)!, "port.txt");
        if (File.Exists(portFile) && int.TryParse(File.ReadAllText(portFile), out var saved) && IsFree(saved))
            return saved;

        using var probe = new TcpListener(IPAddress.Any, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        File.WriteAllText(portFile, port.ToString(CultureInfo.InvariantCulture));
        return port;
    }

    private static bool IsFree(int port)
    {
        if (port is < 1024 or > 65535)
            return false;
        try
        {
            using var probe = new TcpListener(IPAddress.Any, port);
            probe.Start();
            probe.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <param name="publicDownload">True for a download link or file the person brought from elsewhere.</param>
    private TorrentSettings ToTorrentSettings(bool publicDownload = false) => new TorrentSettingsBuilder
    {
        AllowDht = _policy.AllowPublicLookup,
        AllowPeerExchange = _policy.AllowPeerExchange || (publicDownload && _policy.AllowServersInDownloadLinks),
    }.ToSettings();

    public async Task StopAsync()
    {
        if (_progressTimer is { } timer)
        {
            _progressTimer = null;
            await timer.DisposeAsync();
        }
        if (_reachabilityTimer is { } reachabilityTimer)
        {
            _reachabilityTimer = null;
            await reachabilityTimer.DisposeAsync();
        }
        foreach (var offer in _offers.Values)
            offer.Cancel.Cancel();
        if (_engine is { } engine)
        {
            _engine = null;
            try { await engine.StopAllAsync(); }
            catch (Exception) { /* shutting down anyway */ }
            engine.Dispose();
        }
        _offers.Clear();
        _incoming.Clear();
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    // ---- Offering -------------------------------------------------------------------------

    public async Task OfferAsync(ShareId id, string filePath, CancellationToken ct = default)
    {
        await StopOfferAsync(id, forget: false);
        var offer = new Offer(id, filePath);
        _offers[id] = offer;
        OfferChanged?.Invoke(id, OfferState.Preparing);
        offer.Work = Task.Run(() => PrepareOfferAsync(offer), CancellationToken.None);
    }

    private async Task PrepareOfferAsync(Offer offer)
    {
        var engine = _engine;
        if (engine is null)
            return;
        try
        {
            var metadataPath = Path.Combine(_sharesFolder, offer.Id + ".torrent");
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var fromCache = File.Exists(metadataPath);
                var torrent = fromCache
                    ? await Torrent.LoadAsync(metadataPath)
                    : await CreateMetadataAsync(offer, metadataPath);

                // The save directory is the folder the file already sits in, so it is read where it is (AD-6).
                var manager = await engine.AddAsync(torrent, Path.GetDirectoryName(offer.FilePath)!, ToTorrentSettings());
                WatchIncoming(manager);
                offer.Manager = manager;
                await manager.HashCheckAsync(false);
                offer.Cancel.Token.ThrowIfCancellationRequested();

                if (manager.Complete)
                {
                    // Complete means every piece matched, so the engine only ever reads the original.
                    await manager.StartAsync();
                    offer.Invite = new MagnetLink(torrent.InfoHashes, torrent.Name, size: torrent.Size).ToV1String();
                    OfferChanged?.Invoke(offer.Id, OfferState.Ready);
                    return;
                }

                // Never start an incomplete offer: it could write to the original file.
                offer.Manager = null;
                await engine.RemoveAsync(manager);
                if (!fromCache)
                    break;
                // The file changed since it was first shared; describe it again.
                File.Delete(metadataPath);
            }
            OfferChanged?.Invoke(offer.Id, OfferState.Failed);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (!offer.Cancel.IsCancellationRequested)
                OfferChanged?.Invoke(offer.Id, OfferState.Failed);
        }
    }

    private static async Task<Torrent> CreateMetadataAsync(Offer offer, string metadataPath)
    {
        var creator = new TorrentCreator(TorrentType.V1Only) { CreatedBy = "ComParta", Private = false };
        var metadata = await creator.CreateAsync(new TorrentFileSource(offer.FilePath), offer.Cancel.Token);
        await File.WriteAllBytesAsync(metadataPath, metadata.Encode());
        return Torrent.Load(metadata);
    }

    public Task StopOfferingAsync(ShareId id, CancellationToken ct = default) => StopOfferAsync(id, forget: true);

    private async Task StopOfferAsync(ShareId id, bool forget)
    {
        if (_offers.TryRemove(id, out var offer))
        {
            offer.Cancel.Cancel();
            try { await offer.Work; }
            catch (Exception) { /* already reported */ }
            if (offer.Manager is { } manager && _engine is { } engine)
            {
                try
                {
                    await manager.StopAsync();
                    await engine.RemoveAsync(manager);
                }
                catch (Exception) { /* it is gone either way */ }
            }
        }
        if (forget)
            DeleteQuietly(Path.Combine(_sharesFolder, id + ".torrent"));
    }

    public Task<string?> GetInviteAsync(ShareId id, CancellationToken ct = default)
    {
        if (!_offers.TryGetValue(id, out var offer) || offer.Invite is not { } invite)
            return Task.FromResult<string?>(null);

        // The addresses are read now, not when the share was added: a laptop changes networks.
        if (_policy.AllowAddressInInvite)
        {
            foreach (var address in LocalAddresses())
                invite += $"&{AddressHint}={address}:{_policy.ListenPort}";
        }
        return Task.FromResult<string?>(invite);
    }

    private static IEnumerable<IPAddress> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .Distinct()
            .Take(MaxAddressHints);

    // ---- Receiving ------------------------------------------------------------------------

    public string? ReadInviteKey(string inviteText) =>
        TryReadInvite(inviteText, out var link, out _) ? KeyOf(link.InfoHashes) : null;

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
            return Torrent.TryLoad(content, out torrent!) && torrent is not null;
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
            var incoming = new Incoming(id, manager, folder, hints);
            _incoming[id] = incoming;
            manager.TorrentStateChanged += (_, _) => CheckContained(incoming);
            WatchIncoming(manager);
            await manager.StartAsync();
            await TryHintsAsync(incoming);
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

    /// <summary>For tests: connect a transfer straight to another engine instead of discovering it.</summary>
    internal Task ConnectAsync(TransferId id, IPEndPoint other) =>
        _incoming[id].Manager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://{other}")));

    /// <summary>Tries the addresses the invite carried, again and again while nothing is connected.</summary>
    private static async Task TryHintsAsync(Incoming incoming)
    {
        incoming.HintsTriedAt = Environment.TickCount64;
        foreach (var hint in incoming.Hints)
        {
            try { await incoming.Manager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://{hint}"))); }
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
                if (incoming.Finishing || incoming.Paused)
                    continue;
                var manager = incoming.Manager;
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

    /// <summary>
    /// Stops a transfer whose metadata names a path outside its own folder. MonoTorrent refuses such
    /// paths itself; this is the second check AD-6 asks for.
    /// </summary>
    private bool CheckContained(Incoming incoming)
    {
        if (incoming.Rejected)
            return false;
        if (!incoming.Manager.HasMetadata)
            return true;
        if (incoming.Manager.Files.All(f => ReceivedNames.IsInside(incoming.Folder, f.FullPath)))
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

    // ---- Helpers --------------------------------------------------------------------------

    /// <summary>
    /// Reads an invite and drops everything except the content identity, name and size, so an invite
    /// can never make the app contact a server the policy did not allow. A link that names trackers
    /// is a public download link, not a ComParta invite (ComParta never puts trackers in its
    /// invites); it keeps them when the policy allows servers in download links (AD-8, amended).
    /// </summary>
    private bool TryReadInvite(string? inviteText, out MagnetLink link, out IReadOnlyList<IPEndPoint> hints)
    {
        link = null!;
        hints = [];
        try
        {
            if (string.IsNullOrWhiteSpace(inviteText))
                return false;

            // Take the address hints out first; the rest is an ordinary magnet link.
            var found = new List<IPEndPoint>();
            var parts = inviteText.Trim().Split('&');
            foreach (var part in parts.Where(p => p.StartsWith(AddressHint + "=", StringComparison.OrdinalIgnoreCase)))
            {
                if (found.Count < MaxAddressHints
                    && IPEndPoint.TryParse(Uri.UnescapeDataString(part[(AddressHint.Length + 1)..]), out var endPoint)
                    && endPoint.AddressFamily == AddressFamily.InterNetwork && endPoint.Port > 0)
                    found.Add(endPoint);
            }
            var magnet = string.Join('&', parts.Where(p => !p.StartsWith(AddressHint + "=", StringComparison.OrdinalIgnoreCase)));

            if (!MagnetLink.TryParse(magnet, out var parsed) || parsed is null)
                return false;
            if (_policy.AllowAddressInInvite)
                hints = found;
            var keepServers = _policy.AllowAnnounceServers
                || (_policy.AllowServersInDownloadLinks && parsed.AnnounceUrls.Count > 0);
            link = new MagnetLink(
                parsed.InfoHashes,
                parsed.Name,
                keepServers ? parsed.AnnounceUrls : null,
                null,
                parsed.Size);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string KeyOf(InfoHashes hashes) => hashes.V1OrV2.ToHex();

    private string InvitePath(TransferId id) => Path.Combine(_transfersFolder, id + ".invite");

    private string DownloadFilePath(TransferId id) => Path.Combine(_transfersFolder, id + ".torrent");

    private static void DeleteQuietly(string path)
    {
        try { File.Delete(path); }
        catch (Exception) { /* a leftover file in the engine folder is harmless */ }
    }

    private static void DeleteFolderQuietly(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception) { /* a leftover folder is harmless */ }
    }

    private sealed class Offer(ShareId id, string filePath)
    {
        public ShareId Id { get; } = id;
        public string FilePath { get; } = filePath;
        public CancellationTokenSource Cancel { get; } = new();
        public Task Work { get; set; } = Task.CompletedTask;
        public volatile TorrentManager? Manager;
        public volatile string? Invite;
    }

    private sealed class Incoming(TransferId id, TorrentManager manager, string folder, IReadOnlyList<IPEndPoint> hints)
    {
        public IReadOnlyList<IPEndPoint> Hints { get; } = hints;
        public long HintsTriedAt;
        public TransferId Id { get; } = id;
        public TorrentManager Manager { get; } = manager;
        public string Folder { get; } = folder;
        public string? Name { get; set; }
        public long? Size { get; set; }
        public volatile bool Finishing;
        public volatile bool Rejected;
        public volatile bool Paused;
    }
}
