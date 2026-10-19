using ComParta.Core;
using MonoTorrent;
using MonoTorrent.Client;

namespace ComParta.Engine.MonoTorrent;

// The helper computer (AD-23); the class is split by area, see MonoTorrentTransferEngine.cs.
public sealed partial class MonoTorrentTransferEngine
{
    /// <summary>How often a helper that did not answer, or a file it does not hold yet, is tried again.</summary>
    internal static readonly TimeSpan HelperRetryInterval = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _helperGate = new(1, 1);
    private volatile HelperClient? _helper;
    private volatile HelperAbout? _helperAbout;
    private volatile bool _helperHoldsEverything;
    private Timer? _helperTimer;

    public async Task SetHelperAsync(HelperComputer? helper, CancellationToken ct = default)
    {
        await _helperGate.WaitAsync(ct);
        try
        {
            if (_helper is { } previous && previous.Helper != helper)
            {
                // The old helper keeps nothing of ours: each file it holds is taken back, as when a share stops.
                foreach (var offer in _offers.Values.Where(o => o.HeldByHelper))
                {
                    await previous.RemoveAsync(IdOf(offer), CancellationToken.None);
                    offer.HeldByHelper = false;
                }
            }
            _helper = helper is null ? null : new HelperClient(helper);
            _helperAbout = null;
            _helperHoldsEverything = false;
        }
        finally
        {
            _helperGate.Release();
        }
        await SyncHelperAsync(ct);
    }

    /// <summary>
    /// Hands the helper every file that is ready and it does not hold yet, and makes every shared file
    /// announce to the helper's public server, so the helper and any app find this computer.
    /// Safe to call at any time; the retry timer calls it until the helper holds everything.
    /// </summary>
    internal async Task SyncHelperAsync(CancellationToken ct = default)
    {
        await _helperGate.WaitAsync(ct);
        try
        {
            var helper = _helper;
            var about = helper is null ? null : await helper.AboutAsync(ct);
            // A helper that did not answer this time keeps its place in links: it may be back by the time someone asks.
            if (about is not null)
                _helperAbout = about;
            foreach (var offer in _offers.Values)
            {
                if (offer.Manager is not { } manager || offer.Invite is null)
                    continue;
                var added = false;
                if (helper is not null && about is not null && !offer.HeldByHelper)
                {
                    var description = await File.ReadAllBytesAsync(DescriptionPath(offer.Id), ct);
                    offer.HeldByHelper = added = await helper.AddAsync(description, ct);
                }
                try
                {
                    await UseServersAsync(manager);
                    // Announced now, not at its next turn, so the helper meets this computer at once.
                    if (added && manager.State is TorrentState.Seeding or TorrentState.Downloading)
                        await manager.TrackerManager.AnnounceAsync(ct);
                }
                catch (Exception) when (!ct.IsCancellationRequested) { /* the next turn tries again */ }
            }
            // Counted again at the end: a file that became ready meanwhile is not held yet.
            _helperHoldsEverything = helper is not null && about is not null
                && _offers.Values.Where(o => o.Manager is not null && o.Invite is not null).All(o => o.HeldByHelper);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            _helperHoldsEverything = false;
        }
        finally
        {
            _helperGate.Release();
        }
        ReportReachability();
    }

    /// <summary>
    /// Takes a share back from the helper when it stops, so the helper does not keep it. The helper is
    /// told in the background: Stop sharing must not wait on another computer.
    /// </summary>
    private void ForgetAtHelper(Offer offer)
    {
        if (offer.HeldByHelper && _helper is { } helper)
        {
            var id = IdOf(offer);
            _ = Task.Run(() => helper.RemoveAsync(id, CancellationToken.None));
        }
        offer.HeldByHelper = false;
    }

    /// <summary>
    /// The public servers a shared file announces to and a link names: the helper's first, so every
    /// app finds the helper, then the ones the person named (AD-8, AD-16).
    /// </summary>
    private IReadOnlyList<Uri> AnnounceServers()
    {
        var helperServers = _helper is not null && _helperAbout is { } about ? about.Trackers : [];
        return [.. helperServers.Concat(_publicServers).Distinct()];
    }

    private Task UseServersAsync(TorrentManager manager) => UsePublicServersAsync(manager, AnnounceServers());

    private void StartHelperTimer() =>
        _helperTimer = new Timer(_ =>
        {
            if (_helper is not null && !_helperHoldsEverything)
                _ = SyncHelperAsync();
        }, null, HelperRetryInterval, HelperRetryInterval);

    private async Task StopHelperTimerAsync()
    {
        if (_helperTimer is { } timer)
        {
            _helperTimer = null;
            await timer.DisposeAsync();
        }
    }

    private string DescriptionPath(ShareId id) => Path.Combine(_sharesFolder, id + ".torrent");

    private static string IdOf(Offer offer) => offer.Manager is { } manager ? KeyOf(manager.InfoHashes).ToLowerInvariant() : "";

    /// <summary>For tests: whether the helper holds every ready file and answers.</summary>
    internal bool HelperHoldsEverything => _helperHoldsEverything;
}
