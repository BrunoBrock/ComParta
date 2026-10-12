using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ComParta.Core;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Connections;
using MonoTorrent.Connections.Peer;
using MonoTorrent.PortForwarding;

namespace ComParta.Engine.MonoTorrent;

/// <summary>
/// Reads what the engine can observe about being reachable and reports it as neutral facts (AD-15).
/// It decides nothing: what the facts mean is core's business. It only looks at the engine's own
/// state and this computer's addresses, and contacts nothing (AD-9), and it never starts or stops
/// the router discovery, which is shared by everything in the process.
/// </summary>
/// <param name="clock">Times the window of an incoming connection.</param>
/// <param name="isOutsideLocalNetwork">Judges whether an address leads outside the local network; core's rule when null.</param>
/// <param name="localAddresses">Lists the addresses of this computer; the network connections that are up when null.</param>
internal sealed class ReachabilityProbe(
    TimeProvider clock, Func<IPAddress, bool>? isOutsideLocalNetwork = null, Func<IEnumerable<IPAddress>>? localAddresses = null)
{
    private readonly Func<IPAddress, bool> _isOutside = isOutsideLocalNetwork ?? NetworkAddresses.IsPublic;
    private readonly Func<IEnumerable<IPAddress>> _localAddresses = localAddresses ?? LocalAddresses;

    /// <summary>How long one incoming connection from outside counts as proof that others can get in.</summary>
    internal static readonly TimeSpan IncomingWindow = TimeSpan.FromHours(1);

    /// <summary>How long the router may stay silent before the mapping counts as refused.</summary>
    internal static readonly TimeSpan PendingLimit = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly List<(IPEndPoint EndPoint, IPeerConnectionListener Listener)> _listeners = [];
    private DateTimeOffset? _lastOutsideIncoming;
    private DateTimeOffset? _pendingSince;

    /// <summary>Remembers a listener the engine created, so its status can be read later.</summary>
    public IPeerConnectionListener Track(IPEndPoint endPoint, IPeerConnectionListener listener)
    {
        lock (_gate)
            _listeners.Add((endPoint, listener));
        return listener;
    }

    /// <summary>Forgets the listeners, any earlier incoming connection and how long the router has been silent, for a new engine.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _listeners.Clear();
            _lastOutsideIncoming = null;
            _pendingSince = null;
        }
    }

    /// <summary>
    /// Notes a connection another device made to us. Only one that comes from outside the local
    /// network counts: a device in the same home proves nothing about the internet.
    /// </summary>
    public void NoteConnection(Direction direction, Uri? remote)
    {
        if (direction != Direction.Incoming || remote is null)
            return;
        if (!IPAddress.TryParse(remote.DnsSafeHost, out var address) || !_isOutside(address))
            return;
        lock (_gate)
            _lastOutsideIncoming = clock.GetUtcNow();
    }

    /// <summary>The facts as they are right now.</summary>
    /// <param name="externalAddress">The router's address on the internet, once the router has told it.</param>
    public ReachabilityFacts Read(SharingPolicy policy, ClientEngine? engine, IPAddress? externalAddress = null)
    {
        List<(IPEndPoint EndPoint, IPeerConnectionListener Listener)> listeners;
        bool incoming;
        lock (_gate)
        {
            listeners = [.. _listeners];
            incoming = _lastOutsideIncoming is { } at && clock.GetUtcNow() - at <= IncomingWindow;
        }

        var statuses = listeners.Select(l => (l.EndPoint, Status: l.Listener.Status)).ToList();
        var listening = statuses.Any(l => l.Status == ListenerStatus.Listening);
        var busy = statuses.Any(l => l.Status == ListenerStatus.PortNotFree);
        var ipv6 = statuses.Any(l => l.Status == ListenerStatus.Listening && l.EndPoint.AddressFamily == AddressFamily.InterNetworkV6);

        var ports = statuses.Where(l => l.EndPoint.AddressFamily == AddressFamily.InterNetwork).Select(l => l.EndPoint.Port).ToHashSet();
        var (mapping, externalPort) = ReadMapping(policy, engine?.PortMappings, ports, listening);
        mapping = GiveUpOnSilentRouter(mapping, listening);

        return new ReachabilityFacts(listening, busy, mapping, externalPort, externalAddress,
            HasGlobalIPv6(_localAddresses()), ipv6, incoming);
    }

    /// <summary>
    /// What became of asking the router to open the listen port. While the engine listens but the
    /// router's lists do not show the port yet, the request is about to be made, so it is pending.
    /// </summary>
    private static (PortMappingState State, int? ExternalPort) ReadMapping(
        SharingPolicy policy, Mappings? mappings, HashSet<int> ports, bool listening)
    {
        if (!policy.AllowPortForwarding)
            return (PortMappingState.NotRequested, null);

        static bool Ours(Mapping m, HashSet<int> ports) => m.Protocol == Protocol.Tcp && ports.Contains(m.PrivatePort);

        if (mappings?.Created.FirstOrDefault(m => Ours(m, ports)) is { } created)
            return (PortMappingState.Obtained, created.PublicPort);
        if (mappings?.Pending.Any(m => Ours(m, ports)) == true)
            return (PortMappingState.Pending, null);
        if (mappings?.Failed.Any(m => Ours(m, ports)) == true)
            return (PortMappingState.Refused, null);
        return (listening ? PortMappingState.Pending : PortMappingState.NotRequested, null);
    }

    /// <summary>
    /// A router that has not answered within <see cref="PendingLimit"/> is not going to: the mapping
    /// becomes refused. The count starts when pending is first seen and starts over whenever the
    /// state leaves pending or the engine stops listening.
    /// </summary>
    private PortMappingState GiveUpOnSilentRouter(PortMappingState state, bool listening)
    {
        lock (_gate)
        {
            if (state != PortMappingState.Pending || !listening)
            {
                _pendingSince = null;
                return state;
            }
            var now = clock.GetUtcNow();
            _pendingSince ??= now;
            return now - _pendingSince.Value > PendingLimit ? PortMappingState.Refused : state;
        }
    }

    /// <summary>True when any of the addresses is an internet-wide IPv6 address.</summary>
    internal static bool HasGlobalIPv6(IEnumerable<IPAddress> addresses) =>
        addresses.Any(a => a.AddressFamily == AddressFamily.InterNetworkV6 && NetworkAddresses.IsPublic(a));

    /// <summary>The addresses of the network connections that are up. Tunnels are left out: they are not a way in.</summary>
    private static IEnumerable<IPAddress> LocalAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .ToList();
        }
        catch (Exception e) when (e is NetworkInformationException or PlatformNotSupportedException)
        {
            return [];
        }
    }
}
