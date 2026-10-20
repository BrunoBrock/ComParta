using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using MonoTorrent.BEncoding;
using MonoTorrent.Connections.TrackerServer;

namespace ComParta.Helper;

/// <summary>
/// The rules the helper's public server (tracker) applies to every announcement before it is answered,
/// on HTTP and UDP alike.
/// </summary>
internal static class AnnounceRules
{
    /// <summary>
    /// Returns the announcement as the tracker should read it:
    /// <list type="bullet">
    /// <item>An address an app claims for itself (<c>ip=</c>) is kept only when it is the address the
    /// request came from, or the request came from this computer (the helper announcing its public
    /// address). Otherwise anyone could make the tracker send every app to a third party.</item>
    /// <item>An app that takes no connections (some announce a port of -1) still learns where the files
    /// are: its port becomes 0, which nobody connects to, instead of the request failing.</item>
    /// </list>
    /// </summary>
    public static NameValueCollection Clean(NameValueCollection request, IPAddress remote, bool isScrape)
    {
        if (isScrape)
            return request;
        var cleaned = new NameValueCollection(request);
        if (cleaned["ip"] is { } claimed
            && IPAddress.TryParse(claimed, out var address)
            && !IPAddress.IsLoopback(remote)
            && !Plain(address).Equals(Plain(remote)))
            cleaned.Remove("ip");
        if (cleaned["port"] is { } port
            && (!int.TryParse(port, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number is < 0 or > 65535))
            cleaned["port"] = "0";
        return cleaned;
    }

    /// <summary>An IPv4 address that arrived wrapped as IPv6 is the IPv4 address.</summary>
    public static IPAddress Plain(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}

/// <summary>The tracker's HTTP side. The web server owns the port and hands each request here.</summary>
internal sealed class WebTrackerListener : TrackerListener
{
    protected override void StartCore(CancellationToken token) => RaiseStatusChanged(MonoTorrent.Connections.ListenerStatus.Listening);

    public override BEncodedDictionary Handle(NameValueCollection collection, IPAddress remoteAddress, bool isScrape) =>
        base.Handle(AnnounceRules.Clean(collection, remoteAddress, isScrape), remoteAddress, isScrape);
}

/// <summary>The tracker's UDP side, which most phone apps use.</summary>
internal sealed class CheckedUdpTrackerListener(IPEndPoint endPoint) : UdpTrackerListener(endPoint)
{
    public override BEncodedDictionary Handle(NameValueCollection collection, IPAddress remoteAddress, bool isScrape) =>
        base.Handle(AnnounceRules.Clean(collection, AnnounceRules.Plain(remoteAddress), isScrape), AnnounceRules.Plain(remoteAddress), isScrape);
}
