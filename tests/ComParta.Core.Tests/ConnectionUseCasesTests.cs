using System.Net;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

public sealed class ConnectionUseCasesTests
{
    private readonly FakeEngine _engine = new();

    private ConnectionUseCases Create() => new(_engine);

    private static ReachabilityFacts Facts(
        bool listening = true,
        bool busy = false,
        PortMappingState mapping = PortMappingState.NotRequested,
        string? externalAddress = null,
        bool globalIPv6 = false,
        bool listeningOnIPv6 = false,
        bool incoming = false) =>
        new(listening, busy, mapping, ExternalPort: mapping == PortMappingState.Obtained ? 40_000 : null,
            externalAddress is null ? null : IPAddress.Parse(externalAddress), globalIPv6, listeningOnIPv6, incoming);

    // One row per row of the matrix, then rows that show the first match winning.
    [Theory]
    // Nothing shared or received.
    [InlineData(false, false, PortMappingState.NotRequested, null, false, false, false, null)]
    [InlineData(false, false, PortMappingState.Obtained, null, true, true, true, null)]
    // The listen port is busy.
    [InlineData(false, true, PortMappingState.NotRequested, null, false, false, false, ConnectionHealth.Blocked)]
    [InlineData(false, true, PortMappingState.Obtained, "203.0.113.7", true, true, true, ConnectionHealth.Blocked)]
    // Another device from outside connected in the last hour.
    [InlineData(true, false, PortMappingState.Refused, null, false, false, true, ConnectionHealth.Good)]
    [InlineData(true, false, PortMappingState.Pending, null, false, false, true, ConnectionHealth.Good)]
    [InlineData(true, false, PortMappingState.Obtained, "192.168.0.2", false, false, true, ConnectionHealth.Good)]
    // The router agreed and its address is not read yet or is public.
    [InlineData(true, false, PortMappingState.Obtained, null, false, false, false, ConnectionHealth.Good)]
    [InlineData(true, false, PortMappingState.Obtained, "203.0.113.7", false, false, false, ConnectionHealth.Good)]
    // An internet-wide IPv6 address that the engine listens on.
    [InlineData(true, false, PortMappingState.Refused, null, true, true, false, ConnectionHealth.Good)]
    [InlineData(true, false, PortMappingState.NotRequested, null, true, true, false, ConnectionHealth.Good)]
    [InlineData(true, false, PortMappingState.Pending, null, true, true, false, ConnectionHealth.Good)]
    [InlineData(true, false, PortMappingState.Obtained, "100.64.8.8", true, true, false, ConnectionHealth.Good)]
    // An IPv6 address alone is not enough: it needs a listener on it, and the listener needs an address.
    [InlineData(true, false, PortMappingState.Refused, null, true, false, false, ConnectionHealth.Blocked)]
    [InlineData(true, false, PortMappingState.Refused, null, false, true, false, ConnectionHealth.Blocked)]
    // Still asking the router.
    [InlineData(true, false, PortMappingState.Pending, null, false, false, false, null)]
    [InlineData(true, false, PortMappingState.Pending, null, true, false, false, null)]
    // The router agreed but its address is private or shared among homes.
    [InlineData(true, false, PortMappingState.Obtained, "192.168.1.1", false, false, false, ConnectionHealth.Limited)]
    [InlineData(true, false, PortMappingState.Obtained, "10.20.30.40", false, false, false, ConnectionHealth.Limited)]
    [InlineData(true, false, PortMappingState.Obtained, "172.16.5.5", false, false, false, ConnectionHealth.Limited)]
    [InlineData(true, false, PortMappingState.Obtained, "100.64.8.8", false, false, false, ConnectionHealth.Limited)]
    // The router refused or was never asked, and no IPv6 can be used.
    [InlineData(true, false, PortMappingState.Refused, null, false, false, false, ConnectionHealth.Blocked)]
    [InlineData(true, false, PortMappingState.NotRequested, null, false, false, false, ConnectionHealth.Blocked)]
    public void Facts_become_health_by_the_first_rule_that_applies(
        bool listening, bool busy, PortMappingState mapping, string? externalAddress,
        bool globalIPv6, bool listeningOnIPv6, bool incoming, ConnectionHealth? expected)
    {
        var facts = Facts(listening, busy, mapping, externalAddress, globalIPv6, listeningOnIPv6, incoming);

        Assert.Equal(expected, ConnectionUseCases.Reduce(facts));
    }

