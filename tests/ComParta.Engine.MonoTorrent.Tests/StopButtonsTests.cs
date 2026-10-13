using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using ComParta.Core;
using ComParta.Core.Ports;
using MonoTorrent.PortForwarding;

namespace ComParta.Engine.MonoTorrent.Tests;

/// <summary>
/// Stop, per-file pause and Stop sharing with router port mapping switched on, as the app runs at
/// home. The router is a fake that never answers a request to remove a mapping, like a slow one:
/// none of the buttons may wait for it, and the mapping must stay while the app runs.
/// </summary>
public sealed class StopButtonsTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    /// <summary>A button, or quitting, that takes longer than this looks broken to the person.</summary>
    private static readonly TimeSpan Quick = TimeSpan.FromSeconds(8);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "comparta-tests", Guid.NewGuid().ToString("N"));
    private readonly List<MonoTorrentTransferEngine> _engines = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var engine in _engines)
            await engine.StopAsync().WaitAsync(Quick);
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

    /// <summary>Maps at once; never finishes removing a mapping.</summary>
    private sealed class SlowRouter : INatGateway
    {
        private int _unmaps;
        public string Key => "slow router";
        public int Unmaps => Volatile.Read(ref _unmaps);
        public Task<int> MapAsync(Protocol protocol, int privatePort, int publicPort, TimeSpan lifetime) => Task.FromResult(publicPort);

        public Task UnmapAsync(Protocol protocol, int privatePort, int publicPort)
        {
            Interlocked.Increment(ref _unmaps);
            return new TaskCompletionSource().Task;
        }

        public Task<IPAddress?> GetExternalAddressAsync() => Task.FromResult<IPAddress?>(IPAddress.Parse("203.0.113.9"));
    }

    private sealed class RouterOnTheNetwork(INatGateway router) : INatDiscovery
    {
        public event Action<INatGateway>? Found;
        public void Start() => Found?.Invoke(router);
        public void Stop() { }
    }

    private async Task<(MonoTorrentTransferEngine Engine, SlowRouter Router)> StartAsync()
    {
        var router = new SlowRouter();
        var engine = new MonoTorrentTransferEngine(new Paths(Path.Combine(_folder, "device")), new RouterOnTheNetwork(router));
        _engines.Add(engine);
        await engine.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort(), AllowPortForwarding = true });
        return (engine, router);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private string CreateFile(string name)
    {
        var folder = Path.Combine(_folder, "originals");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(200_000));
        return path;
    }

    private async Task<ShareId> OfferAsync(MonoTorrentTransferEngine engine, string name)
    {
        var id = ShareId.New();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.OfferChanged += (changed, state) =>
        {
            if (changed == id && state == OfferState.Ready)
                ready.TrySetResult();
        };
        await engine.OfferAsync(id, CreateFile(name));
        await ready.Task.WaitAsync(Timeout);
        // The listener is up and the router has mapped its port.
        for (var i = 0; i < 100 && engine.PortMapper!.Mappings.Created.Count == 0; i++)
            await Task.Delay(50);
        Assert.NotEmpty(engine.PortMapper!.Mappings.Created);
        return id;
    }

    [Fact]
    public async Task Stop_sharing_the_only_file_finishes_quickly_and_keeps_the_router_mapping()
    {
        var (engine, router) = await StartAsync();
        var id = await OfferAsync(engine, "only.bin");

        await engine.StopOfferingAsync(id).WaitAsync(Quick);

        Assert.Equal(0, router.Unmaps);
        Assert.NotEmpty(engine.PortMapper!.Mappings.Created);
        Assert.Null(await engine.GetInviteAsync(id));
    }

    [Fact]
    public async Task Stop_everything_finishes_quickly_and_Continue_sends_again()
    {
        var (engine, router) = await StartAsync();
        var id = await OfferAsync(engine, "stop.bin");

        await engine.SetStoppedAsync(sending: true, receiving: true).WaitAsync(Quick);
        Assert.Equal(global::MonoTorrent.Client.TorrentState.Stopped, engine.OfferStateOf(id));

        await engine.SetStoppedAsync(sending: false, receiving: false).WaitAsync(Quick);
        Assert.NotEqual(global::MonoTorrent.Client.TorrentState.Stopped, engine.OfferStateOf(id));
        Assert.Equal(0, router.Unmaps);
        Assert.NotEmpty(engine.PortMapper!.Mappings.Created);
    }

    [Fact]
    public async Task Pausing_one_shared_file_stops_only_that_file_and_survives_a_global_continue()
    {
        var (engine, _) = await StartAsync();
        var paused = await OfferAsync(engine, "paused.bin");
        var other = await OfferAsync(engine, "other.bin");

        await engine.PauseOfferingAsync(paused).WaitAsync(Quick);
        Assert.Equal(global::MonoTorrent.Client.TorrentState.Stopped, engine.OfferStateOf(paused));
        Assert.NotEqual(global::MonoTorrent.Client.TorrentState.Stopped, engine.OfferStateOf(other));

        await engine.SetStoppedAsync(sending: true, receiving: false).WaitAsync(Quick);
        await engine.SetStoppedAsync(sending: false, receiving: false).WaitAsync(Quick);
        Assert.Equal(global::MonoTorrent.Client.TorrentState.Stopped, engine.OfferStateOf(paused));

        await engine.ContinueOfferingAsync(paused).WaitAsync(Quick);
        Assert.NotEqual(global::MonoTorrent.Client.TorrentState.Stopped, engine.OfferStateOf(paused));
    }

    [Fact]
    public async Task Pausing_the_only_receiving_file_finishes_quickly()
    {
        var (engine, router) = await StartAsync();
        var id = TransferId.New();
        Assert.True(await engine.ReceiveAsync(id, "magnet:?xt=urn:btih:3123456789abcdef0123456789abcdef01234567"));

        await engine.SetStoppedAsync(sending: false, receiving: true).WaitAsync(Quick);
        await engine.SetStoppedAsync(sending: false, receiving: false).WaitAsync(Quick);
        await engine.StopReceivingAsync(id).WaitAsync(Quick);

        Assert.Equal(0, router.Unmaps);
    }
}
