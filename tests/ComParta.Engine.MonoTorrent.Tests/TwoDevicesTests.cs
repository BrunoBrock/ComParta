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

    private async Task<(MonoTorrentTransferEngine Engine, Paths Paths)> StartDeviceAsync(string name)
    {
        var paths = new Paths(Path.Combine(_folder, name));
        var engine = new MonoTorrentTransferEngine(paths);
        _engines.Add(engine);
        await engine.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort() });
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
        Assert.False(await engine.ReceiveAsync(TransferId.New(), text));
    }
}
