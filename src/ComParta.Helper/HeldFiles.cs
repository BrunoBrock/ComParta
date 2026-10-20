using System.Net;
using System.Net.Sockets;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.TrackerServer;

namespace ComParta.Helper;

/// <summary>What happened to a file a ComParta install asked the helper to hold.</summary>
public enum AddResult
{
    Added,
    AlreadyHeld,
    /// <summary>Not a description of one plain file.</summary>
    Invalid,
    /// <summary>Bigger than one file or all files together may be.</summary>
    TooLarge,
}

/// <summary>The answer to adding a file: the id that removes it again.</summary>
public sealed record AddedFile(string Id);

/// <summary>What a ComParta needs to name the helper in its links: its public servers and where it serves files.</summary>
public sealed record HelperAbout(string[] Trackers, string Peer);

/// <summary>One file the helper holds, as the control port lists it.</summary>
public sealed record HeldFile(string Id, string Name, long Size, double Percent, DateTime AddedUtc);

/// <summary>
/// The files the helper holds (AD-23): each is a torrent the helper's engine downloads from the
/// ComParta that connects out to it, then serves to anyone, announced only on the helper's own
/// public server. Within <see cref="HelperOptions.MaxTotalBytes"/>, the oldest go first.
/// </summary>
internal sealed class HeldFiles : IAsyncDisposable
{
    private readonly HelperOptions _options;
    private readonly TrackerServer _tracker;
    private readonly Uri _ownTracker;
    private readonly string _filesFolder;
    private readonly string _descriptionsFolder;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<Held> _held = [];
    private ClientEngine? _engine;

    public HeldFiles(HelperOptions options, TrackerServer tracker, string dataFolder)
    {
        _options = options;
        _tracker = tracker;
        // The helper announces itself on its own server from this computer, naming its public address.
        // Over the same internet protocol as that address, so the engine sends it along.
        var loopback = options.Address.AddressFamily == AddressFamily.InterNetworkV6 ? "[::1]" : "127.0.0.1";
        _ownTracker = new Uri($"http://{loopback}:{options.TrackerPort}/announce");
        _filesFolder = Path.Combine(dataFolder, "files");
        _descriptionsFolder = Path.Combine(dataFolder, "descriptions");
        CacheFolder = Path.Combine(dataFolder, "engine");
    }

    private string CacheFolder { get; }

    public async Task StartAsync()
    {
        Directory.CreateDirectory(_filesFolder);
        Directory.CreateDirectory(_descriptionsFolder);
        Directory.CreateDirectory(CacheFolder);
        _engine = new ClientEngine(EngineSettings());

        // Files held before a restart come back, oldest first, so the oldest still go first.
        var saved = new DirectoryInfo(_descriptionsFolder).GetFiles("*.torrent").OrderBy(f => f.LastWriteTimeUtc);
        foreach (var file in saved)
        {
            if (Torrent.TryLoad(await File.ReadAllBytesAsync(file.FullName), out var torrent) && IsPlainFile(torrent))
                await HoldAsync(torrent, file.LastWriteTimeUtc);
            else
                file.Delete();
        }
    }

    private EngineSettings EngineSettings()
    {
        var address = _options.Address;
        var family = address.AddressFamily == AddressFamily.InterNetworkV6 ? "ipv6" : "ipv4";
        var listen = new Dictionary<string, IPEndPoint> { ["ipv4"] = new(IPAddress.Any, _options.PeerPort) };
        if (Socket.OSSupportsIPv6)
            listen["ipv6"] = new IPEndPoint(IPAddress.IPv6Any, _options.PeerPort);
        return new EngineSettingsBuilder
        {
            // Nothing but the helper's own public server: no lookup network, no home network search,
            // no router requests. The computer is reachable already.
            AllowLocalPeerDiscovery = false,
            AllowPortForwarding = false,
            DhtEndPoint = null,
            AutoSaveLoadFastResume = true,
            CacheDirectory = CacheFolder,
            ListenEndPoints = listen,
            ReportedListenEndPoints = new Dictionary<string, IPEndPoint> { [family] = new(address, _options.PeerPort) },
        }.ToSettings();
    }

