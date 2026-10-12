using System.Net;
using MonoTorrent.PortForwarding;

namespace ComParta.Engine.MonoTorrent.Tests;

/// <summary>Router port mapping against a fake router: request, renewal, removal and failure.</summary>
public sealed class PortMapperTests
{
    private static readonly Mapping Tcp = new(Protocol.Tcp, 41_000);
    private static readonly Mapping Udp = new(Protocol.Udp, 41_000);

    private sealed class FakeRouter(string key = "router") : INatGateway
    {
        public string Key { get; } = key;
        public List<string> Calls { get; } = [];
        public bool Refuse { get; set; }
        public int PublicPortOffset { get; set; }
        public IPAddress? External { get; set; } = IPAddress.Parse("203.0.113.7");

        public Task<int> MapAsync(Protocol protocol, int privatePort, int publicPort, TimeSpan lifetime)
        {
            Calls.Add($"map {protocol} {privatePort}->{publicPort} {lifetime.TotalHours}h");
            if (Refuse)
                throw new InvalidOperationException("refused");
            return Task.FromResult(publicPort + PublicPortOffset);
        }

        public Task UnmapAsync(Protocol protocol, int privatePort, int publicPort)
        {
            Calls.Add($"unmap {protocol} {privatePort}->{publicPort}");
            return Task.CompletedTask;
        }

        public Task<IPAddress?> GetExternalAddressAsync() => Task.FromResult(External);
    }

    private sealed class FakeDiscovery : INatDiscovery
    {
        public event Action<INatGateway>? Found;
        public bool Searching { get; private set; }
        public void Start() => Searching = true;
        public void Stop() => Searching = false;
        public void Announce(INatGateway gateway) => Found?.Invoke(gateway);
    }

    private static async Task<(PortMapper Mapper, FakeDiscovery Discovery)> StartAsync(bool enabled = true)
    {
        var discovery = new FakeDiscovery();
        var mapper = new PortMapper(discovery, enabled);
        await mapper.StartAsync(CancellationToken.None);
        return (mapper, discovery);
    }

    private static async Task SettleAsync(Func<bool> done)
    {
        for (var i = 0; i < 100 && !done(); i++)
            await Task.Delay(20);
    }

    [Fact]
    public async Task Registered_ports_are_mapped_on_a_router_found_later_and_the_address_is_learnt()
    {
        var (mapper, discovery) = await StartAsync();
        await mapper.RegisterMappingAsync(Tcp);
        await mapper.RegisterMappingAsync(Udp);
        Assert.Equal(2, mapper.Mappings.Pending.Count);
        var router = new FakeRouter { PublicPortOffset = 5 };

        discovery.Announce(router);
        await SettleAsync(() => mapper.Mappings.Created.Count == 2);

        Assert.True(discovery.Searching);
        Assert.Equal(2, mapper.Mappings.Created.Count);
        Assert.Equal(41_005, mapper.MappedPort(41_000));
        Assert.Equal(IPAddress.Parse("203.0.113.7"), mapper.ExternalAddress);
        Assert.Contains("map Tcp 41000->41000 2h", router.Calls);
    }

    [Fact]
    public async Task A_router_found_twice_is_asked_once()
    {
        var (mapper, discovery) = await StartAsync();
        var router = new FakeRouter();
        discovery.Announce(router);
        discovery.Announce(new FakeRouter());

        await mapper.RegisterMappingAsync(Tcp);

        Assert.Single(router.Calls);
        Assert.Equal(41_000, mapper.MappedPort(41_000));
    }

    [Fact]
    public async Task Mappings_are_renewed_with_the_same_public_port()
    {
        var (mapper, discovery) = await StartAsync();
        var router = new FakeRouter { PublicPortOffset = 1 };
        discovery.Announce(router);
        await mapper.RegisterMappingAsync(Tcp);

        await mapper.RenewAsync();

        Assert.Equal(["map Tcp 41000->41000 2h", "map Tcp 41000->41001 2h"], router.Calls);
        Assert.Equal(41_001, mapper.MappedPort(41_000));
    }

    [Fact]
    public async Task A_renewal_the_router_refuses_marks_the_mapping_failed()
    {
        var (mapper, discovery) = await StartAsync();
        var router = new FakeRouter();
        discovery.Announce(router);
        await mapper.RegisterMappingAsync(Tcp);
        router.Refuse = true;

        await mapper.RenewAsync();

        Assert.Null(mapper.MappedPort(41_000));
        Assert.Single(mapper.Mappings.Failed);
    }

    [Fact]
    public async Task Stopping_removes_every_mapping_from_the_router()
    {
        var (mapper, discovery) = await StartAsync();
        var router = new FakeRouter();
        discovery.Announce(router);
        await mapper.RegisterMappingAsync(Tcp);
        await mapper.RegisterMappingAsync(Udp);
        var changes = 0;
        mapper.MappingsChanged += (_, _) => changes++;

        await mapper.StopAsync(CancellationToken.None);

        Assert.False(mapper.Active);
        Assert.False(discovery.Searching);
        Assert.Contains("unmap Tcp 41000->41000", router.Calls);
        Assert.Contains("unmap Udp 41000->41000", router.Calls);
        Assert.Null(mapper.MappedPort(41_000));
        Assert.True(changes > 0);
    }

    [Fact]
    public async Task Unregistering_removes_that_mapping_only()
    {
        var (mapper, discovery) = await StartAsync();
        var router = new FakeRouter();
        discovery.Announce(router);
        await mapper.RegisterMappingAsync(Tcp);
        await mapper.RegisterMappingAsync(Udp);

        await mapper.UnregisterMappingAsync(Udp, CancellationToken.None);

        Assert.Equal(["map Tcp 41000->41000 2h", "map Udp 41000->41000 2h", "unmap Udp 41000->41000"], router.Calls);
        Assert.Single(mapper.Mappings.Created);
    }

    [Fact]
    public async Task A_router_that_refuses_leaves_the_mapping_failed()
    {
        var (mapper, discovery) = await StartAsync();
        discovery.Announce(new FakeRouter { Refuse = true, External = null });

        await mapper.RegisterMappingAsync(Tcp);

        Assert.Single(mapper.Mappings.Failed);
        Assert.Null(mapper.MappedPort(41_000));
        Assert.Null(mapper.ExternalAddress);
    }

    [Fact]
    public async Task When_the_policy_says_no_the_router_is_never_asked()
    {
        var (mapper, discovery) = await StartAsync(enabled: false);
        var router = new FakeRouter();
        discovery.Announce(router);

        await mapper.RegisterMappingAsync(Tcp);

        Assert.False(mapper.Active);
        Assert.False(discovery.Searching);
        Assert.Empty(router.Calls);
        Assert.Single(mapper.Mappings.Pending);
    }
}
