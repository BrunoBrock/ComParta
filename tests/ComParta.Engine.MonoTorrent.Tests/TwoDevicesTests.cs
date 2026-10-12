using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using ComParta.Core;
using ComParta.Core.Ports;

namespace ComParta.Engine.MonoTorrent.Tests;

/// <summary>
/// Two real engines on this machine: one offers a file, the other receives it from the invite.
/// Discovery is closed, so the test connects them by hand.
/// </summary>
public sealed class TwoDevicesTests : IAsyncLifetime
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
    }

    private async Task<(MonoTorrentTransferEngine Engine, Paths Paths)> StartDeviceAsync(
        string name, bool allowDownloadLinks = false, bool acceptIncoming = true, int sendRateLimit = 0, FullDiskWriter[]? disk = null)
    {
        var paths = new Paths(Path.Combine(_folder, name));
        var engine = new MonoTorrentTransferEngine(paths)
        {
            AcceptIncoming = acceptIncoming,
            SendRateLimit = sendRateLimit,
            WrapWriter = disk is null ? writer => writer : writer => disk[0] = new FullDiskWriter(writer) { Full = true },
        };
        _engines.Add(engine);
        await engine.StartAsync(SharingPolicy.Closed with
        {
            ListenPort = FreePort(),
            AllowServersInDownloadLinks = allowDownloadLinks,
        });
        return (engine, paths);
    }

    private static TaskCompletionSource<ReceiveProgress> WhenFinished(MonoTorrentTransferEngine receiver)
    {
        var done = new TaskCompletionSource<ReceiveProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.ReceiveProgressed += progress =>
        {
            if (progress.Done || progress.Failed)
                done.TrySetResult(progress);
        };
        return done;
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private string CreateFile(string name, int sizeBytes)
    {
        var folder = Path.Combine(_folder, "originals");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(sizeBytes));
        return path;
    }

    private static string Checksum(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static async Task<OfferState> OfferAndWaitAsync(MonoTorrentTransferEngine engine, ShareId id, string path)
    {
        var outcome = new TaskCompletionSource<OfferState>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(ShareId changed, OfferState state)
        {
            if (changed == id && state != OfferState.Preparing)
                outcome.TrySetResult(state);
        }
        engine.OfferChanged += OnChanged;
        try
        {
            await engine.OfferAsync(id, path);
            return await outcome.Task.WaitAsync(Timeout);
        }
        finally { engine.OfferChanged -= OnChanged; }
    }

    [Fact]
    public async Task A_file_offered_on_one_device_is_received_on_another_from_the_invite()
    {
        var original = CreateFile("family video.mp4", 3 * 1024 * 1024 + 123);
        var checksumBefore = Checksum(original);
        var (sender, _) = await StartDeviceAsync("sender");
        var (receiver, receiverPaths) = await StartDeviceAsync("receiver");

        var shareId = ShareId.New();
        Assert.Equal(OfferState.Ready, await OfferAndWaitAsync(sender, shareId, original));
        var invite = await sender.GetInviteAsync(shareId);
        Assert.NotNull(invite);

        var done = new TaskCompletionSource<ReceiveProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.ReceiveProgressed += progress =>
        {
            if (progress.Done || progress.Failed)
                done.TrySetResult(progress);
        };
        var transferId = TransferId.New();
        Assert.Equal("family video.mp4", receiver.DescribeInvite(invite)!.Name);
        Assert.True(await receiver.ReceiveAsync(transferId, invite));
        Assert.Equal(receiver.ReadInviteKey(invite), receiver.GetReceiveKey(transferId));
        await receiver.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));

        var result = await done.Task.WaitAsync(Timeout);

        Assert.True(result.Done);
        Assert.Equal("family video.mp4", result.Name);
        var received = Path.Combine(receiverPaths.ReceivedRoot, "family video.mp4");
        Assert.Equal(checksumBefore, Checksum(received));
        Assert.Equal(checksumBefore, Checksum(original));
        // Nothing unfinished is left behind, and the engine has let go of the transfer.
        Assert.Empty(Directory.GetDirectories(Path.Combine(receiverPaths.ReceivedRoot, ".in-progress")));
        Assert.Null(receiver.GetReceiveKey(transferId));
    }

    [Fact]
    public async Task An_invite_that_carries_the_senders_address_needs_no_discovery()
    {
        var original = CreateFile("album.zip", 500_000);
        var withAddress = SharingPolicy.Closed with { AllowAddressInInvite = true };
        var senderPaths = new Paths(Path.Combine(_folder, "sender"));
        var sender = new MonoTorrentTransferEngine(senderPaths);
        _engines.Add(sender);
        await sender.StartAsync(withAddress);
        var receiverPaths = new Paths(Path.Combine(_folder, "receiver"));
        var receiver = new MonoTorrentTransferEngine(receiverPaths);
        _engines.Add(receiver);
        await receiver.StartAsync(withAddress);

        var shareId = ShareId.New();
        Assert.Equal(OfferState.Ready, await OfferAndWaitAsync(sender, shareId, original));
        var invite = (await sender.GetInviteAsync(shareId))!;
        Assert.Contains("&x.pe=", invite);
        // The chosen port is remembered, so the invite stays good after a restart.
        Assert.EndsWith(":" + File.ReadAllText(Path.Combine(senderPaths.EngineRoot, "port.txt")), invite);

        var done = new TaskCompletionSource<ReceiveProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.ReceiveProgressed += progress =>
        {
            if (progress.Done || progress.Failed)
                done.TrySetResult(progress);
        };
        Assert.True(await receiver.ReceiveAsync(TransferId.New(), invite));

        var result = await done.Task.WaitAsync(Timeout);

        Assert.True(result.Done);
        Assert.Equal(Checksum(original), Checksum(Path.Combine(receiverPaths.ReceivedRoot, "album.zip")));
    }

    [Fact]
    public async Task Without_the_policy_an_invite_carries_no_address()
    {
        var original = CreateFile("private.txt", 10_000);
        var (sender, _) = await StartDeviceAsync("sender");
        var shareId = ShareId.New();
        await OfferAndWaitAsync(sender, shareId, original);

        Assert.DoesNotContain("x.pe", await sender.GetInviteAsync(shareId));
    }

    [Fact]
    public async Task A_received_file_never_overwrites_one_with_the_same_name()
    {
        var original = CreateFile("photo.jpg", 200_000);
        var (sender, _) = await StartDeviceAsync("sender");
        var (receiver, receiverPaths) = await StartDeviceAsync("receiver");
        Directory.CreateDirectory(receiverPaths.ReceivedRoot);
        var existing = Path.Combine(receiverPaths.ReceivedRoot, "photo.jpg");
        File.WriteAllText(existing, "an older photo");

        var shareId = ShareId.New();
        await OfferAndWaitAsync(sender, shareId, original);
        var done = new TaskCompletionSource<ReceiveProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.ReceiveProgressed += progress =>
        {
            if (progress.Done || progress.Failed)
                done.TrySetResult(progress);
        };
        var transferId = TransferId.New();
        await receiver.ReceiveAsync(transferId, (await sender.GetInviteAsync(shareId))!);
        await receiver.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));

        var result = await done.Task.WaitAsync(Timeout);

        Assert.Equal("photo (2).jpg", result.Name);
        Assert.Equal("an older photo", File.ReadAllText(existing));
        Assert.Equal(Checksum(original), Checksum(Path.Combine(receiverPaths.ReceivedRoot, "photo (2).jpg")));
    }

    [Fact]
    public async Task Stopping_an_offer_leaves_the_engine_empty_and_the_file_unchanged()
    {
        var original = CreateFile("notes.txt", 50_000);
        var checksumBefore = Checksum(original);
        var (engine, paths) = await StartDeviceAsync("sender");
        var shareId = ShareId.New();
        Assert.Equal(OfferState.Ready, await OfferAndWaitAsync(engine, shareId, original));

        await engine.StopOfferingAsync(shareId);

        Assert.Null(await engine.GetInviteAsync(shareId));
        Assert.Empty(Directory.GetFiles(Path.Combine(paths.EngineRoot, "shares")));
        Assert.Equal(checksumBefore, Checksum(original));
        // The file is free again: nothing holds it open.
        using var stream = new FileStream(original, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task A_file_changed_after_it_was_shared_is_described_again_and_never_written_to()
    {
        var original = CreateFile("letter.txt", 80_000);
        var (first, paths) = await StartDeviceAsync("sender");
        var shareId = ShareId.New();
        await OfferAndWaitAsync(first, shareId, original);
        await first.StopAsync();

        File.WriteAllBytes(original, RandomNumberGenerator.GetBytes(80_000));
        var checksumChanged = Checksum(original);
        var second = new MonoTorrentTransferEngine(paths);
        _engines.Add(second);
        await second.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort() });

        Assert.Equal(OfferState.Ready, await OfferAndWaitAsync(second, shareId, original));
        Assert.Equal(checksumChanged, Checksum(original));
    }

    [Fact]
    public async Task Cancelling_a_transfer_deletes_its_unfinished_data()
    {
        var (receiver, paths) = await StartDeviceAsync("receiver");
        var transferId = TransferId.New();
        Assert.True(await receiver.ReceiveAsync(transferId, "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567"));
        Assert.True(Directory.Exists(Path.Combine(paths.ReceivedRoot, ".in-progress", transferId.ToString())));

        await receiver.StopReceivingAsync(transferId);

        Assert.False(Directory.Exists(Path.Combine(paths.ReceivedRoot, ".in-progress", transferId.ToString())));
        Assert.Null(receiver.GetReceiveKey(transferId));
        Assert.False(await receiver.ResumeReceivingAsync(transferId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello grandma")]
    [InlineData("https://example.com/video.mp4")]
    [InlineData("magnet:?dn=no-identity")]
    public async Task Text_that_is_not_an_invite_is_not_accepted(string text)
    {
        var (engine, _) = await StartDeviceAsync("receiver");

        Assert.Null(engine.ReadInviteKey(text));
        Assert.Null(engine.DescribeInvite(text));
        Assert.False(await engine.ReceiveAsync(TransferId.New(), text));
    }

    [Fact]
    public async Task A_download_file_is_received_on_another_device()
    {
        var original = CreateFile("holiday.mov", 1024 * 1024 + 7);
        var (sender, senderPaths) = await StartDeviceAsync("sender");
        var (receiver, receiverPaths) = await StartDeviceAsync("receiver", allowDownloadLinks: true);
        var shareId = ShareId.New();
        Assert.Equal(OfferState.Ready, await OfferAndWaitAsync(sender, shareId, original));
        var downloadFile = File.ReadAllBytes(Path.Combine(senderPaths.EngineRoot, "shares", shareId + ".torrent"));

        var done = WhenFinished(receiver);
        var transferId = TransferId.New();
        Assert.Equal(receiver.ReadInviteKey((await sender.GetInviteAsync(shareId))!), receiver.ReadDownloadFileKey(downloadFile));
        Assert.True(await receiver.ReceiveFromFileAsync(transferId, downloadFile));
        await receiver.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));

        var result = await done.Task.WaitAsync(Timeout);

        Assert.True(result.Done);
        Assert.Equal(Checksum(original), Checksum(Path.Combine(receiverPaths.ReceivedRoot, "holiday.mov")));
        // The saved copy of the download file is gone once the transfer is finished.
        Assert.Empty(Directory.GetFiles(Path.Combine(receiverPaths.EngineRoot, "transfers"), "*.torrent"));
    }

    [Fact]
    public async Task Without_the_policy_a_download_file_is_refused()
    {
        var original = CreateFile("notes.txt", 20_000);
        var (sender, senderPaths) = await StartDeviceAsync("sender");
        var (receiver, _) = await StartDeviceAsync("receiver");
        var shareId = ShareId.New();
        await OfferAndWaitAsync(sender, shareId, original);
        var downloadFile = File.ReadAllBytes(Path.Combine(senderPaths.EngineRoot, "shares", shareId + ".torrent"));

        Assert.Null(receiver.ReadDownloadFileKey(downloadFile));
        Assert.False(await receiver.ReceiveFromFileAsync(TransferId.New(), downloadFile));
    }

    [Fact]
    public async Task A_file_that_is_not_a_download_file_is_refused()
    {
        var (receiver, _) = await StartDeviceAsync("receiver", allowDownloadLinks: true);
        var notADownloadFile = "hello grandma"u8.ToArray();

        Assert.Null(receiver.ReadDownloadFileKey(notADownloadFile));
        Assert.False(await receiver.ReceiveFromFileAsync(TransferId.New(), notADownloadFile));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_public_link_keeps_its_servers_only_when_the_policy_allows(bool allow)
    {
        var (receiver, paths) = await StartDeviceAsync("receiver", allowDownloadLinks: allow);
        var transferId = TransferId.New();
        const string link = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=film"
            + "&tr=http%3A%2F%2F127.0.0.1%3A1%2Fannounce";

        Assert.True(await receiver.ReceiveAsync(transferId, link));

        var saved = File.ReadAllText(Path.Combine(paths.EngineRoot, "transfers", transferId + ".invite"));
        Assert.Equal(allow, saved.Contains("tr=", StringComparison.Ordinal));
        await receiver.StopReceivingAsync(transferId);
    }

    [Fact]
    public async Task A_paused_transfer_waits_and_finishes_after_continuing()
    {
        var original = CreateFile("wedding.mp4", 2 * 1024 * 1024);
        var (sender, _) = await StartDeviceAsync("sender");
        var (receiver, receiverPaths) = await StartDeviceAsync("receiver");
        var shareId = ShareId.New();
        Assert.Equal(OfferState.Ready, await OfferAndWaitAsync(sender, shareId, original));
        var invite = (await sender.GetInviteAsync(shareId))!;

        var done = WhenFinished(receiver);
        var transferId = TransferId.New();
        Assert.True(await receiver.ReceiveAsync(transferId, invite));
        await receiver.PauseReceivingAsync(transferId);
        var senderAddress = new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port);
        await receiver.ConnectAsync(transferId, senderAddress);

        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.False(done.Task.IsCompleted);
        Assert.False(File.Exists(Path.Combine(receiverPaths.ReceivedRoot, "wedding.mp4")));

        await receiver.ContinueReceivingAsync(transferId);
        await receiver.ConnectAsync(transferId, senderAddress);
        var result = await done.Task.WaitAsync(Timeout);

        Assert.True(result.Done);
        Assert.Equal(Checksum(original), Checksum(Path.Combine(receiverPaths.ReceivedRoot, "wedding.mp4")));
    }

    [Fact]
    public async Task An_idle_engine_reports_listening_and_no_router_mapping_under_the_closed_policy()
    {
        var (engine, _) = await StartDeviceAsync("idle");

        var facts = await WaitForAsync(engine, f => f.Listening);

        Assert.True(facts.Listening);
        Assert.False(facts.PortMapped);
        Assert.Null(facts.MappedPort);
        Assert.Null(facts.LastIncomingAt);
    }

    [Fact]
    public async Task A_sender_nobody_can_reach_still_delivers_by_connecting_out_to_the_receiver()
    {
        var original = CreateFile("holiday.mp4", 700_000);
        var (sender, _) = await StartDeviceAsync("sender", acceptIncoming: false);
        var (receiver, receiverPaths) = await StartDeviceAsync("receiver");
        Assert.Null(sender.ListenEndPoint);
        Assert.False(sender.GetReachability().Listening);

        var shareId = ShareId.New();
        Assert.Equal(OfferState.Ready, await OfferAndWaitAsync(sender, shareId, original));
        var done = WhenFinished(receiver);
        Assert.True(await receiver.ReceiveAsync(TransferId.New(), (await sender.GetInviteAsync(shareId))!));

        await sender.ConnectOfferAsync(shareId, new IPEndPoint(IPAddress.Loopback, receiver.ListenEndPoint!.Port));
        var result = await done.Task.WaitAsync(Timeout);

        Assert.True(result.Done);
        Assert.Equal(Checksum(original), Checksum(Path.Combine(receiverPaths.ReceivedRoot, "holiday.mp4")));
        // The receiver saw someone connect in, which is what makes its connection "good".
        Assert.NotNull(receiver.GetReachability().LastIncomingAt);
        Assert.Null(sender.GetReachability().LastIncomingAt);
    }

    [Fact]
    public async Task Two_devices_connect_over_IPv6_using_only_the_address_in_the_invite()
    {
        if (!CanUseIPv6Loopback())
            return; // This machine has no IPv6; Windows CI runs it.

        var original = CreateFile("ipv6.bin", 300_000);
        var policy = SharingPolicy.Closed with { AllowAddressInInvite = true };
        var sender = new MonoTorrentTransferEngine(new Paths(Path.Combine(_folder, "sender")));
        _engines.Add(sender);
        await sender.StartAsync(policy with { ListenPort = FreePort() });
        var receiverPaths = new Paths(Path.Combine(_folder, "receiver"));
        var receiver = new MonoTorrentTransferEngine(receiverPaths);
        _engines.Add(receiver);
        await receiver.StartAsync(policy with { ListenPort = FreePort() });

        var shareId = ShareId.New();
        Assert.Equal(OfferState.Ready, await OfferAndWaitAsync(sender, shareId, original));
        // Only the IPv6 loopback address, so nothing else can carry the transfer.
        var invite = (await sender.GetInviteAsync(shareId))!.Split("&x.pe=")[0]
            + $"&x.pe={Uri.EscapeDataString(new IPEndPoint(IPAddress.IPv6Loopback, sender.ListenEndPoint!.Port).ToString())}";
        var done = WhenFinished(receiver);
        Assert.True(await receiver.ReceiveAsync(TransferId.New(), invite));

        var result = await done.Task.WaitAsync(Timeout);

        Assert.True(result.Done);
        Assert.Equal(Checksum(original), Checksum(Path.Combine(receiverPaths.ReceivedRoot, "ipv6.bin")));
    }

    [Fact]
    public async Task An_invite_address_that_is_no_use_is_dropped()
    {
        var (receiver, _) = await StartDeviceAsync("receiver");
        var magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";

        Assert.True(await receiver.ReceiveAsync(TransferId.New(), magnet + "&x.pe=0.0.0.0:80&x.pe=239.1.2.3:80&x.pe=[ff02::1]:80"));
    }

    [Fact]
    public async Task Both_sides_report_the_bytes_moved_for_the_item()
    {
        var original = CreateFile("concert.mp4", 600_000);
        var (sender, _) = await StartDeviceAsync("sender");
        var (receiver, _) = await StartDeviceAsync("receiver");
        var shareId = ShareId.New();
        await OfferAndWaitAsync(sender, shareId, original);
        var transferId = TransferId.New();
        var activityRaised = 0;
        sender.ActivityChanged += () => Interlocked.Increment(ref activityRaised);

        await receiver.ReceiveAsync(transferId, (await sender.GetInviteAsync(shareId))!);
        // Read just before the transfer finishes and leaves the engine.
        ItemActivity? received = null;
        receiver.ReceiveProgressed += progress =>
        {
            if (progress.Id == transferId && !progress.Done && progress.ReceivedBytes > 0)
                received = receiver.GetActivity().Transfers.GetValueOrDefault(transferId) ?? received;
        };
        var done = WhenFinished(receiver);
        await receiver.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));
        Assert.True((await done.Task.WaitAsync(Timeout)).Done);

        var sent = sender.GetActivity().Shares[shareId];
        Assert.True(sent.BytesSent >= 600_000, $"sent {sent.BytesSent}");
        Assert.Equal(0, sent.BytesReceived);
        Assert.Empty(sender.GetActivity().Transfers);
        Assert.True(activityRaised > 0);
        if (received is not null)
            Assert.True(received.BytesReceived > 0);
    }

    [Fact]
    public async Task The_sender_counts_a_device_getting_the_file_and_then_one_that_finished()
    {
        var original = CreateFile("holiday.mp4", 3 * 1024 * 1024);
        var (sender, _) = await StartDeviceAsync("sender", sendRateLimit: 1024 * 1024);
        var (receiver, _) = await StartDeviceAsync("receiver");
        var shareId = ShareId.New();
        await OfferAndWaitAsync(sender, shareId, original);
        var seen = new List<ItemActivity>();
        sender.ActivityChanged += () =>
        {
            if (sender.GetActivity().Shares.GetValueOrDefault(shareId) is { } activity)
                lock (seen)
                    seen.Add(activity);
        };

        var transferId = TransferId.New();
        await receiver.ReceiveAsync(transferId, (await sender.GetInviteAsync(shareId))!);
        var done = WhenFinished(receiver);
        // Connected again every few seconds while it runs, as an invite's addresses are retried,
        // because the limited sending makes this transfer take a while.
        var senderAddress = new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port);
        var until = DateTime.UtcNow + Timeout;
        while (!done.Task.IsCompleted && DateTime.UtcNow < until)
        {
            try { await receiver.ConnectAsync(transferId, senderAddress); }
            catch (KeyNotFoundException) { /* finished meanwhile */ }
            await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        }
        Assert.True((await done.Task.WaitAsync(Timeout)).Done);
        until = DateTime.UtcNow + Timeout;
        while (sender.GetActivity().Shares[shareId] is { Finished: 0 } or { Receivers: > 0 } && DateTime.UtcNow < until)
            await Task.Delay(100);

        var after = sender.GetActivity().Shares[shareId];
        Assert.Equal(1, after.Finished);
        Assert.Equal(0, after.Receivers);
        List<ItemActivity> during;
        lock (seen)
            during = seen.Where(a => a.Receivers > 0).ToList();
        Assert.NotEmpty(during);
        Assert.All(during, a => Assert.Equal(1, a.Receivers));
        Assert.Contains(during, a => a.ReceiversPercent is > 0 and < 100);
        Assert.True(during.Select(a => a.ReceiversPercent).SequenceEqual(during.Select(a => a.ReceiversPercent).Order()), "progress only rises");
    }

    [Fact]
    public async Task A_full_disk_stops_the_transfer_with_its_own_report_and_it_finishes_once_there_is_room()
    {
        var original = CreateFile("family album.zip", 2 * 1024 * 1024);
        var (sender, _) = await StartDeviceAsync("sender");
        var disk = new FullDiskWriter[1];
        var (receiver, receiverPaths) = await StartDeviceAsync("receiver", disk: disk);
        var shareId = ShareId.New();
        await OfferAndWaitAsync(sender, shareId, original);
        var transferId = TransferId.New();
        var noSpace = new TaskCompletionSource<ReceiveProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.ReceiveProgressed += progress =>
        {
            if (progress.NoSpace)
                noSpace.TrySetResult(progress);
        };
        var done = WhenFinished(receiver);
        await receiver.ReceiveAsync(transferId, (await sender.GetInviteAsync(shareId))!);
        var senderAddress = new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port);
        await receiver.ConnectAsync(transferId, senderAddress);

        var full = await noSpace.Task.WaitAsync(Timeout);
        Assert.False(full.Failed);
        Assert.Equal(2 * 1024 * 1024, full.SizeBytes);
        Assert.False(done.Task.IsCompleted);

        disk[0].Full = false;
        await receiver.ContinueReceivingAsync(transferId);
        await receiver.ConnectAsync(transferId, senderAddress);

        Assert.True((await done.Task.WaitAsync(Timeout)).Done);
        Assert.Equal(Checksum(original), Checksum(Path.Combine(receiverPaths.ReceivedRoot, "family album.zip")));
    }

    [Fact]
    public async Task A_transfer_continued_in_a_new_run_reports_what_it_already_has_at_once()
    {
        var original = CreateFile("long film.mp4", 4 * 1024 * 1024);
        var (sender, _) = await StartDeviceAsync("sender", sendRateLimit: 1024 * 1024);
        var shareId = ShareId.New();
        await OfferAndWaitAsync(sender, shareId, original);
        var paths = new Paths(Path.Combine(_folder, "receiver"));
        var first = new MonoTorrentTransferEngine(paths);
        await first.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort() });
        var halfWay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.ReceiveProgressed += progress =>
        {
            if (progress.SizeBytes is { } size && progress.ReceivedBytes > size / 2)
                halfWay.TrySetResult();
        };
        var transferId = TransferId.New();
        await first.ReceiveAsync(transferId, (await sender.GetInviteAsync(shareId))!);
        await first.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));
        await halfWay.Task.WaitAsync(Timeout);
        await first.StopAsync();

        // The next run, with the sender gone: whatever it reports comes from what is on disk.
        await sender.StopOfferingAsync(shareId);
        var second = new MonoTorrentTransferEngine(paths);
        _engines.Add(second);
        await second.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort() });
        var firstReport = new TaskCompletionSource<ReceiveProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        second.ReceiveProgressed += progress =>
        {
            if (progress.Id == transferId)
                firstReport.TrySetResult(progress);
        };
        Assert.True(await second.ResumeReceivingAsync(transferId));

        var report = await firstReport.Task.WaitAsync(Timeout);
        Assert.Equal(4 * 1024 * 1024, report.SizeBytes);
        Assert.True(report.ReceivedBytes > report.SizeBytes * 0.4, $"first report {report.ReceivedBytes}");
    }

    [Fact]
    public async Task While_sending_is_stopped_nothing_arrives_and_after_continue_the_file_does()
    {
        var original = CreateFile("paused.bin", 400_000);
        var (sender, _) = await StartDeviceAsync("sender");
        var (receiver, receiverPaths) = await StartDeviceAsync("receiver");
        var shareId = ShareId.New();
        await OfferAndWaitAsync(sender, shareId, original);
        await sender.SetStoppedAsync(sending: true, receiving: false);

        var transferId = TransferId.New();
        var done = WhenFinished(receiver);
        await receiver.ReceiveAsync(transferId, (await sender.GetInviteAsync(shareId))!);
        await receiver.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.False(done.Task.IsCompleted);
        Assert.Equal(0, sender.GetActivity().Shares[shareId].BytesSent);

        await sender.SetStoppedAsync(sending: false, receiving: false);
        // The receiver's refused attempt waits out the engine's retry delay, so the sender, started
        // again, connects out, as it does when the lookup network tells it where the receiver is.
        await sender.ConnectOfferAsync(shareId, new IPEndPoint(IPAddress.Loopback, receiver.ListenEndPoint!.Port));

        Assert.True((await done.Task.WaitAsync(Timeout)).Done);
        Assert.Equal(Checksum(original), Checksum(Path.Combine(receiverPaths.ReceivedRoot, "paused.bin")));
    }

    [Fact]
    public async Task A_file_shared_while_sending_is_stopped_waits_for_continue()
    {
        var original = CreateFile("later.bin", 300_000);
        var (sender, _) = await StartDeviceAsync("sender");
        var (receiver, receiverPaths) = await StartDeviceAsync("receiver");
        await sender.SetStoppedAsync(sending: true, receiving: false);
        var shareId = ShareId.New();
        await OfferAndWaitAsync(sender, shareId, original);

        var transferId = TransferId.New();
        var done = WhenFinished(receiver);
        await receiver.ReceiveAsync(transferId, (await sender.GetInviteAsync(shareId))!);
        await receiver.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.False(done.Task.IsCompleted);

        await sender.SetStoppedAsync(sending: false, receiving: false);
        // The receiver's refused attempt waits out the engine's retry delay, so the sender, started
        // again, connects out, as it does when the lookup network tells it where the receiver is.
        await sender.ConnectOfferAsync(shareId, new IPEndPoint(IPAddress.Loopback, receiver.ListenEndPoint!.Port));

        Assert.True((await done.Task.WaitAsync(Timeout)).Done);
        Assert.Equal(Checksum(original), Checksum(Path.Combine(receiverPaths.ReceivedRoot, "later.bin")));
    }

    [Fact]
    public async Task A_transfer_paused_on_its_own_stays_paused_after_receiving_continues()
    {
        var (receiver, _) = await StartDeviceAsync("receiver");
        var mine = TransferId.New();
        var other = TransferId.New();
        Assert.True(await receiver.ReceiveAsync(mine, "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567"));
        Assert.True(await receiver.ReceiveAsync(other, "magnet:?xt=urn:btih:1123456789abcdef0123456789abcdef01234567"));
        await receiver.PauseReceivingAsync(mine);

        await receiver.SetStoppedAsync(sending: false, receiving: true);
        // Continuing one transfer while everything is stopped does not start it.
        await receiver.ContinueReceivingAsync(other);
        Assert.Equal(global::MonoTorrent.Client.TorrentState.Stopped, receiver.StateOf(other));
        await receiver.PauseReceivingAsync(other);
        await receiver.ContinueReceivingAsync(other);

        await receiver.SetStoppedAsync(sending: false, receiving: false);

        Assert.Equal(global::MonoTorrent.Client.TorrentState.Stopped, receiver.StateOf(mine));
        Assert.NotEqual(global::MonoTorrent.Client.TorrentState.Stopped, receiver.StateOf(other));
    }

    [Fact]
    public async Task A_transfer_added_while_receiving_is_stopped_waits()
    {
        var (receiver, _) = await StartDeviceAsync("receiver");
        await receiver.SetStoppedAsync(sending: false, receiving: true);
        var id = TransferId.New();

        Assert.True(await receiver.ReceiveAsync(id, "magnet:?xt=urn:btih:2123456789abcdef0123456789abcdef01234567"));
        Assert.Equal(global::MonoTorrent.Client.TorrentState.Stopped, receiver.StateOf(id));

        await receiver.SetStoppedAsync(sending: false, receiving: false);
        Assert.NotEqual(global::MonoTorrent.Client.TorrentState.Stopped, receiver.StateOf(id));
    }

    private static async Task<ReachabilityFacts> WaitForAsync(MonoTorrentTransferEngine engine, Func<ReachabilityFacts, bool> condition)
    {
        var until = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < until)
        {
            var facts = engine.GetReachability();
            if (condition(facts))
                return facts;
            await Task.Delay(100);
        }
        return engine.GetReachability();
    }

    private static bool CanUseIPv6Loopback()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