    /// <summary>Holds the file <paramref name="description"/> describes, making room by dropping the oldest.</summary>
    public async Task<(AddResult Result, string? Id)> AddAsync(byte[] description)
    {
        if (!Torrent.TryLoad(description, out var torrent) || !IsPlainFile(torrent))
            return (AddResult.Invalid, null);
        var id = IdOf(torrent.InfoHashes);
        if (torrent.Size > _options.MaxFileBytes || torrent.Size > _options.MaxTotalBytes)
            return (AddResult.TooLarge, id);

        await _gate.WaitAsync();
        try
        {
            if (_held.Any(h => h.Id == id))
                return (AddResult.AlreadyHeld, id);
            while (_held.Sum(h => h.Torrent.Size) + torrent.Size > _options.MaxTotalBytes)
                await DropAsync(_held[0]);
            await File.WriteAllBytesAsync(DescriptionPath(id), description);
            await HoldAsync(torrent, DateTime.UtcNow);
            // Answered once the helper is listed on its own server, so the sharing computer, which
            // announces right after, learns where the helper is at once instead of at its next turn.
            await WaitUntilListedAsync(torrent.InfoHashes, TimeSpan.FromSeconds(10));
            return (AddResult.Added, id);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stops serving a file and deletes the helper's copy, as when its share stops.</summary>
    public async Task<bool> RemoveAsync(string id)
    {
        await _gate.WaitAsync();
        try
        {
            if (_held.FirstOrDefault(h => h.Id == id) is not { } held)
                return false;
            await DropAsync(held);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<HeldFile> List()
    {
        lock (_held)
            return [.. _held.Select(h => new HeldFile(h.Id, h.Torrent.Name, h.Torrent.Size, h.Manager.Progress, h.AddedUtc))];
    }

    private async Task HoldAsync(Torrent torrent, DateTime addedUtc)
    {
        var engine = _engine!;
        var id = IdOf(torrent.InfoHashes);
        var settings = new TorrentSettingsBuilder { AllowDht = false, AllowPeerExchange = false }.ToSettings();
        var manager = await engine.AddAsync(torrent, Path.Combine(_filesFolder, id), settings);
        // Only the helper's own server: a description may name others, and the helper does not announce there.
        foreach (var tracker in manager.TrackerManager.Tiers.SelectMany(t => t.Trackers).ToList())
            await manager.TrackerManager.RemoveTrackerAsync(tracker);
        await manager.TrackerManager.AddTrackerAsync(_ownTracker);
        _tracker.Add(new InfoHashTrackable(torrent));
        lock (_held)
            _held.Add(new Held(id, torrent, manager, addedUtc));
        await manager.StartAsync();
    }

    private async Task WaitUntilListedAsync(InfoHashes hashes, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until
            && !_tracker.GetTrackerItems().Any(item => item.Trackable.InfoHash == hashes.V1OrV2 && item.Count > 0))
            await Task.Delay(50);
    }

    private async Task DropAsync(Held held)
    {
        lock (_held)
            _held.Remove(held);
        // The description goes first, so a file that could not be deleted does not come back after a restart.
        DeleteQuietly(DescriptionPath(held.Id));
        _tracker.Remove(new InfoHashTrackable(held.Torrent));
        try
        {
            await held.Manager.StopAsync(TimeSpan.FromSeconds(5));
            await _engine!.RemoveAsync(held.Manager, RemoveMode.CacheDataAndDownloadedData);
        }
        catch (Exception) { /* the folders below go either way */ }
        try { Directory.Delete(Path.Combine(_filesFolder, held.Id), recursive: true); }
        catch (Exception) { /* nothing was written yet, or it is gone */ }
    }

    /// <summary>
    /// ComParta shares one file at a time, so the helper takes exactly that: one file whose name is a
    /// plain name, never a path that could reach outside the helper's folder.
    /// </summary>
    internal static bool IsPlainFile(Torrent torrent) =>
        torrent.Files.Count == 1
        && torrent.Size > 0
        && IsPlainName(torrent.Name)
        && torrent.Files[0].Path == torrent.Name;

    internal static bool IsPlainName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= 255
        && name is not ("." or "..")
        && !name.EndsWith('.') && !name.EndsWith(' ')
        && name.All(c => c >= 32 && !"<>:\"/\\|?*".Contains(c));

    internal static string IdOf(InfoHashes hashes) => hashes.V1OrV2.ToHex().ToLowerInvariant();

    private string DescriptionPath(string id) => Path.Combine(_descriptionsFolder, id + ".torrent");

    private static void DeleteQuietly(string path)
    {
        try { File.Delete(path); }
        catch (Exception) { /* a leftover copy only takes space; its description is gone, so it is not served */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (_engine is { } engine)
        {
            _engine = null;
            try { await engine.StopAllAsync(); }
            catch (Exception) { /* shutting down anyway */ }
            engine.Dispose();
        }
    }

    private sealed record Held(string Id, Torrent Torrent, TorrentManager Manager, DateTime AddedUtc);
}
