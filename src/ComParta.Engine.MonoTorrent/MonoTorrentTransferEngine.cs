using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ComParta.Core;
using ComParta.Core.Ports;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Connections;
using MonoTorrent.Connections.Peer;
using MonoTorrent.PieceWriter;

[assembly: InternalsVisibleTo("ComParta.Engine.MonoTorrent.Tests")]
[assembly: InternalsVisibleTo("ComParta.Wiring.Tests")]
[assembly: InternalsVisibleTo("ComParta.Helper.Tests")]

namespace ComParta.Engine.MonoTorrent;

/// <summary>
/// <see cref="ITransferEngine"/> on MonoTorrent. Everything torrent-shaped stays in this project (AD-2):
/// a share is a single-file torrent seeded in place, an invite is its magnet link, and a transfer
/// is a download of that magnet link, with the trackers the person named, if any. A "download link" is any other magnet link and a "download
/// file" is a .torrent file (AD-21); for those the trackers they name are kept (AD-8, amended).
/// The class is split by area: Offering, Receiving, Activity and Reachability each have their own file.
/// </summary>
public sealed partial class MonoTorrentTransferEngine : ITransferEngine, IAsyncDisposable
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan HintRetryInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ReachabilityInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ActivityInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan QuitWait = TimeSpan.FromSeconds(5);

    // The magnet-link parameter for "a peer address to try directly" (BEP 9).
    private const string AddressHint = "x.pe";
    private const int MaxAddressHints = 6;

    /// <summary>What the shell's file dialog shows for "open a download file".</summary>
    public static IReadOnlyList<string> DownloadFileExtensions { get; } = [".torrent"];

    private readonly string _sharesFolder;
    private readonly string _transfersFolder;
    private readonly string _cacheFolder;
    private readonly string _usualReceivedRoot;
    private readonly ConcurrentDictionary<ShareId, Offer> _offers = new();
    private readonly ConcurrentDictionary<TransferId, Incoming> _incoming = new();
    private readonly INatDiscovery? _natDiscovery;
    private readonly Factories _baseFactories;
    private readonly ReachabilityProbe _probe;
    private readonly object _reachabilityGate = new();
    private ReachabilityFacts? _lastReachability;
    private ClientEngine? _engine;
    private PortMapper? _portMapper;
    private SharingPolicy _policy = SharingPolicy.Closed;
    // Trackers the person named for what they share (AD-8); kept across a restart of the engine.
    private volatile IReadOnlyList<Uri> _publicServers = [];
    private Timer? _progressTimer;
    private Timer? _reachabilityTimer;
    private Timer? _activityTimer;
    private volatile bool _sendingStopped;
    private volatile bool _receivingStopped;
    private int _reporting;

    public MonoTorrentTransferEngine(IAppPaths paths) : this(paths, new MonoNatDiscovery())
    {
    }

    internal MonoTorrentTransferEngine(IAppPaths paths, INatDiscovery natDiscovery)
        : this(paths, natDiscovery, Factories.Default, TimeProvider.System)
    {
    }

    /// <summary>For tests: builds the engine from <paramref name="factories"/>, so a fake router can stand in for the real one.</summary>
    /// <param name="factories">What the engine builds its parts from. Its port forwarder is used as it is.</param>
    /// <param name="clock">Times the one-hour window of an incoming connection from outside.</param>
    /// <param name="isOutsideLocalNetwork">Which addresses count as outside the local network; the usual rule when null.</param>
    /// <param name="localAddresses">The addresses of this computer; the ones of the network connections that are up when null.</param>
    internal MonoTorrentTransferEngine(
        IAppPaths paths, Factories factories, TimeProvider clock, Func<IPAddress, bool>? isOutsideLocalNetwork = null,
        Func<IEnumerable<IPAddress>>? localAddresses = null)
        : this(paths, null, factories, clock, isOutsideLocalNetwork, localAddresses)
    {
    }

    private MonoTorrentTransferEngine(
        IAppPaths paths, INatDiscovery? natDiscovery, Factories factories, TimeProvider clock,
        Func<IPAddress, bool>? isOutsideLocalNetwork = null, Func<IEnumerable<IPAddress>>? localAddresses = null)
    {
        _natDiscovery = natDiscovery;
        _baseFactories = factories;
        _probe = new ReachabilityProbe(clock, isOutsideLocalNetwork, localAddresses);
        _sharesFolder = Path.Combine(paths.EngineRoot, "shares");
        _transfersFolder = Path.Combine(paths.EngineRoot, "transfers");
        _cacheFolder = Path.Combine(paths.EngineRoot, "cache");
        _usualReceivedRoot = paths.ReceivedRoot;
    }

    public event Action<ShareId, OfferState>? OfferChanged;
    public event Action<ReceiveProgress>? ReceiveProgressed;
    public event Action<ReachabilityFacts>? ReachabilityChanged;
    public event Action? ActivityChanged;

    /// <summary>The endpoint this engine was told to accept connections on. For tests.</summary>
    internal IPEndPoint? ListenEndPoint => _engine?.Settings.ListenEndPoints.Values.FirstOrDefault();

    /// <summary>For tests: false makes this device refuse every incoming connection.</summary>
    internal bool AcceptIncoming { get; init; } = true;

    /// <summary>
    /// Reads what is shared (AD-20); files on this computer's disks unless the shell gives another,
    /// such as a phone's picked items.
    /// </summary>
    public IContentSource Content { get; init; } = new FileContent();

    // Where the engine thinks each shared file is, and the content reference it is really read from.
    private readonly ConcurrentDictionary<string, string> _sharedFiles = new(StringComparer.Ordinal);

    /// <summary>For tests: wraps the disk writer, for example to stand for a full disk.</summary>
    internal Func<IPieceWriter, IPieceWriter> WrapWriter { get; init; } = writer => writer;

    /// <summary>For tests: how long one request to the router may take.</summary>
    internal TimeSpan RouterTimeout { get; init; } = PortMapper.DefaultRouterTimeout;

    /// <summary>For tests: the router port mapping this engine uses.</summary>
    internal PortMapper? PortMapper => _portMapper;

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
        var factories = _baseFactories;
        _portMapper = null;
        if (_natDiscovery is { } discovery)
        {
            var mapper = _portMapper = new PortMapper(discovery, _policy.AllowPortForwarding, RouterTimeout);
            mapper.MappingsChanged += (_, _) => ReportReachability();
            factories = factories.WithPortForwarderCreator(() => mapper.ForEngine);
        }
        factories = factories
            .WithPieceWriterCreator(maxOpenFiles => ContentWriter(maxOpenFiles))
            // Every listener the engine creates is remembered so its status can be read (AD-15).
            .WithPeerConnectionListenerCreator(endPoint =>
            {
                var listener = _baseFactories.CreatePeerConnectionListener(endPoint);
                listener.StatusChanged += (_, _) => ReportReachability();
                return _probe.Track(endPoint, listener);
            });
        _engine = new ClientEngine(WithLimits(ToEngineSettings(_policy, _cacheFolder, AcceptIncoming)), factories);
        _progressTimer = new Timer(_ => ReportProgress(), null, ProgressInterval, ProgressInterval);
        _reachabilityTimer = new Timer(_ => ReportReachability(), null, ReachabilityInterval, ReachabilityInterval);
        _activityTimer = new Timer(_ => _ = TickActivityAsync(), null, ActivityInterval, ActivityInterval);
        StartHelperTimer();
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        return Task.CompletedTask;
    }

    /// <summary>The engine's disk, with shared files read through <see cref="Content"/> (AD-20).</summary>
    private IPieceWriter ContentWriter(int maxOpenFiles) =>
        new ContentPieceWriter(WrapWriter(_baseFactories.CreatePieceWriter(maxOpenFiles)), Content, _sharedFiles);

    /// <summary>The only place network options are set, and only from the policy (AD-8).</summary>
    /// <param name="acceptIncoming">False only in tests, to stand for a computer nobody can reach.</param>
    internal static EngineSettings ToEngineSettings(SharingPolicy policy, string cacheFolder, bool acceptIncoming = true)
    {
        // Incoming connections are accepted on both internet protocols (AD-15).
        var listen = new Dictionary<string, IPEndPoint>();
        if (acceptIncoming)
        {
            listen["ipv4"] = new IPEndPoint(IPAddress.Any, policy.ListenPort);
            if (Socket.OSSupportsIPv6)
                listen["ipv6"] = new IPEndPoint(IPAddress.IPv6Any, policy.ListenPort);
        }
        var builder = new EngineSettingsBuilder
        {
            AllowLocalPeerDiscovery = policy.AllowLocalDiscovery,
            AllowPortForwarding = policy.AllowPortForwarding,
            DhtEndPoint = policy.AllowPublicLookup ? new IPEndPoint(IPAddress.Any, policy.ListenPort) : null,
            AutoSaveLoadDhtCache = policy.AllowPublicLookup,
            AutoSaveLoadFastResume = true,
            AutoSaveLoadMagnetLinkMetadata = true,
            CacheDirectory = cacheFolder,
            // No write cache: on a full disk a cached part could count as written when it never was.
            DiskCacheBytes = 0,
            ListenEndPoints = listen,
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
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
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
        if (_activityTimer is { } activityTimer)
        {
            _activityTimer = null;
            await activityTimer.DisposeAsync();
        }
        await StopHelperTimerAsync();
        foreach (var offer in _offers.Values)
            offer.Cancel.Cancel();
        if (_engine is { } engine)
        {
            _engine = null;
            try { await engine.StopAllAsync(); }
            catch (Exception) { /* shutting down anyway */ }
            engine.Dispose();
        }
        if (_portMapper is { } portMapper)
        {
            // Takes the router mapping away again, so nothing stays open after the app quits.
            // A router that does not answer must not keep the app from quitting.
            var unmapped = portMapper.StopAsync(removeExisting: true, CancellationToken.None);
            try
            {
                await unmapped.WaitAsync(QuitWait);
                portMapper.Dispose();
            }
            catch (Exception) { /* the router lets it expire */ }
            _portMapper = null;
        }
        _probe.Reset();
        _offers.Clear();
        _incoming.Clear();
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    // ---- Helpers --------------------------------------------------------------------------

    /// <summary>
    /// Reads an invite and drops everything except the content identity, name and size, so an invite
    /// can never make the app contact a server the policy did not allow. A link that names trackers
    /// keeps them when the policy allows servers in download links (AD-8, amended): a public download
    /// link, or a ComParta invite whose sender named public servers.
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
                    && IsUsableHint(endPoint))
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

    /// <summary>An address worth trying: either internet protocol, a real port, never a group or "any" address.</summary>
    private static bool IsUsableHint(IPEndPoint endPoint) =>
        endPoint.Port > 0
        && endPoint.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6
        && !endPoint.Address.Equals(IPAddress.Any) && !endPoint.Address.Equals(IPAddress.IPv6Any)
        && !endPoint.Address.Equals(IPAddress.Broadcast) && !endPoint.Address.IsIPv6Multicast
        && !(endPoint.AddressFamily == AddressFamily.InterNetwork && endPoint.Address.GetAddressBytes()[0] is >= 224 and <= 239);

    // A magnet link ends at the first blank, quote or bracket of the message around it.
    [GeneratedRegex(@"magnet:\?[^\s""'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex MagnetInText();

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

    private sealed class Offer(ShareId id, string filePath, string? name = null, IReadOnlyList<ShareItem>? items = null)
    {
        public ShareId Id { get; } = id;
        public string FilePath { get; } = filePath;
        /// <summary>The name several files are offered under; null for one file offered as itself.</summary>
        public string? Name { get; } = name;
        /// <summary>Several files offered as one (AD-19); null for one file.</summary>
        public IReadOnlyList<ShareItem>? Items { get; } = items;
        public CancellationTokenSource Cancel { get; } = new();
        public Task Work { get; set; } = Task.CompletedTask;
        public volatile TorrentManager? Manager;
        public volatile string? Invite;
        /// <summary>The helper computer holds this file (AD-23).</summary>
        public volatile bool HeldByHelper;
        /// <summary>The person paused this file on its own; a global continue leaves it paused.</summary>
        public volatile bool Paused;
        /// <summary>Devices getting the file now, and their average percentage; refreshed each activity tick.</summary>
        public volatile ReceiverCount Receivers = ReceiverCount.None;
        /// <summary>Devices that got all of the file from this one, by their session id.</summary>
        public readonly HashSet<string> Finished = [];
        /// <summary>Where the engine thinks this share's files are, each read through the content source.</summary>
        public readonly List<string> SharedPaths = [];
    }

    private sealed record ReceiverCount(int Count, int Percent)
    {
        public static ReceiverCount None { get; } = new(0, 0);
    }

    /// <param name="root">The Received root the transfer goes to (AD-6).</param>
    /// <param name="folder">Its unfinished data, under <paramref name="root"/>.</param>
    private sealed class Incoming(TransferId id, TorrentManager manager, string root, string folder, IReadOnlyList<IPEndPoint> hints)
    {
        public string Root { get; } = root;
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
