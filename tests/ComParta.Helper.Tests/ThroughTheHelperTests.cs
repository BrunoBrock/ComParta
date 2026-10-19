using ComParta.Core;
using ComParta.Core.Ports;
using ComParta.Engine.MonoTorrent;
using MonoTorrent;

namespace ComParta.Helper.Tests;

/// <summary>
/// The case the helper exists for (AD-23): the sharing computer and the receiving one both refuse every
/// incoming connection, as behind a home router nobody opened or on mobile data. Real ComParta engines
/// on both sides and a real helper between them; the file can only travel through the helper.
/// </summary>
public sealed class ThroughTheHelperTests : HelperTestBase
{
    private readonly List<MonoTorrentTransferEngine> _engines = [];

    public override async Task DisposeAsync()
    {
        foreach (var engine in _engines)
            await engine.StopAsync();
        await base.DisposeAsync();
    }

    private sealed record Paths(string Root) : IAppPaths
    {
        public string DatabasePath => Path.Combine(Root, "comparta.db");
        public string EngineRoot => Path.Combine(Root, "engine");
        public string ReceivedRoot => Path.Combine(Root, "Received");
        public string LogsRoot => Path.Combine(Root, "logs");
        public string SavedRoot => Path.Combine(Root, "Downloads");
    }

    /// <summary>A device nobody can connect to, with every other way of finding the other side closed.</summary>
    private async Task<(MonoTorrentTransferEngine Engine, Paths Paths)> StartUnreachableDeviceAsync(string name)
    {
        var paths = new Paths(Path.Combine(Folder, name));
        var engine = new MonoTorrentTransferEngine(paths) { AcceptIncoming = false };
        _engines.Add(engine);
        await engine.StartAsync(SharingPolicy.Closed with { ListenPort = FreeTcpPort(), AllowServersInDownloadLinks = true });
        return (engine, paths);
    }

    private static async Task OfferAndWaitAsync(MonoTorrentTransferEngine engine, ShareId id, string path)
    {
        var ready = new TaskCompletionSource<OfferState>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.OfferChanged += (changed, state) =>
        {
            if (changed == id && state != OfferState.Preparing)
                ready.TrySetResult(state);
        };
        await engine.OfferAsync(id, path);
        Assert.Equal(OfferState.Ready, await ready.Task.WaitAsync(Timeout));
    }

    [Fact]
    public async Task A_file_arrives_through_the_helper_when_neither_side_takes_connections()
    {
        var helper = await StartHelperAsync();
        var original = CreateFile("holiday.mp4", 3 * 1024 * 1024 + 7);
        var (sender, senderPaths) = await StartUnreachableDeviceAsync("sender");
        var (receiver, receiverPaths) = await StartUnreachableDeviceAsync("receiver");

        var shareId = ShareId.New();
        await OfferAndWaitAsync(sender, shareId, original);
        // The sender hands the helper the description it shares; the helper starts asking for the bytes.
        var description = await File.ReadAllBytesAsync(Path.Combine(senderPaths.EngineRoot, "shares", shareId + ".torrent"));
        Assert.Equal(System.Net.HttpStatusCode.Created, (await AddAsync(helper, description)).StatusCode);
        await WaitUntilTheHelperIsListedAsync(helper, Torrent.Load(description).InfoHashes);
        // The sender announces on the helper's server, learns where the helper is and connects out to it.
        await sender.SetPublicServersAsync([helper.TrackerUri]);

        var invite = (await sender.GetInviteAsync(shareId))!;
        Assert.Contains("tr=" + Uri.EscapeDataString(helper.TrackerUri.OriginalString), invite, StringComparison.Ordinal);

        var done = new TaskCompletionSource<ReceiveProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.ReceiveProgressed += progress =>
        {
            if (progress.Done || progress.Failed)
                done.TrySetResult(progress);
        };
        Assert.True(await receiver.ReceiveAsync(TransferId.New(), invite));

        var result = await done.Task.WaitAsync(Timeout);
        Assert.True(result.Done);
        Assert.Equal(Checksum(original), Checksum(Path.Combine(receiverPaths.ReceivedRoot, "holiday.mp4")));
        // The helper holds a whole copy, so it keeps serving after the sender goes away.
        Assert.Equal(100, Assert.Single(helper.Files).Percent);
    }

    [Fact]
    public async Task The_helper_keeps_serving_after_the_sender_is_gone()
    {
        var helper = await StartHelperAsync();
        var original = CreateFile("song.mp3", 700_000);
        var (sender, senderPaths) = await StartUnreachableDeviceAsync("sender");
        var shareId = ShareId.New();
        await OfferAndWaitAsync(sender, shareId, original);
        var description = await File.ReadAllBytesAsync(Path.Combine(senderPaths.EngineRoot, "shares", shareId + ".torrent"));
        await AddAsync(helper, description);
        await WaitUntilTheHelperIsListedAsync(helper, Torrent.Load(description).InfoHashes);
        await sender.SetPublicServersAsync([helper.TrackerUri]);
        var invite = (await sender.GetInviteAsync(shareId))!;

        var until = DateTime.UtcNow + Timeout;
        while (Assert.Single(helper.Files).Percent < 100)
        {
            Assert.True(DateTime.UtcNow < until, "The helper never got the whole file.");
            await Task.Delay(100);
        }
        await sender.StopAsync();

        var (receiver, receiverPaths) = await StartUnreachableDeviceAsync("receiver");
        var done = new TaskCompletionSource<ReceiveProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.ReceiveProgressed += progress =>
        {
            if (progress.Done || progress.Failed)
                done.TrySetResult(progress);
        };
        Assert.True(await receiver.ReceiveAsync(TransferId.New(), invite));

        Assert.True((await done.Task.WaitAsync(Timeout)).Done);
        Assert.Equal(Checksum(original), Checksum(Path.Combine(receiverPaths.ReceivedRoot, "song.mp3")));
    }
}
