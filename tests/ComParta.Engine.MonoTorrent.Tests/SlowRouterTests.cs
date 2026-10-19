using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using ComParta.Core;
using ComParta.Core.Ports;
using MonoTorrent.PortForwarding;

namespace ComParta.Engine.MonoTorrent.Tests;

/// <summary>
/// A router that answers the search but never answers a request to open a port (seen on real home
/// networks, and on other devices that answer the same search) must not hold up sharing.
/// </summary>
public sealed class SlowRouterTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    // Long enough that a share waiting on the router would miss ReadyWithin; the stop at the end
    // still finishes, because each request gives up after it.
    private static readonly TimeSpan RouterTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReadyWithin = TimeSpan.FromSeconds(5);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "comparta-tests", Guid.NewGuid().ToString("N"));
    private MonoTorrentTransferEngine? _engine;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_engine is { } engine)
            await engine.StopAsync().WaitAsync(Timeout);
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    private sealed record Paths(string Root) : IAppPaths
    {
        public string DatabasePath => Path.Combine(Root, "comparta.db");
        public string EngineRoot => Path.Combine(Root, "engine");
        public string ReceivedRoot => Path.Combine(Root, "Received");
        public string LogsRoot => Path.Combine(Root, "logs");
        public string SavedRoot => Path.Combine(Root, "Downloads");
    }

    /// <summary>Found by the search; every request to it waits forever.</summary>
    private sealed class SilentRouter : INatGateway
    {
        public string Key => "silent";
        public int MapRequests;

        public Task<int> MapAsync(Protocol protocol, int privatePort, int publicPort, TimeSpan lifetime)
        {
            Interlocked.Increment(ref MapRequests);
            return new TaskCompletionSource<int>().Task;
        }

        public Task UnmapAsync(Protocol protocol, int privatePort, int publicPort) => new TaskCompletionSource().Task;

        public Task<IPAddress?> GetExternalAddressAsync() => new TaskCompletionSource<IPAddress?>().Task;
    }

    private sealed class FindsRouterAtOnce(INatGateway router) : INatDiscovery
    {
        public event Action<INatGateway>? Found;
        public void Start() => Found?.Invoke(router);
        public void Stop() { }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [Fact]
    public async Task A_file_is_ready_to_send_while_the_router_never_answers()
    {
        var router = new SilentRouter();
        var engine = _engine = new MonoTorrentTransferEngine(new Paths(_folder), new FindsRouterAtOnce(router))
        {
            RouterTimeout = RouterTimeout,
        };
        await engine.StartAsync(SharingPolicy.Closed with { AllowPortForwarding = true, ListenPort = FreePort() });

        var folder = Path.Combine(_folder, "Downloads");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "mountain-landscape-ponta-delgada-island-azores.webp");
        File.WriteAllBytes(file, RandomNumberGenerator.GetBytes(345_678));

        var id = ShareId.New();
        var outcome = new TaskCompletionSource<OfferState>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.OfferChanged += (changed, state) =>
        {
            if (changed == id && state != OfferState.Preparing)
                outcome.TrySetResult(state);
        };
        await engine.OfferAsync(id, file);

        Assert.Equal(OfferState.Ready, await outcome.Task.WaitAsync(ReadyWithin));
        Assert.NotNull(await engine.GetInviteAsync(id));
        // The router was still asked; it just did not hold anything up.
        Assert.True(router.MapRequests > 0);
    }
}
