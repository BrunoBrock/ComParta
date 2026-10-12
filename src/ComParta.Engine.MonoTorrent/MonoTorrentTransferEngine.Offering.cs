using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ComParta.Core;
using ComParta.Core.Ports;
using MonoTorrent;

namespace ComParta.Engine.MonoTorrent;

// Offering of the engine; the class is split by area, see MonoTorrentTransferEngine.cs.
public sealed partial class MonoTorrentTransferEngine
{
    public async Task OfferAsync(ShareId id, string filePath, CancellationToken ct = default)
    {
        await StopOfferAsync(id, forget: false);
        var offer = new Offer(id, filePath);
        _offers[id] = offer;
        OfferChanged?.Invoke(id, OfferState.Preparing);
        offer.Work = Task.Run(() => PrepareOfferAsync(offer), CancellationToken.None);
    }

    private async Task PrepareOfferAsync(Offer offer)
    {
        var engine = _engine;
        if (engine is null)
            return;
        try
        {
            var metadataPath = Path.Combine(_sharesFolder, offer.Id + ".torrent");
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var fromCache = File.Exists(metadataPath);
                var torrent = fromCache
                    ? await Torrent.LoadAsync(metadataPath)
                    : await CreateMetadataAsync(offer, metadataPath);

                // The save directory is the folder the file already sits in, so it is read where it is (AD-6).
                var manager = await engine.AddAsync(torrent, Path.GetDirectoryName(offer.FilePath)!, ToTorrentSettings());
                WatchIncoming(manager);
                WatchReceivers(offer, manager);
                offer.Manager = manager;
                await manager.HashCheckAsync(false);
                offer.Cancel.Token.ThrowIfCancellationRequested();

                if (manager.Complete)
                {
                    // Complete means every piece matched, so the engine only ever reads the original.
                    // While sending is stopped the offer waits, ready, until Continue starts it.
                    if (!_sendingStopped)
                        await manager.StartAsync();
                    offer.Invite = new MagnetLink(torrent.InfoHashes, torrent.Name, size: torrent.Size).ToV1String();
                    OfferChanged?.Invoke(offer.Id, OfferState.Ready);
                    return;
                }

                // Never start an incomplete offer: it could write to the original file.
                offer.Manager = null;
                await engine.RemoveAsync(manager);
                if (!fromCache)
                    break;
                // The file changed since it was first shared; describe it again.
                File.Delete(metadataPath);
            }
            OfferChanged?.Invoke(offer.Id, OfferState.Failed);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (!offer.Cancel.IsCancellationRequested)
                OfferChanged?.Invoke(offer.Id, OfferState.Failed);
        }
    }

    private static async Task<Torrent> CreateMetadataAsync(Offer offer, string metadataPath)
    {
        var creator = new TorrentCreator(TorrentType.V1Only) { CreatedBy = "ComParta", Private = false };
        var metadata = await creator.CreateAsync(new TorrentFileSource(offer.FilePath), offer.Cancel.Token);
        await File.WriteAllBytesAsync(metadataPath, metadata.Encode());
        return Torrent.Load(metadata);
    }

    public Task StopOfferingAsync(ShareId id, CancellationToken ct = default) => StopOfferAsync(id, forget: true);

    private async Task StopOfferAsync(ShareId id, bool forget)
    {
        if (_offers.TryRemove(id, out var offer))
        {
            offer.Cancel.Cancel();
            try { await offer.Work; }
            catch (Exception) { /* already reported */ }
            if (offer.Manager is { } manager && _engine is { } engine)
            {
                try
                {
                    await manager.StopAsync();
                    await engine.RemoveAsync(manager);
                }
                catch (Exception) { /* it is gone either way */ }
            }
        }
        if (forget)
            DeleteQuietly(Path.Combine(_sharesFolder, id + ".torrent"));
    }

    public Task<string?> GetInviteAsync(ShareId id, CancellationToken ct = default)
    {
        if (!_offers.TryGetValue(id, out var offer) || offer.Invite is not { } invite)
            return Task.FromResult<string?>(null);

        // The addresses are read now, not when the share was added: a laptop changes networks.
        if (_policy.AllowAddressInInvite)
        {
            foreach (var endPoint in InviteAddresses())
                invite += $"&{AddressHint}={endPoint}";
        }
        return Task.FromResult<string?>(invite);
    }

    /// <summary>
    /// Every address this computer has good reason to think others can use, best first: the one the
    /// router opened, then addresses on the newer internet protocol, then ones on the home network (AD-15).
    /// </summary>
    internal IEnumerable<IPEndPoint> InviteAddresses()
    {
        var found = new List<IPEndPoint>();
        if (_portMapper is { ExternalAddress: { } external } mapper && mapper.MappedPort(_policy.ListenPort) is { } mappedPort)
            found.Add(new IPEndPoint(external, mappedPort));
        if (AcceptIncoming && Socket.OSSupportsIPv6)
            found.AddRange(GlobalIPv6Addresses().Select(a => new IPEndPoint(a, _policy.ListenPort)));
        found.AddRange(LocalIPv4Addresses().Select(a => new IPEndPoint(a, _policy.ListenPort)));
        return found.Distinct().Take(MaxAddressHints);
    }

    private static IEnumerable<IPAddress> UpAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    private static IEnumerable<IPAddress> LocalIPv4Addresses() =>
        UpAddresses()
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .Distinct();

    /// <summary>Addresses on the newer internet protocol that reach this computer from anywhere.</summary>
    internal static IEnumerable<IPAddress> GlobalIPv6Addresses() => UpAddresses().Where(IsGlobalIPv6).Distinct();

    internal static bool IsGlobalIPv6(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6
            || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
            || address.IsIPv6Teredo || address.IsIPv4MappedToIPv6 || IPAddress.IsLoopback(address))
            return false;
        var bytes = address.GetAddressBytes();
        // Global unicast is 2000::/3; that leaves out unique local addresses (fc00::/7) and the rest.
        return (bytes[0] & 0xE0) == 0x20;
    }
}
