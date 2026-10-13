using System.Net;
using System.Net.Sockets;
using Mono.Nat;
using MonoTorrent.PortForwarding;
using Mapping = MonoTorrent.PortForwarding.Mapping;
using Protocol = MonoTorrent.PortForwarding.Protocol;

namespace ComParta.Engine.MonoTorrent;

/// <summary>A home router that can open a port for this computer (UPnP or NAT-PMP).</summary>
internal interface INatGateway
{
    /// <summary>Tells devices found more than once apart.</summary>
    string Key { get; }
    /// <summary>Opens the port and returns the public port the router chose.</summary>
    Task<int> MapAsync(Protocol protocol, int privatePort, int publicPort, TimeSpan lifetime);
    Task UnmapAsync(Protocol protocol, int privatePort, int publicPort);
    Task<IPAddress?> GetExternalAddressAsync();
}

/// <summary>Looks for routers on the local network. Talks only to the local network (AD-9).</summary>
internal interface INatDiscovery
{
    event Action<INatGateway>? Found;
    void Start();
    void Stop();
}

/// <summary>
/// Asks the home router to pass incoming connections to this computer, renews the mapping before
/// it runs out, and removes it when the app stops (AD-15). MonoTorrent registers the ports it uses;
/// this class does the asking, so the result and the router's public address can be reported.
/// </summary>
internal sealed class PortMapper : IPortForwarder, IDisposable
{
    /// <summary>How long each mapping is asked for; it is renewed at half of that.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);

    /// <summary>
    /// How long one request to a router may take. Some routers, and other devices that answer the
    /// same search, never answer a request; without a limit that held up sharing and quitting.
    /// </summary>
    public static readonly TimeSpan DefaultRouterTimeout = TimeSpan.FromSeconds(10);

    private readonly INatDiscovery _discovery;
    private readonly bool _enabled;
    private readonly TimeSpan _routerTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<INatGateway> _gateways = [];
    private readonly HashSet<Mapping> _registered = [];
    private readonly Dictionary<Mapping, (INatGateway Gateway, int PublicPort)> _created = [];
    private readonly HashSet<Mapping> _failed = [];
    private Timer? _renewal;

    public PortMapper(INatDiscovery discovery, bool enabled, TimeSpan? routerTimeout = null)
    {
        _discovery = discovery;
        _enabled = enabled;
        _routerTimeout = routerTimeout ?? DefaultRouterTimeout;
    }

    public event EventHandler? MappingsChanged;

    public bool Active { get; private set; }

    /// <summary>The router's address on the internet, once a router answered.</summary>
    public IPAddress? ExternalAddress { get; private set; }

    public Mappings Mappings
    {
        get
        {
            lock (_registered)
            {
                var mappings = Mappings.Empty;
                foreach (var mapping in _registered)
                {
                    mappings = _created.TryGetValue(mapping, out var created)
                        ? mappings.WithCreated(new Mapping(mapping.Protocol, mapping.PrivatePort, created.PublicPort))
                        : _failed.Contains(mapping) ? mappings.WithFailed(mapping) : mappings.WithPending(mapping);
                }
                return mappings;
            }
        }
    }

    /// <summary>The public port the router opened for incoming connections, or null.</summary>
    public int? MappedPort(int privatePort)
    {
        lock (_registered)
        {
            return _created.FirstOrDefault(c => c.Key.Protocol == Protocol.Tcp && c.Key.PrivatePort == privatePort).Value
                is { Gateway: not null } created ? created.PublicPort : null;
        }
    }

    public Task StartAsync(CancellationToken token)
    {
        if (!_enabled || Active)
            return Task.CompletedTask;
        Active = true;
        _discovery.Found += OnFound;
        try { _discovery.Start(); }
        catch (Exception) { /* no network to search; the app still works without a mapping */ }
        var every = Lifetime / 2;
        _renewal = new Timer(_ => _ = RenewAsync(), null, every, every);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken token) => StopAsync(true, token);

