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
}
