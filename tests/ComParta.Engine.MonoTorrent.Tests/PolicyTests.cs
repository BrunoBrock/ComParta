using ComParta.Core;
using MonoTorrent.Connections;

namespace ComParta.Engine.MonoTorrent.Tests;

public sealed class PolicyTests
{
    [Fact]
    public void The_closed_policy_enables_no_discovery_at_all()
    {
        var settings = MonoTorrentTransferEngine.ToEngineSettings(SharingPolicy.Closed, "cache");

        Assert.False(settings.AllowLocalPeerDiscovery);
        Assert.False(settings.AllowPortForwarding);
        Assert.Null(settings.DhtEndPoint);
        Assert.DoesNotContain(EncryptionType.PlainText, settings.AllowedEncryption);
    }

    [Fact]
    public void The_default_policy_enables_local_discovery_and_public_lookup_only()
    {
        var settings = MonoTorrentTransferEngine.ToEngineSettings(SharingPolicy.Default, "cache");

        Assert.True(settings.AllowLocalPeerDiscovery);
        Assert.True(settings.AllowPortForwarding);
        Assert.NotNull(settings.DhtEndPoint);
        Assert.DoesNotContain(EncryptionType.PlainText, settings.AllowedEncryption);
    }

    [Fact]
    public void Connections_are_accepted_on_both_internet_protocols_when_the_system_has_them()
    {
        var settings = MonoTorrentTransferEngine.ToEngineSettings(SharingPolicy.Default with { ListenPort = 41_000 }, "cache");

        Assert.Equal(41_000, settings.ListenEndPoints["ipv4"].Port);
        Assert.Equal(System.Net.Sockets.Socket.OSSupportsIPv6, settings.ListenEndPoints.ContainsKey("ipv6"));
    }

    [Fact]
    public void A_device_that_refuses_incoming_connections_listens_nowhere()
    {
        var settings = MonoTorrentTransferEngine.ToEngineSettings(SharingPolicy.Default, "cache", acceptIncoming: false);

        Assert.Empty(settings.ListenEndPoints);
    }

    [Theory]
    [InlineData("2804:14c:65a1:4000::1", true)]
    [InlineData("2001:db8::1", true)]
    [InlineData("fe80::1", false)]
    [InlineData("fd12:3456::1", false)]
    [InlineData("::1", false)]
    [InlineData("2001:0:4136:e378::1", false)]
    [InlineData("ff02::1", false)]
    [InlineData("192.168.0.10", false)]
    public void Only_global_IPv6_addresses_count_as_reachable_from_anywhere(string address, bool global) =>
        Assert.Equal(global, MonoTorrentTransferEngine.IsGlobalIPv6(System.Net.IPAddress.Parse(address)));
}