    public async Task StopAsync(bool removeExisting, CancellationToken token)
    {
        if (!Active)
            return;
        Active = false;
        _discovery.Found -= OnFound;
        try { _discovery.Stop(); }
        catch (Exception) { /* stopping anyway */ }
        if (_renewal is { } renewal)
        {
            _renewal = null;
            await renewal.DisposeAsync();
        }
        if (removeExisting)
        {
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                foreach (var (mapping, created) in Snapshot())
                    await UnmapQuietlyAsync(created.Gateway, mapping, created.PublicPort, _routerTimeout);
                lock (_registered)
                    _created.Clear();
            }
            finally { _gate.Release(); }
            Changed();
        }
    }

    /// <summary>
    /// Asks the routers already found to open the port, without waiting for them: MonoTorrent waits
    /// for this before a share starts, and a share must not wait on a router (AD-15).
    /// </summary>
    public Task RegisterMappingAsync(Mapping mapping)
    {
        lock (_registered)
        {
            if (!_registered.Add(mapping))
                return Task.CompletedTask;
        }
        Changed();
        if (!Active)
            return Task.CompletedTask;
        INatGateway[] gateways;
        lock (_gateways)
            gateways = [.. _gateways];
        foreach (var gateway in gateways)
            _ = MapAsync(gateway, mapping);
        return Task.CompletedTask;
    }

    public async Task UnregisterMappingAsync(Mapping mapping, CancellationToken token)
    {
        (INatGateway Gateway, int PublicPort) created;
        bool wasCreated;
        lock (_registered)
        {
            _registered.Remove(mapping);
            _failed.Remove(mapping);
            wasCreated = _created.Remove(mapping, out created);
        }
        if (wasCreated)
            await UnmapQuietlyAsync(created.Gateway, mapping, created.PublicPort, _routerTimeout);
        Changed();
    }

    /// <summary>Asks again for every mapping, so the router keeps it. Runs on a timer; tests call it directly.</summary>
    internal async Task RenewAsync()
    {
        await _gate.WaitAsync();
        try
        {
            foreach (var (mapping, created) in Snapshot())
            {
                try
                {
                    await created.Gateway.MapAsync(mapping.Protocol, mapping.PrivatePort, created.PublicPort, Lifetime)
                        .WaitAsync(_routerTimeout);
                }
                catch (Exception)
                {
                    lock (_registered)
                    {
                        _created.Remove(mapping);
                        if (_registered.Contains(mapping))
                            _failed.Add(mapping);
                    }
                    Changed();
                }
            }
        }
        finally { _gate.Release(); }
    }

    private void OnFound(INatGateway gateway)
    {
        lock (_gateways)
        {
            if (_gateways.Any(g => g.Key == gateway.Key))
                return;
            _gateways.Add(gateway);
        }
        _ = UseGatewayAsync(gateway);
    }

    private async Task UseGatewayAsync(INatGateway gateway)
    {
        try
        {
            var address = await gateway.GetExternalAddressAsync().WaitAsync(_routerTimeout);
            if (address is { AddressFamily: AddressFamily.InterNetwork } && !address.Equals(IPAddress.Any))
                ExternalAddress = address;
        }
        catch (Exception) { /* the mapping can still work without knowing the address */ }

        Mapping[] registered;
        lock (_registered)
            registered = [.. _registered];
        foreach (var mapping in registered)
            await MapAsync(gateway, mapping);
    }

    private async Task MapAsync(INatGateway gateway, Mapping mapping)
    {
        await _gate.WaitAsync();
        try
        {
            lock (_registered)
            {
                if (_created.ContainsKey(mapping) || !_registered.Contains(mapping) || !Active)
                    return;
            }
            try
            {
                var publicPort = await gateway.MapAsync(mapping.Protocol, mapping.PrivatePort, mapping.PublicPort, Lifetime)
                    .WaitAsync(_routerTimeout);
                lock (_registered)
                {
                    _failed.Remove(mapping);
                    _created[mapping] = (gateway, publicPort);
                }
            }
            catch (Exception)
            {
                lock (_registered)
                    _failed.Add(mapping);
            }
        }
        finally { _gate.Release(); }
        Changed();
    }

    private List<(Mapping Mapping, (INatGateway Gateway, int PublicPort) Created)> Snapshot()
    {
        lock (_registered)
            return [.. _created.Select(c => (c.Key, c.Value))];
    }

    private static async Task UnmapQuietlyAsync(INatGateway gateway, Mapping mapping, int publicPort, TimeSpan timeout)
    {
        try { await gateway.UnmapAsync(mapping.Protocol, mapping.PrivatePort, publicPort).WaitAsync(timeout); }
        catch (Exception) { /* the router lets it expire */ }
    }

    private void Changed() => MappingsChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _renewal?.Dispose();
        _gate.Dispose();
    }
}

/// <summary>Router discovery with Mono.Nat: UPnP and NAT-PMP on the local network only.</summary>
internal sealed class MonoNatDiscovery : INatDiscovery
{
    public event Action<INatGateway>? Found;

    public void Start()
    {
        NatUtility.DeviceFound += OnDeviceFound;
        NatUtility.StartDiscovery(NatProtocol.Upnp, NatProtocol.Pmp);
    }

    public void Stop()
    {
        NatUtility.DeviceFound -= OnDeviceFound;
        NatUtility.StopDiscovery();
    }

    private void OnDeviceFound(object? sender, DeviceEventArgs e) => Found?.Invoke(new MonoNatGateway(e.Device));

    private sealed class MonoNatGateway(INatDevice device) : INatGateway
    {
        private const string Description = "ComParta";

        public string Key { get; } = $"{device.NatProtocol} {device.DeviceEndpoint}";

        public async Task<int> MapAsync(Protocol protocol, int privatePort, int publicPort, TimeSpan lifetime)
        {
            var natProtocol = protocol == Protocol.Tcp ? Mono.Nat.Protocol.Tcp : Mono.Nat.Protocol.Udp;
            try
            {
                var mapped = await device.CreatePortMapAsync(
                    new Mono.Nat.Mapping(natProtocol, privatePort, publicPort, (int)lifetime.TotalSeconds, Description));
                return mapped.PublicPort;
            }
            catch (MappingException) when (device.NatProtocol == NatProtocol.Upnp)
            {
                // Many routers accept only mappings without an end; those are removed on quit instead.
                var mapped = await device.CreatePortMapAsync(new Mono.Nat.Mapping(natProtocol, privatePort, publicPort, 0, Description));
                return mapped.PublicPort;
            }
        }

        public Task UnmapAsync(Protocol protocol, int privatePort, int publicPort) =>
            device.DeletePortMapAsync(new Mono.Nat.Mapping(
                protocol == Protocol.Tcp ? Mono.Nat.Protocol.Tcp : Mono.Nat.Protocol.Udp, privatePort, publicPort));

        public Task<IPAddress?> GetExternalAddressAsync() => device.GetExternalIPAsync()!;
    }
}
