using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using ComParta.Core;
using ComParta.Core.Ports;
using MonoTorrent.Connections.TrackerServer;
using MonoTorrent.TrackerServer;

namespace ComParta.Engine.MonoTorrent.Tests;

/// <summary>
/// Public trackers the person names when sharing: a real tracker runs on this machine, and two real
/// engines with every other way of finding each other closed meet only through it.
/// </summary>
public sealed class PublicServersTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "comparta-tests", Guid.NewGuid().ToString("N"));
    private readonly List<MonoTorrentTransferEngine> _engines = [];
    private readonly TrackerServer _tracker = new() { AllowUnregisteredTorrents = true };
    private ITrackerListener _listener = null!;
    private Uri _trackerUri = null!;

    public Task InitializeAsync()
    {
        var port = FreeUdpPort();
        _listener = TrackerListenerFactory.CreateUdp(IPAddress.Loopback, port);
        _tracker.RegisterListener(_listener);
        _listener.Start();
        _trackerUri = new Uri($"udp://127.0.0.1:{port}");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var engine in _engines)
            await engine.StopAsync();
        _listener.Stop();
        _tracker.Dispose();
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    private sealed record Paths(string Root) : IAppPaths
    {
        public string DatabasePath => Path.Combine(Root, "comparta.db");
        public string EngineRoot => Path.Combine(Root, "engine");
        public string ReceivedRoot => Path.Combine(Root, "Received");
        public string LogsRoot => Path.Combine(Root, "logs");
    }

    /// <summary>A device with local discovery, the public lookup network and address hints all off.</summary>
    private async Task<(MonoTorrentTransferEngine Engine, Paths Paths)> StartDeviceAsync(string name)
    {
        var paths = new Paths(Path.Combine(_folder, name));
        var engine = new MonoTorrentTransferEngine(paths);
        _engines.Add(engine);
        await engine.StartAsync(SharingPolicy.Closed with { ListenPort = FreeTcpPort(), AllowServersInDownloadLinks = true });
        return (engine, paths);
    }

    private static int FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static int FreeUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }

    private string CreateFile(string name, int sizeBytes)
    {
        var folder = Path.Combine(_folder, "originals");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(sizeBytes));
        return path;
    }

    private static async Task OfferAndWaitAsync(MonoTorrentTransferEngine engine, ShareId id, string path)
    {
        var ready = new TaskCompletionSource<OfferState>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.OfferChanged += (changed, state) =>
        {
            if (changed == id && state != OfferState.Preparing)
                ready.TrySetResult(state);
        };
        await engine.OfferAsync(id, path);
        Assert.Equal(OfferState.Ready, await ready.Task.WaitAsync(Timeout));
    }

    private async Task WaitUntilTheTrackerKnowsAsync(int devices)
    {
        var until = DateTime.UtcNow + Timeout;
        while (_tracker.GetTrackerItems().Sum(item => item.Count) < devices)
        {
            Assert.True(DateTime.UtcNow < until, $"The tracker never saw {devices} device(s).");
            await Task.Delay(100);
        }
    }

    private static string Checksum(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    [Fact]
    public async Task A_file_is_received_through_a_public_tracker_alone()
    {
        var original = CreateFile("trip.mp4", 2 * 1024 * 1024 + 11);
        var (sender, _) = await StartDeviceAsync("sender");
        var (receiver, receiverPaths) = await StartDeviceAsync("receiver");
        await sender.SetPublicServersAsync([_trackerUri]);
        var shareId = ShareId.New();
        await OfferAndWaitAsync(sender, shareId, original);
        await WaitUntilTheTrackerKnowsAsync(1);

        var invite = (await sender.GetInviteAsync(shareId))!;
        Assert.Contains("tr=" + Uri.EscapeDataString(_trackerUri.OriginalString), invite, StringComparison.Ordinal);
        Assert.DoesNotContain("x.pe=", invite, StringComparison.Ordinal);

        var done = new TaskCompletionSource<ReceiveProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.ReceiveProgressed += progress =>
        {
            if (progress.Done || progress.Failed)
                done.TrySetResult(progress);
        };
        Assert.True(await receiver.ReceiveAsync(TransferId.New(), invite));

        // Nobody connects them by hand: the receiver learns where the sender is from the tracker.
        var result = await done.Task.WaitAsync(Timeout);
        Assert.True(result.Done);
        Assert.Equal(Checksum(original), Checksum(Path.Combine(receiverPaths.ReceivedRoot, "trip.mp4")));
    }

    [Fact]
    public async Task Trackers_named_later_reach_files_already_shared_and_can_be_taken_away()
    {
        var (sender, _) = await StartDeviceAsync("sender");
        var shareId = ShareId.New();
        await OfferAndWaitAsync(sender, shareId, CreateFile("song.mp3", 300_000));
        Assert.Empty(sender.PublicServersOf(shareId));
        Assert.DoesNotContain("tr=", (await sender.GetInviteAsync(shareId))!, StringComparison.Ordinal);

        await sender.SetPublicServersAsync([_trackerUri]);

        Assert.Equal([_trackerUri], sender.PublicServersOf(shareId));
        await WaitUntilTheTrackerKnowsAsync(1);
        Assert.Contains("tr=", (await sender.GetInviteAsync(shareId))!, StringComparison.Ordinal);

        await sender.SetPublicServersAsync([]);

        Assert.Empty(sender.PublicServersOf(shareId));
        Assert.DoesNotContain("tr=", (await sender.GetInviteAsync(shareId))!, StringComparison.Ordinal);
    }
}
