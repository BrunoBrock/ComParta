using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using ComParta.Core;
using ComParta.Core.Ports;
using ComParta.Core.UseCases;
using MonoTorrent;
using MonoTorrent.PortForwarding;

namespace ComParta.Engine.MonoTorrent.Tests;

/// <summary>
/// What the engine reports about being reachable. Real engines on this machine, with the router
/// replaced by a fake so nothing outside the computer is contacted.
/// </summary>
public sealed class ReachabilityFactsTests : IAsyncLifetime
{
    private const string LinkOnly = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "comparta-tests", Guid.NewGuid().ToString("N"));
    private readonly List<MonoTorrentTransferEngine> _engines = [];
    private readonly List<TcpListener> _occupied = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var engine in _engines)
            await engine.StopAsync();
        foreach (var listener in _occupied)
            listener.Stop();
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

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>
    /// A router that answers every request the same way, but only once it has been started, as the real
    /// one does: asked without being started, it only takes note of the request.
    /// </summary>
    /// <param name="forgetsWhenUnregistered">False for a router that keeps showing a request after the engine withdrew it.</param>
    private sealed class FakeRouter(Func<Mapping, Mappings, Mappings> answer, bool forgetsWhenUnregistered = true) : IPortForwarder
    {
        private Mappings _mappings = Mappings.Empty;
        private int _calls;

        /// <summary>How often the engine asked this router anything at all.</summary>
        public int Calls => Volatile.Read(ref _calls);
        /// <summary>True once the engine has started it, which is what makes a real router get contacted.</summary>
        public bool EverStarted { get; private set; }
        public bool Active { get; private set; }
        public Mappings Mappings => _mappings;
        public event EventHandler? MappingsChanged;

        public Task StartAsync(CancellationToken token)
        {
            Interlocked.Increment(ref _calls);
            EverStarted = Active = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken token) => StopAsync(true, token);

        public Task StopAsync(bool removeExistingMappings, CancellationToken token)
        {
            Interlocked.Increment(ref _calls);
            Active = false;
            return Task.CompletedTask;
        }

        public Task RegisterMappingAsync(Mapping mapping)
        {
            Interlocked.Increment(ref _calls);
            _mappings = Active ? answer(mapping, _mappings) : _mappings.WithPending(mapping);
            MappingsChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task UnregisterMappingAsync(Mapping mapping, CancellationToken token)
        {
            Interlocked.Increment(ref _calls);
            if (forgetsWhenUnregistered)
                _mappings = _mappings.Remove(mapping, out _);
            return Task.CompletedTask;
        }
    }

    private const int RouterPublicPort = 43_210;

    private static readonly Func<Mapping, Mappings, Mappings> RouterAgrees =
        (mapping, current) => current.WithCreated(new Mapping(mapping.Protocol, mapping.PrivatePort, RouterPublicPort));

    private static readonly Func<Mapping, Mappings, Mappings> RouterNeverAnswers =
        (mapping, current) => current.WithPending(mapping);

    private static readonly Func<Mapping, Mappings, Mappings> RouterRefuses =
        (mapping, current) => current.WithFailed(mapping);

    // Answers neither yes nor no, and keeps no record of the request: no list ever holds the port.
    private static readonly Func<Mapping, Mappings, Mappings> RouterIgnores = (_, current) => current;

    private static Factories WithRouter(FakeRouter router) => Factories.Default.WithPortForwarderCreator(() => router);

    private async Task<MonoTorrentTransferEngine> StartAsync(
        string name, SharingPolicy policy, Factories? factories = null, TimeProvider? clock = null,
        Func<IPAddress, bool>? isOutside = null, int? port = null, Func<IEnumerable<IPAddress>>? addresses = null)
    {
        // The computer's own addresses are replaced too, so what a test finds does not depend on this
        // computer having an internet-wide IPv6 address. The engine itself does listen on IPv6.
        var engine = new MonoTorrentTransferEngine(
            new Paths(Path.Combine(_folder, name)), factories ?? Factories.Default, clock ?? TimeProvider.System, isOutside,
            addresses ?? (() => []));
        _engines.Add(engine);
        await engine.StartAsync(policy with { ListenPort = port ?? FreePort() });
        return engine;
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static SharingPolicy ClosedWithRouter => SharingPolicy.Closed with { AllowPortForwarding = true };

    // ---- What the engine observes ---------------------------------------------------------

    [Fact]
    public async Task Nothing_shared_or_received_means_not_listening_and_no_health()
    {
        var router = new FakeRouter(RouterAgrees);
        var engine = await StartAsync("idle", ClosedWithRouter, WithRouter(router));
        var connection = new ConnectionUseCases(engine);

        var facts = engine.ReadReachability();
        engine.ReportReachability();

        Assert.False(facts.Listening);
        Assert.False(facts.ListenPortBusy);
        Assert.Equal(PortMappingState.NotRequested, facts.PortMapping);
        Assert.Null(facts.ExternalPort);
        Assert.Null(facts.ExternalAddress);
        Assert.False(facts.ListeningOnIPv6);
        Assert.False(facts.IncomingConnectionSeenRecently);
        Assert.Null(connection.Current());
        Assert.Equal(0, router.Calls);
    }

    [Fact]
    public async Task A_link_only_receive_under_the_closed_policy_listens_and_asks_no_router()
    {
        var router = new FakeRouter(RouterAgrees);
        var engine = await StartAsync("receiver", SharingPolicy.Closed, WithRouter(router));
        var connection = new ConnectionUseCases(engine);

        Assert.True(await engine.ReceiveAsync(TransferId.New(), LinkOnly));
        engine.ReportReachability();

        var facts = engine.ReadReachability();
        Assert.True(facts.Listening);
        Assert.False(facts.ListenPortBusy);
        Assert.Equal(PortMappingState.NotRequested, facts.PortMapping);
        Assert.Null(facts.ExternalPort);
        Assert.Equal(Socket.OSSupportsIPv6, facts.ListeningOnIPv6);
        // The router is never started, so nothing can be sent to it, and nothing was opened.
        Assert.False(router.EverStarted);
        Assert.Empty(router.Mappings.Created);
        // Listening, but nothing asked the router and this computer has no internet-wide IPv6 address: nobody can get in.
        Assert.Equal(ConnectionHealth.Blocked, connection.Current());
    }

    [Fact]
    public async Task A_busy_listen_port_is_reported_when_something_is_received_and_core_says_blocked()
    {
        var busy = new TcpListener(IPAddress.Any, 0);
        busy.Start();
        _occupied.Add(busy);
        var busyPort = ((IPEndPoint)busy.LocalEndpoint).Port;
        if (Socket.OSSupportsIPv6)
        {
            // The engine listens on both internet protocols, so the same port is taken on the other one too.
            var busyV6 = new TcpListener(IPAddress.IPv6Any, busyPort) { Server = { DualMode = false } };
            busyV6.Start();
            _occupied.Add(busyV6);
        }
        var engine = await StartAsync("receiver", SharingPolicy.Closed, port: busyPort);
        var connection = new ConnectionUseCases(engine);

        Assert.True(await engine.ReceiveAsync(TransferId.New(), LinkOnly));
        engine.ReportReachability();

        var facts = engine.ReadReachability();
        Assert.True(facts.ListenPortBusy);
        Assert.False(facts.Listening);
        Assert.Equal(ConnectionHealth.Blocked, connection.Current());
    }

    [Fact]
    public async Task A_router_that_agreed_gives_an_obtained_mapping_with_its_public_port()
    {
        var router = new FakeRouter(RouterAgrees);
        var engine = await StartAsync("receiver", ClosedWithRouter, WithRouter(router));
        var connection = new ConnectionUseCases(engine);

        Assert.True(await engine.ReceiveAsync(TransferId.New(), LinkOnly));
        engine.ReportReachability();

        var facts = engine.ReadReachability();
        Assert.True(facts.Listening);
        Assert.Equal(PortMappingState.Obtained, facts.PortMapping);
        Assert.Equal(RouterPublicPort, facts.ExternalPort);
        Assert.Null(facts.ExternalAddress);
        Assert.True(router.Calls > 0);
        Assert.Equal(ConnectionHealth.Good, connection.Current());
    }

    [Fact]
    public async Task A_router_that_has_not_answered_gives_a_pending_mapping_and_no_health_yet()
    {
        var router = new FakeRouter(RouterNeverAnswers);
        var engine = await StartAsync("receiver", ClosedWithRouter, WithRouter(router));
        var connection = new ConnectionUseCases(engine);

        Assert.True(await engine.ReceiveAsync(TransferId.New(), LinkOnly));
        engine.ReportReachability();

        var facts = engine.ReadReachability();
        Assert.True(facts.Listening);
        Assert.Equal(PortMappingState.Pending, facts.PortMapping);
        Assert.Null(facts.ExternalPort);
        Assert.Null(connection.Current());
    }

    [Fact]
    public async Task A_router_that_refused_gives_a_refused_mapping_and_core_says_blocked()
    {
        var router = new FakeRouter(RouterRefuses);
        var engine = await StartAsync("receiver", ClosedWithRouter, WithRouter(router));
        var connection = new ConnectionUseCases(engine);

        Assert.True(await engine.ReceiveAsync(TransferId.New(), LinkOnly));
        engine.ReportReachability();

        var facts = engine.ReadReachability();
        Assert.Equal(PortMappingState.Refused, facts.PortMapping);
        Assert.Null(facts.ExternalPort);
        Assert.Equal(ConnectionHealth.Blocked, connection.Current());
    }

    // ---- A router that never answers ------------------------------------------------------------

    [Theory]
    [MemberData(nameof(SilentRouters))]
    public async Task A_silent_router_is_pending_for_thirty_seconds_and_refused_after(string name, Func<Mapping, Mappings, Mappings> answer)
    {
        var clock = new TestClock();
        var engine = await StartAsync(name, ClosedWithRouter, WithRouter(new FakeRouter(answer)), clock);
        var connection = new ConnectionUseCases(engine);
        Assert.True(await engine.ReceiveAsync(TransferId.New(), LinkOnly));

        Assert.Equal(PortMappingState.Pending, engine.ReadReachability().PortMapping);
        clock.Advance(TimeSpan.FromSeconds(29));
        engine.ReportReachability();
        Assert.Equal(PortMappingState.Pending, engine.ReadReachability().PortMapping);
        Assert.Null(connection.Current());

        clock.Advance(TimeSpan.FromSeconds(2));
        engine.ReportReachability();

        var facts = engine.ReadReachability();
        Assert.Equal(PortMappingState.Refused, facts.PortMapping);
        Assert.Null(facts.ExternalPort);
        Assert.Equal(ConnectionHealth.Blocked, connection.Current());
    }

    // A router that records the request as pending, and one that records nothing at all.
    public static TheoryData<string, Func<Mapping, Mappings, Mappings>> SilentRouters => new()
    {
        { "never-answers", RouterNeverAnswers },
        { "ignores-the-request", RouterIgnores },
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_wait_for_a_router_starts_over_after_the_engine_stops_listening(bool routerForgetsWithdrawnRequests)
    {
        var clock = new TestClock();
        var router = new FakeRouter(RouterNeverAnswers, routerForgetsWithdrawnRequests);
        var engine = await StartAsync("receiver", ClosedWithRouter, WithRouter(router), clock);
        var first = TransferId.New();
        Assert.True(await engine.ReceiveAsync(first, LinkOnly));
        Assert.Equal(PortMappingState.Pending, engine.ReadReachability().PortMapping);
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(PortMappingState.Pending, engine.ReadReachability().PortMapping);

        await engine.StopReceivingAsync(first);
        Assert.False(engine.ReadReachability().Listening);
        clock.Advance(TimeSpan.FromSeconds(20));

        // Forty seconds after the first request, but the wait only began again with this one.
        Assert.True(await engine.ReceiveAsync(TransferId.New(), LinkOnly));
        Assert.Equal(PortMappingState.Pending, engine.ReadReachability().PortMapping);
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(PortMappingState.Pending, engine.ReadReachability().PortMapping);
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(PortMappingState.Refused, engine.ReadReachability().PortMapping);
    }

    // ---- When the engine reports ----------------------------------------------------------

    [Fact]
    public async Task Facts_are_raised_only_when_they_change()
    {
        var engine = await StartAsync("receiver", SharingPolicy.Closed);
        var reported = new ConcurrentQueue<ReachabilityFacts>();
        engine.ReachabilityChanged += reported.Enqueue;

        engine.ReportReachability();
        engine.ReportReachability();
        Assert.Single(reported);
        Assert.False(reported.First().Listening);

        await engine.ReceiveAsync(TransferId.New(), LinkOnly);
        engine.ReportReachability();
        engine.ReportReachability();

        // The engine also reports by itself as each listener comes up, so there may be a step in between.
        Assert.True(reported.Count >= 2);
        Assert.True(reported.Last().Listening);
        Assert.All(reported.Zip(reported.Skip(1)), pair => Assert.NotEqual(pair.First, pair.Second));
        var settled = reported.Count;
        engine.ReportReachability();
        engine.ReportReachability();
        Assert.Equal(settled, reported.Count);
    }

    [Fact]
    public async Task The_engine_reports_by_itself_every_couple_of_seconds()
    {
        var engine = await StartAsync("receiver", SharingPolicy.Closed);
        var first = new TaskCompletionSource<ReachabilityFacts>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.ReachabilityChanged += facts => first.TrySetResult(facts);

        var facts = await first.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.False(facts.Listening);
        // Subscribed first: the engine reports the moment its listener comes up.
        var next = new TaskCompletionSource<ReachabilityFacts>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.ReachabilityChanged += changed =>
        {
            if (changed.Listening)
                next.TrySetResult(changed);
        };
        await engine.ReceiveAsync(TransferId.New(), LinkOnly);
        Assert.True((await next.Task.WaitAsync(TimeSpan.FromSeconds(15))).Listening);
    }

    // ---- Incoming connections from outside --------------------------------------------------

    [Theory]
    [InlineData("203.0.113.9", true)]
    [InlineData("8.8.4.4", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("192.168.1.20", false)]
    [InlineData("10.0.0.4", false)]
    [InlineData("100.64.3.3", false)]
    [InlineData("fe80::1", false)]
    public void Only_a_connection_from_outside_the_local_network_counts(string remote, bool counts)
    {
        var probe = new ReachabilityProbe(new TestClock());

        probe.NoteConnection(Direction.Incoming, new Uri($"ipv4://{(remote.Contains(':') ? $"[{remote}]" : remote)}:51413"));

        Assert.Equal(counts, probe.Read(SharingPolicy.Closed, null).IncomingConnectionSeenRecently);
    }

    [Fact]
    public void A_connection_we_made_ourselves_proves_nothing()
    {
        var probe = new ReachabilityProbe(new TestClock());

        probe.NoteConnection(Direction.Outgoing, new Uri("ipv4://203.0.113.9:51413"));
        probe.NoteConnection(Direction.Incoming, null);

        Assert.False(probe.Read(SharingPolicy.Closed, null).IncomingConnectionSeenRecently);
    }

    [Fact]
    public void An_incoming_connection_from_outside_counts_for_one_hour_and_not_after()
    {
        var clock = new TestClock();
        var probe = new ReachabilityProbe(clock);
        probe.NoteConnection(Direction.Incoming, new Uri("ipv4://203.0.113.9:51413"));

        Assert.True(probe.Read(SharingPolicy.Closed, null).IncomingConnectionSeenRecently);
        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.True(probe.Read(SharingPolicy.Closed, null).IncomingConnectionSeenRecently);
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False(probe.Read(SharingPolicy.Closed, null).IncomingConnectionSeenRecently);

        // A newer connection starts the hour again.
        probe.NoteConnection(Direction.Incoming, new Uri("ipv4://198.51.100.2:6000"));
        Assert.True(probe.Read(SharingPolicy.Closed, null).IncomingConnectionSeenRecently);
    }

    [Fact]
    public async Task A_device_that_connects_from_the_same_computer_is_not_proof_of_being_reachable()
    {
        var judged = new ConcurrentQueue<IPAddress>();
        var (sender, shareId) = await StartSenderAsync(new TestClock(), isOutside: address =>
        {
            judged.Enqueue(address);
            return NetworkAddresses.IsPublic(address);
        });
        await ConnectReceiverAsync(sender, shareId);

        // The sender did see the other device connect, from this computer's own address.
        await WaitUntilAsync(() => !judged.IsEmpty);
        sender.ReportReachability();

        Assert.All(judged, address => Assert.True(IPAddress.IsLoopback(address)));
        Assert.True(sender.ReadReachability().Listening);
        Assert.False(sender.ReadReachability().IncomingConnectionSeenRecently);
    }

    [Fact]
    public async Task An_incoming_connection_judged_to_be_from_outside_is_reported_for_one_hour_and_then_not()
    {
        var clock = new TestClock();
        var (sender, shareId) = await StartSenderAsync(clock, isOutside: _ => true);
        var connection = new ConnectionUseCases(sender);
        var reported = new ConcurrentQueue<ReachabilityFacts>();
        sender.ReachabilityChanged += reported.Enqueue;
        await ConnectReceiverAsync(sender, shareId);

        await WaitUntilAsync(() => sender.ReadReachability().IncomingConnectionSeenRecently);
        sender.ReportReachability();
        Assert.True(reported.Last().IncomingConnectionSeenRecently);
        Assert.Equal(ConnectionHealth.Good, connection.Current());

        clock.Advance(TimeSpan.FromMinutes(61));
        sender.ReportReachability();

        Assert.False(reported.Last().IncomingConnectionSeenRecently);
        Assert.Equal(ConnectionHealth.Blocked, connection.Current());
    }

    /// <summary>A device that offers a file, with its own idea of which addresses are outside the local network.</summary>
    private async Task<(MonoTorrentTransferEngine Sender, ShareId ShareId)> StartSenderAsync(
        TimeProvider clock, Func<IPAddress, bool>? isOutside)
    {
        var original = Path.Combine(_folder, "originals", "holiday.mov");
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        await File.WriteAllBytesAsync(original, RandomNumberGenerator.GetBytes(400_000));
        var sender = await StartAsync("sender", SharingPolicy.Closed, clock: clock, isOutside: isOutside);

        var ready = new TaskCompletionSource<OfferState>(TaskCreationOptions.RunContinuationsAsynchronously);
        sender.OfferChanged += (_, state) =>
        {
            if (state != OfferState.Preparing)
                ready.TrySetResult(state);
        };
        var shareId = ShareId.New();
        await sender.OfferAsync(shareId, original);
        Assert.Equal(OfferState.Ready, await ready.Task.WaitAsync(Timeout));
        return (sender, shareId);
    }

    /// <summary>Starts a second device that receives the shared file and connects straight to the sender.</summary>
    private async Task ConnectReceiverAsync(MonoTorrentTransferEngine sender, ShareId shareId)
    {
        var receiver = await StartAsync("receiver", SharingPolicy.Closed);
        var transferId = TransferId.New();
        Assert.True(await receiver.ReceiveAsync(transferId, (await sender.GetInviteAsync(shareId))!));
        await receiver.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting.");
            await Task.Delay(100);
        }
    }

    // ---- IPv6 ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Any_internet_wide_IPv6_address_counts_and_nothing_else_does(bool withGlobal)
    {
        string[] others = ["127.0.0.1", "::1", "fe80::1c2d:3e4f:5a6b:7c8d", "fd12:3456:789a::1", "192.168.1.5", "8.8.8.8"];
        var addresses = others.Select(IPAddress.Parse).ToList();
        if (withGlobal)
            addresses.Add(IPAddress.Parse("2a00:1450:4001:81b::200e"));

        Assert.Equal(withGlobal, ReachabilityProbe.HasGlobalIPv6(addresses));
    }

    [Fact]
    public void No_addresses_means_no_global_IPv6()
    {
        Assert.False(ReachabilityProbe.HasGlobalIPv6([]));
    }
}
