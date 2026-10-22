using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using ComParta.Core;
using ComParta.Core.Ports;

namespace ComParta.Engine.MonoTorrent.Tests;

/// <summary>
/// More options on a download (epic 14 story 4) on real engines: files left out never arrive, and a
/// download kept shared goes on being sent from the Received folder until sharing stops.
/// </summary>
public sealed class ReceivingChoicesTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "comparta-tests", Guid.NewGuid().ToString("N"));
    private readonly List<MonoTorrentTransferEngine> _engines = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var engine in _engines)
            await engine.StopAsync();
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    private sealed record Paths(string Root) : IAppPaths
    {
        public string DatabasePath => Path.Combine(Root, "comparta.db");
        public string EngineRoot => Path.Combine(Root, "engine");
        public string ReceivedRoot => Path.Combine(Root, "Received");
        public string LogsRoot => Path.Combine(Root, "logs");
        public string SavedRoot => Path.Combine(Root, "Downloads");
    }

    private async Task<(MonoTorrentTransferEngine Engine, Paths Paths)> StartDeviceAsync(string name)
    {
        var paths = new Paths(Path.Combine(_folder, name));
        var engine = new MonoTorrentTransferEngine(paths);
        _engines.Add(engine);
        await engine.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort(), AllowServersInDownloadLinks = true });
        return (engine, paths);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private string CreateFile(string name, int sizeBytes)
    {
        var path = Path.Combine(_folder, "originals", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(sizeBytes));
        return path;
    }

    private static string Checksum(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static TaskCompletionSource<ReceiveProgress> WhenFinished(MonoTorrentTransferEngine receiver, TransferId id)
    {
        var done = new TaskCompletionSource<ReceiveProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.ReceiveProgressed += progress =>
        {
            if (progress.Id == id && (progress.Done || progress.Failed))
                done.TrySetResult(progress);
        };
        return done;
    }

    private static async Task OfferAsync(MonoTorrentTransferEngine engine, ShareId id, string name, IReadOnlyList<ShareItem>? items = null, string? file = null)
    {
        var ready = new TaskCompletionSource<OfferState>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.OfferChanged += (changed, state) =>
        {
            if (changed == id && state != OfferState.Preparing)
                ready.TrySetResult(state);
        };
        if (file is not null)
            await engine.OfferAsync(id, file);
        else
            await engine.OfferItemsAsync(id, name, items!);
        Assert.Equal(OfferState.Ready, await ready.Task.WaitAsync(Timeout));
    }

    private static async Task<IReadOnlyList<ReceiveFile>> FilesOfAsync(MonoTorrentTransferEngine engine, TransferId id)
    {
        var until = DateTime.UtcNow + Timeout;
        while (true)
        {
            if (engine.GetReceiveFiles(id) is { } files)
                return files;
            Assert.True(DateTime.UtcNow < until, "The files of the download never became known.");
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task A_file_left_out_never_arrives_and_the_others_arrive_whole()
    {
        var film = CreateFile(Path.Combine("Films", "forest.mkv"), 1_500_000);
        var extras = CreateFile(Path.Combine("Films", "making-of.mkv"), 1_200_000);
        var subtitles = CreateFile(Path.Combine("Films", "subtitles.srt"), 40_000);
        ShareItem[] items = [new("forest.mkv", 1_500_000, film), new("Extras/making-of.mkv", 1_200_000, extras), new("subtitles.srt", 40_000, subtitles)];
        var (sender, _) = await StartDeviceAsync("sender");
        var (receiver, receiverPaths) = await StartDeviceAsync("receiver");
        // Slow enough that the choice is made long before the files are all there.
        await sender.SetSpeedLimitsAsync(null, 300_000);
        var shareId = ShareId.New();
        await OfferAsync(sender, shareId, "Nature films", items);
        var invite = (await sender.GetInviteAsync(shareId))!;
        var id = TransferId.New();
        var done = WhenFinished(receiver, id);
        Assert.True(await receiver.ReceiveAsync(id, invite));
        await receiver.ConnectAsync(id, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));

        var files = await FilesOfAsync(receiver, id);
        Assert.Equal(["Extras/making-of.mkv", "forest.mkv", "subtitles.srt"], files.Select(f => f.Path).Order(StringComparer.Ordinal));
        Assert.All(files, f => Assert.True(f.Wanted));
        var skipped = files.Single(f => f.Path == "Extras/making-of.mkv").Index;
        await receiver.SetSkippedFilesAsync(id, [skipped]);

        Assert.False(receiver.GetReceiveFiles(id)!.Single(f => f.Index == skipped).Wanted);
        var result = await done.Task.WaitAsync(Timeout);

        Assert.True(result.Done);
        Assert.Equal(1_540_000, result.SizeBytes);
        var received = Path.Combine(receiverPaths.ReceivedRoot, "Nature films");
        Assert.Equal(Checksum(film), Checksum(Path.Combine(received, "forest.mkv")));
        Assert.Equal(Checksum(subtitles), Checksum(Path.Combine(received, "subtitles.srt")));
        Assert.False(File.Exists(Path.Combine(received, "Extras", "making-of.mkv")));
        // Nothing of the file left out stays behind, not even in the hidden folder of unfinished files.
        Assert.Empty(Directory.GetDirectories(Path.Combine(receiverPaths.ReceivedRoot, ".in-progress")));
    }

    [Fact]
    public async Task A_download_kept_shared_is_sent_on_from_the_Received_folder_until_sharing_stops()
    {
        var original = CreateFile("documentary.mkv", 800_000);
        var (sender, _) = await StartDeviceAsync("sender");
        var (receiver, receiverPaths) = await StartDeviceAsync("receiver");
        var (third, thirdPaths) = await StartDeviceAsync("third");
        var shareId = ShareId.New();
        await OfferAsync(sender, shareId, "documentary.mkv", file: original);
        var invite = (await sender.GetInviteAsync(shareId))!;
        var id = TransferId.New();
        var done = WhenFinished(receiver, id);
        Assert.True(await receiver.ReceiveAsync(id, invite));
        await receiver.SetSharingWhenDoneAsync(id, true);
        await receiver.ConnectAsync(id, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));

        Assert.True((await done.Task.WaitAsync(Timeout)).Done);

        var received = Path.Combine(receiverPaths.ReceivedRoot, "documentary.mkv");
        Assert.Equal(Checksum(original), Checksum(received));
        Assert.True(receiver.IsSharingReceived(id));
        // The first computer stops; the third gets all of it from the one that received it.
        await sender.StopOfferingAsync(shareId);
        var thirdId = TransferId.New();
        var thirdDone = WhenFinished(third, thirdId);
        Assert.True(await third.ReceiveAsync(thirdId, invite));
        await third.ConnectAsync(thirdId, new IPEndPoint(IPAddress.Loopback, receiver.ListenEndPoint!.Port));
        Assert.True((await thirdDone.Task.WaitAsync(Timeout)).Done);
        Assert.Equal(Checksum(original), Checksum(Path.Combine(thirdPaths.ReceivedRoot, "documentary.mkv")));
        Assert.True(receiver.GetActivity().Transfers[id].BytesSent >= 800_000);

        await receiver.SetSharingWhenDoneAsync(id, false);

        Assert.False(receiver.IsSharingReceived(id));
        Assert.False(receiver.GetActivity().Transfers.ContainsKey(id));
        // The file stays, and can be deleted at once.
        Assert.Equal(Checksum(original), Checksum(received));
        File.Delete(received);
        Assert.Empty(Directory.GetDirectories(Path.Combine(receiverPaths.ReceivedRoot, ".in-progress")));
    }

    [Fact]
    public async Task A_download_file_says_its_name_and_size_and_anything_else_says_nothing()
    {
        var original = CreateFile("song.mp3", 50_000);
        var (sender, _) = await StartDeviceAsync("sender");
        var shareId = ShareId.New();
        await OfferAsync(sender, shareId, "song.mp3", file: original);
        var downloadFile = (await sender.GetDownloadFileAsync(shareId))!;

        var summary = sender.DescribeDownloadFile(downloadFile.Content);

        Assert.Equal(new InviteSummary("song.mp3", 50_000), summary);
        Assert.Null(sender.DescribeDownloadFile("not a download file"u8.ToArray()));
    }

    [Theory]
    [InlineData("magnet:?xt=urn:btih:4123456789abcdef0123456789abcdef01234567&dn=film", "magnet:?xt=urn:btih:4123456789abcdef0123456789abcdef01234567&dn=film")]
    [InlineData(@"C:\Users\Ana\film.torrent", null)]
    [InlineData("--background", null)]
    public void A_download_link_is_found_in_what_Windows_opened_the_app_with(string argument, string? link) =>
        Assert.Equal(link, MonoTorrentTransferEngine.FindLinkIn(argument));
}
