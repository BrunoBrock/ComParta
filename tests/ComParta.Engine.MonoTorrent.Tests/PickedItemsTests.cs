using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using ComParta.Core;
using ComParta.Core.Ports;

namespace ComParta.Engine.MonoTorrent.Tests;

/// <summary>
/// Sharing items that have no path, as a phone's picker hands them over (epic 7 story 2, AD-20): the
/// engine reads them only through the content source and never writes them.
/// </summary>
public sealed class PickedItemsTests : IAsyncLifetime
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

    /// <summary>Items as a phone's picker gives them: a reference, a name and bytes, and no path.</summary>
    private sealed class PickedContent : IContentSource
    {
        public ConcurrentDictionary<string, (string Name, byte[] Bytes)> Items { get; } = new();
        public int Opened;

        public string Add(string name, int sizeBytes)
        {
            var reference = $"content://com.android.providers.media.documents/document/{Items.Count + 1}";
            Items[reference] = (name, RandomNumberGenerator.GetBytes(sizeBytes));
            return reference;
        }

        public bool Exists(string contentRef) => Items.ContainsKey(contentRef);
        public long Length(string contentRef) => Items.TryGetValue(contentRef, out var item) ? item.Bytes.Length : throw new FileNotFoundException();
        public string NameOf(string contentRef) => Items[contentRef].Name;

        public Stream OpenRead(string contentRef)
        {
            Interlocked.Increment(ref Opened);
            return Items.TryGetValue(contentRef, out var item) ? new MemoryStream(item.Bytes, writable: false) : throw new FileNotFoundException();
        }
    }

    private async Task<(MonoTorrentTransferEngine Engine, Paths Paths)> StartDeviceAsync(string name, IContentSource? content = null)
    {
        var paths = new Paths(Path.Combine(_folder, name));
        var engine = content is null ? new MonoTorrentTransferEngine(paths) : new MonoTorrentTransferEngine(paths) { Content = content };
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

    private static async Task<OfferState> WhenOfferedAsync(MonoTorrentTransferEngine engine, ShareId id, Func<Task> offer)
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
            await offer();
            return await outcome.Task.WaitAsync(Timeout);
        }
        finally { engine.OfferChanged -= OnChanged; }
    }

    private static async Task<ReceiveProgress> ReceiveAsync(MonoTorrentTransferEngine sender, MonoTorrentTransferEngine receiver, ShareId shareId)
    {
        var done = new TaskCompletionSource<ReceiveProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.ReceiveProgressed += progress =>
        {
            if (progress.Done || progress.Failed)
                done.TrySetResult(progress);
        };
        var invite = (await sender.GetInviteAsync(shareId))!;
        var transferId = TransferId.New();
        Assert.True(await receiver.ReceiveAsync(transferId, invite));
        await receiver.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));
        return await done.Task.WaitAsync(Timeout);
    }

    private static string Checksum(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Checksum(string path) => Checksum(File.ReadAllBytes(path));

    [Fact]
    public async Task One_picked_video_without_a_path_arrives_byte_identical_under_its_own_name()
    {
        var picked = new PickedContent();
        var video = picked.Add("Aniversário.mp4", 900_000);
        var (sender, senderPaths) = await StartDeviceAsync("phone", picked);
        var (receiver, receiverPaths) = await StartDeviceAsync("pc");
        var shareId = ShareId.New();

        Assert.Equal(OfferState.Ready, await WhenOfferedAsync(sender, shareId, () => sender.OfferAsync(shareId, video)));
        Assert.Equal("Aniversário.mp4", receiver.DescribeInvite((await sender.GetInviteAsync(shareId))!)!.Name);
        var result = await ReceiveAsync(sender, receiver, shareId);

        Assert.True(result.Done);
        Assert.Equal(Checksum(picked.Items[video].Bytes), Checksum(Path.Combine(receiverPaths.ReceivedRoot, "Aniversário.mp4")));
        Assert.True(picked.Opened > 0);
        // Nothing of the phone's is copied into the app's own storage to be offered.
        Assert.False(Directory.Exists(Path.Combine(senderPaths.EngineRoot, "shares", shareId.ToString())));
    }

    [Fact]
    public async Task A_picked_folder_arrives_with_its_structure()
    {
        var picked = new PickedContent();
        ShareItem[] items =
        [
            new("praia.jpg", 400_000, picked.Add("praia.jpg", 400_000)),
            new("dia 2/morro.jpg", 250_123, picked.Add("morro.jpg", 250_123)),
        ];
        var (sender, _) = await StartDeviceAsync("phone", picked);
        var (receiver, receiverPaths) = await StartDeviceAsync("pc");
        var shareId = ShareId.New();

        Assert.Equal(OfferState.Ready, await WhenOfferedAsync(sender, shareId, () => sender.OfferItemsAsync(shareId, "Viagem", items)));
        var result = await ReceiveAsync(sender, receiver, shareId);

        Assert.True(result.Done);
        var received = Path.Combine(receiverPaths.ReceivedRoot, "Viagem");
        Assert.Equal(Checksum(picked.Items[items[0].ContentRef].Bytes), Checksum(Path.Combine(received, "praia.jpg")));
        Assert.Equal(Checksum(picked.Items[items[1].ContentRef].Bytes), Checksum(Path.Combine(received, "dia 2", "morro.jpg")));
    }

    [Fact]
    public async Task A_picked_item_that_is_gone_after_a_restart_is_not_offered()
    {
        var picked = new PickedContent();
        var video = picked.Add("video.mp4", 300_000);
        var paths = new Paths(Path.Combine(_folder, "phone"));
        var first = new MonoTorrentTransferEngine(paths) { Content = picked };
        await first.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort() });
        var shareId = ShareId.New();
        Assert.Equal(OfferState.Ready, await WhenOfferedAsync(first, shareId, () => first.OfferAsync(shareId, video)));
        await first.StopAsync();

        // The person removed the permission, or the item, while the app was closed.
        picked.Items.TryRemove(video, out _);
        var (again, _) = await StartDeviceAsync("phone", picked);

        Assert.Equal(OfferState.Failed, await WhenOfferedAsync(again, shareId, () => again.OfferAsync(shareId, video)));
    }

    [Fact]
    public async Task Files_on_disk_are_still_read_where_they_are_through_the_same_port()
    {
        var original = Path.Combine(_folder, "originals", "notes.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        await File.WriteAllBytesAsync(original, RandomNumberGenerator.GetBytes(80_000));
        var before = Checksum(original);
        var (sender, _) = await StartDeviceAsync("pc");
        var (receiver, receiverPaths) = await StartDeviceAsync("other");
        var shareId = ShareId.New();

        Assert.Equal(OfferState.Ready, await WhenOfferedAsync(sender, shareId, () => sender.OfferAsync(shareId, original)));
        var result = await ReceiveAsync(sender, receiver, shareId);

        Assert.True(result.Done);
        Assert.Equal(before, Checksum(Path.Combine(receiverPaths.ReceivedRoot, "notes.txt")));
        Assert.Equal(before, Checksum(original));
        Assert.Equal([original], Directory.GetFiles(Path.GetDirectoryName(original)!));
    }
}