    [Fact]
    public void Nothing_is_known_before_the_engine_reports()
    {
        var connection = Create();

        Assert.Null(connection.Current());
    }

    [Fact]
    public void Reported_facts_become_the_current_health()
    {
        var connection = Create();

        _engine.Report(Facts(mapping: PortMappingState.Obtained, externalAddress: "203.0.113.7"));
        Assert.Equal(ConnectionHealth.Good, connection.Current());

        _engine.Report(Facts(mapping: PortMappingState.Obtained, externalAddress: "100.70.0.1"));
        Assert.Equal(ConnectionHealth.Limited, connection.Current());

        _engine.Report(Facts(mapping: PortMappingState.Refused));
        Assert.Equal(ConnectionHealth.Blocked, connection.Current());

        _engine.Report(Facts(listening: false));
        Assert.Null(connection.Current());
    }

    [Fact]
    public void Each_real_change_raises_one_event_and_nothing_else_does()
    {
        var connection = Create();
        var raised = 0;
        connection.Changed += () => raised++;

        // Idle before anything was reported: no change.
        _engine.Report(Facts(listening: false));
        Assert.Equal(0, raised);

        // Still asking the router: health stays unknown.
        _engine.Report(Facts(mapping: PortMappingState.Pending));
        Assert.Equal(0, raised);

        _engine.Report(Facts(mapping: PortMappingState.Obtained));
        Assert.Equal(1, raised);

        // Different facts, same answer.
        _engine.Report(Facts(mapping: PortMappingState.Obtained, externalAddress: "203.0.113.7"));
        _engine.Report(Facts(mapping: PortMappingState.Refused, globalIPv6: true, listeningOnIPv6: true));
        Assert.Equal(1, raised);

        _engine.Report(Facts(mapping: PortMappingState.Refused));
        Assert.Equal(2, raised);

        // The answer becoming unknown again is a change too.
        _engine.Report(Facts(listening: false));
        Assert.Equal(3, raised);
        Assert.Null(connection.Current());
    }

    [Fact]
    public void The_event_is_raised_after_the_new_health_can_be_read()
    {
        var connection = Create();
        ConnectionHealth? seen = null;
        connection.Changed += () => seen = connection.Current();

        _engine.Report(Facts(busy: true, listening: false));

        Assert.Equal(ConnectionHealth.Blocked, seen);
    }

    [Theory]
    // Home and company networks.
    [InlineData("10.0.0.0", false)]
    [InlineData("10.255.255.255", false)]
    [InlineData("9.255.255.255", true)]
    [InlineData("11.0.0.0", true)]
    [InlineData("172.15.255.255", true)]
    [InlineData("172.16.0.0", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("172.32.0.0", true)]
    [InlineData("192.167.255.255", true)]
    [InlineData("192.168.0.0", false)]
    [InlineData("192.168.255.255", false)]
    [InlineData("192.169.0.0", true)]
    // The range internet providers share among homes.
    [InlineData("100.63.255.255", true)]
    [InlineData("100.64.0.0", false)]
    [InlineData("100.127.255.255", false)]
    [InlineData("100.128.0.0", true)]
    // Not for one device on the internet.
    [InlineData("127.0.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("169.254.10.10", false)]
    [InlineData("169.253.0.1", true)]
    [InlineData("224.0.0.251", false)]
    [InlineData("255.255.255.255", false)]
    // Ordinary internet addresses.
    [InlineData("8.8.8.8", true)]
    [InlineData("203.0.113.7", true)]
    // IPv6.
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("2a00:1450:4001:81b::200e", true)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd12:3456:789a::1", false)]
    [InlineData("ff02::1", false)]
    [InlineData("::ffff:192.168.1.5", false)]
    [InlineData("::ffff:8.8.8.8", true)]
    public void An_address_is_public_only_when_it_leads_outside_the_local_network(string address, bool expected)
    {
        Assert.Equal(expected, NetworkAddresses.IsPublic(IPAddress.Parse(address)));
    }
}
