using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using ComParta.Contracts;
using ComParta.Core;
using ComParta.Core.Ports;
using ComParta.Core.Tests;
using ComParta.Core.UseCases;
using ComParta.Engine.MonoTorrent;
using ComParta.Shell;
using ComParta.Storage.Sqlite;

namespace ComParta.Wiring.Tests;

/// <summary>
/// Sharing from a phone (epic 7 story 2), through the app wired as on Android: the screen's buttons as
/// JSON commands, the real router, use cases, database and torrent engine, with the phone's picker and
/// storage as stand-ins that hand over items without paths (AD-20). A computer receives what the
/// phone shares, and the phone shares it again after a restart.
/// </summary>
public sealed class PhonePickerTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Quick = TimeSpan.FromSeconds(8);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "comparta-tests", Guid.NewGuid().ToString("N"));
    private readonly FakePhoneStorage _phone = new();
    private readonly FakeFilePicker _picker = new();
    private readonly FakeClipboard _clipboard = new();
    private readonly FakeOpener _opener = new();
    private readonly List<MonoTorrentTransferEngine> _engines = [];
    private FakePaths _paths = null!;
    private Phone _app = null!;

    private sealed class FakeClipboard : IClipboard
    {
        public string? Text { get; private set; }
        public Task SetTextAsync(string text, CancellationToken ct = default) { Text = text; return Task.CompletedTask; }
        public Task<string?> GetTextAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    /// <summary>These tests never open About; the notices are checked in ButtonsTests.</summary>
    private sealed class NoNotices : IThirdPartyNotices
    {
        public Task<string?> ReadAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    /// <summary>The app on the phone: its router and what it showed the screen.</summary>
    private sealed record Phone(BridgeRouter Router, MonoTorrentTransferEngine Engine, ConcurrentQueue<JsonElement> Events);

    public async Task InitializeAsync()
    {
        _paths = new FakePaths(Path.Combine(_folder, "phone"));
        _app = await StartPhoneAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var engine in _engines)
            await engine.StopAsync().WaitAsync(Quick);
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    /// <summary>As in AppServices.StartAsync with the Android parts.</summary>
    private async Task<Phone> StartPhoneAsync()
    {
        Directory.CreateDirectory(_paths.ReceivedRoot);
        var store = await SqliteStore.OpenAsync(_paths.DatabasePath);
        var engine = new MonoTorrentTransferEngine(_paths) { Content = _phone };
        _engines.Add(engine);
        var shares = new ShareUseCases(store, engine, _picker, _phone, paths: _paths);
        var disk = new LocalFileSystem();
        var receivedFolder = new ReceivedFolder(store, _paths, _picker, disk);
        var transfers = new TransferUseCases(store, engine, picker: _picker, files: disk, storage: new FakeStorage(), received: receivedFolder);
        var settings = new SettingsUseCases(store, new FakeSystem("pt-BR"), engine, receivedFolder);
        var connection = new ConnectionUseCases(engine, new FakeFirewall());
        var activity = new ActivityUseCases(engine, store);
        var opener = _opener;
        var received = new ReceivedUseCases(transfers, _paths, opener, disk);
        var thumbnails = new ThumbnailUseCases(shares, received, new FakeThumbnails());
        var sending = new SendingUseCases(engine, shares);
        var notifications = new NotificationUseCases(engine, transfers, shares, sending, settings, new FakeNotifier(), new FakeWindow());
        var background = new BackgroundUseCases(engine, shares, transfers, sending, settings, store, notifications, new FakePresence());
        var router = new BridgeRouter(
            shares, transfers, settings, connection, new OpenedInvites(), activity, received, _clipboard, opener, thumbnails,
            new DroppedFiles(), sending, notifications, background, new MediaUseCases(shares, received, _phone),
            new SupportUseCases(store, new FakeSystem("pt-BR"), settings, shares, transfers, connection),
            new AboutUseCases(new NoNotices()));
        var events = new ConcurrentQueue<JsonElement>();
        router.EventRaised += json => events.Enqueue(JsonDocument.Parse(json).RootElement.Clone());

        await engine.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort() });
        await settings.StartAsync();
        await activity.StartAsync();
        await shares.StartAsync();
        await transfers.StartAsync();
        await notifications.StartAsync();
        await background.StartAsync();
        return new Phone(router, engine, events);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private async Task<JsonElement> ClickAsync(string command, object? args = null)
    {
        var json = await _app.Router.InvokeAsync(command, args is null ? "null" : JsonSerializer.Serialize(args, Contract.Json)).WaitAsync(Quick);
        var result = JsonDocument.Parse(json).RootElement;
        Assert.True(result.GetProperty("ok").GetBoolean(), json);
        return result.TryGetProperty("value", out var value) ? value : default;
    }

    private async Task WhenReadyAsync(string id)
    {
        var until = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < until)
        {
            var last = _app.Events.LastOrDefault(e => e.GetProperty("event").GetString() == Events.SharesChanged);
            if (last.ValueKind != JsonValueKind.Undefined && last.GetProperty("payload").EnumerateArray().Any(s =>
                    s.GetProperty("id").GetString() == id && s.GetProperty("status").GetString() == "ready"))
                return;
            await Task.Delay(50);
        }
        throw new TimeoutException("The screen never showed the share as ready.");
    }

    /// <summary>A computer opens the invite the phone copied and receives it; returns where it arrived.</summary>
    private async Task<string> ComputerReceivesAsync(string id, string name)
    {
        await ClickAsync(Commands.SharesCopyInvite, new CopyInviteArgs(id, null));
        var computerPaths = new FakePaths(Path.Combine(_folder, "computer"));
        var computer = new MonoTorrentTransferEngine(computerPaths);
        _engines.Add(computer);
        await computer.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort() });
        var done = new TaskCompletionSource<ReceiveProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        computer.ReceiveProgressed += progress =>
        {
            if (progress.Done || progress.Failed)
                done.TrySetResult(progress);
        };
        var transferId = TransferId.New();
        Assert.True(await computer.ReceiveAsync(transferId, computer.FindInvite(_clipboard.Text!)!));
        await computer.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, _app.Engine.ListenEndPoint!.Port));
        Assert.True((await done.Task.WaitAsync(Timeout)).Done);
        return Path.Combine(computerPaths.ReceivedRoot, name);
    }

    private static string Checksum(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    [Fact]
    public async Task A_video_picked_on_the_phone_reaches_a_computer_byte_identical_and_its_details_name_no_folder()
    {
        var video = _phone.AddItem("Aniversário da vovó.mp4", 600_000);
        _picker.NextPick = video;

        var share = (await ClickAsync(Commands.SharesAdd)).GetProperty("share");
        var id = share.GetProperty("id").GetString()!;
        Assert.Equal("Aniversário da vovó.mp4", share.GetProperty("name").GetString());
        await WhenReadyAsync(id);
        // Nothing a person would not understand reaches the screen: no reference, and no folder.
        var details = await ClickAsync(Commands.SharesDetails, new IdArgs(id));
        Assert.Equal("", details.GetProperty("folder").GetString());
        Assert.DoesNotContain(_app.Events, e => e.ToString().Contains("content://", StringComparison.Ordinal));

        var arrived = await ComputerReceivesAsync(id, "Aniversário da vovó.mp4");

        Assert.Equal(Checksum(_phone.Items[video].Bytes), Checksum(File.ReadAllBytes(arrived)));
    }

    [Fact]
    public async Task A_folder_picked_on_the_phone_reaches_a_computer_with_its_structure()
    {
        var tree = _phone.AddTree("Viagem", ("praia.jpg", 300_000), ("dia 2/morro.jpg", 120_000));
        _picker.NextFolder = tree;

        var shared = await ClickAsync(Commands.SharesAddFolder);
        var id = shared.GetProperty("id").GetString()!;
        Assert.Equal(("Viagem", "folder", 2), (shared.GetProperty("name").GetString(), shared.GetProperty("kind").GetString(), shared.GetProperty("itemCount").GetInt32()));
        await WhenReadyAsync(id);

        var arrived = await ComputerReceivesAsync(id, "Viagem");

        var files = _phone.Trees[tree].Files;
        Assert.Equal(Checksum(_phone.Items[files["praia.jpg"]].Bytes), Checksum(File.ReadAllBytes(Path.Combine(arrived, "praia.jpg"))));
        Assert.Equal(Checksum(_phone.Items[files["dia 2/morro.jpg"]].Bytes), Checksum(File.ReadAllBytes(Path.Combine(arrived, "dia 2", "morro.jpg"))));
    }

    [Fact]
    public async Task Photos_picked_together_with_the_same_name_become_one_named_share()
    {
        _picker.NextPicks = [_phone.AddItem("IMG_1.jpg", 50_000), _phone.AddItem("IMG_1.jpg", 60_000)];

        Assert.Equal(2, (await ClickAsync(Commands.SharesAdd)).GetProperty("filesToName").GetInt32());
        var named = await ClickAsync(Commands.SharesAddPicked, new AddPickedArgs("Festa"));
        var id = named.GetProperty("id").GetString()!;
        await WhenReadyAsync(id);

        var arrived = await ComputerReceivesAsync(id, "Festa");

        Assert.Equal(Checksum(_phone.Items[_picker.NextPicks[1]].Bytes), Checksum(File.ReadAllBytes(Path.Combine(arrived, "IMG_1 (2).jpg"))));
    }

    [Fact]
    public async Task After_the_phone_restarts_the_picked_video_is_shared_again_and_one_taken_back_is_marked()
    {
        var video = _phone.AddItem("video.mp4", 200_000);
        var song = _phone.AddItem("song.mp3", 100_000);
        _picker.NextPick = video;
        var videoId = (await ClickAsync(Commands.SharesAdd)).GetProperty("share").GetProperty("id").GetString()!;
        _picker.NextPick = song;
        var songId = (await ClickAsync(Commands.SharesAdd)).GetProperty("share").GetProperty("id").GetString()!;
        await WhenReadyAsync(videoId);
        await WhenReadyAsync(songId);
        await _app.Engine.StopAsync();

        // The person took back the permission for the song while the phone was off.
        _phone.Revoked.Add(song);
        _app = await StartPhoneAsync();

        await WhenReadyAsync(videoId);
        var listed = (await ClickAsync(Commands.SharesList)).EnumerateArray().ToDictionary(s => s.GetProperty("id").GetString()!, s => s.GetProperty("status").GetString());
        Assert.Equal("unavailable", listed[songId]);
        var arrived = await ComputerReceivesAsync(videoId, "video.mp4");
        Assert.Equal(Checksum(_phone.Items[video].Bytes), Checksum(File.ReadAllBytes(arrived)));
    }

    /// <summary>The first bytes of a JPEG photo and an MP4 video, as a phone's camera writes them.</summary>
    private static readonly byte[] JpegHead = [0xFF, 0xD8, 0xFF, 0xE1];
    private static readonly byte[] Mp4Head = [0, 0, 0, 0x20, .. "ftypisom"u8];

    private static byte[] Starting(byte[] head, int size)
    {
        var content = RandomNumberGenerator.GetBytes(size);
        head.CopyTo(content, 0);
        return content;
    }

    private static byte[] ReadAll(MediaReply reply)
    {
        using var body = reply.Body!;
        using var copy = new MemoryStream();
        body.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>The person taps the file in the grid; the viewer asks to open it, then the web view reads it whole and seeks.</summary>
    private async Task ViewerPlaysAsync(ItemKindDto kind, string id, string expectedKind, string name, byte[] content)
    {
        var opened = await ClickAsync(Commands.MediaOpen, new MediaArgs(kind, id));
        Assert.Equal((expectedKind, name), (opened.GetProperty("kind").GetString(), opened.GetProperty("name").GetString()));
        // The screen gets an address that reads this one file: no phone reference and no folder.
        Assert.DoesNotContain("content://", opened.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(_folder, opened.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var url = opened.GetProperty("url").GetString()!;

        var whole = _app.Router.ServeMedia(url, null);
        Assert.Equal((200, (long)content.Length), (whole.Status, whole.Length));
        Assert.Equal(content, ReadAll(whole));
        var seek = _app.Router.ServeMedia(url, $"bytes={content.Length - 1000}-");
        Assert.Equal((206, $"bytes {content.Length - 1000}-{content.Length - 1}/{content.Length}"), (seek.Status, seek.ContentRange));
        Assert.Equal(content[^1000..], ReadAll(seek));
        Assert.Equal(416, _app.Router.ServeMedia(url, $"bytes={content.Length}-").Status);
    }

    [Fact]
    public async Task A_photo_and_a_video_picked_on_the_phone_open_in_the_viewer_and_the_video_seeks()
    {
        var photo = Starting(JpegHead, 80_000);
        var video = Starting(Mp4Head, 300_000);
        _picker.NextPick = _phone.AddItem("IMG_20261029.jpg", photo);
        var photoId = (await ClickAsync(Commands.SharesAdd)).GetProperty("share").GetProperty("id").GetString()!;
        _picker.NextPick = _phone.AddItem("VID_20261029.mp4", video);
        var videoId = (await ClickAsync(Commands.SharesAdd)).GetProperty("share").GetProperty("id").GetString()!;
        await WhenReadyAsync(photoId);
        await WhenReadyAsync(videoId);

        await ViewerPlaysAsync(ItemKindDto.Share, photoId, "picture", "IMG_20261029.jpg", photo);
        await ViewerPlaysAsync(ItemKindDto.Share, videoId, "video", "VID_20261029.mp4", video);
    }

    [Fact]
    public async Task A_video_the_phone_received_plays_and_seeks_from_where_it_arrived()
    {
        // A computer shares a video; the phone receives it through the screen's Receive button.
        var originals = Path.Combine(_folder, "originals");
        Directory.CreateDirectory(originals);
        var content = Starting(Mp4Head, 400_000);
        var original = Path.Combine(originals, "Formatura.mp4");
        File.WriteAllBytes(original, content);
        var computer = new MonoTorrentTransferEngine(new FakePaths(Path.Combine(_folder, "computer")));
        _engines.Add(computer);
        await computer.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort() });
        var offered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shareId = ShareId.New();
        computer.OfferChanged += (changed, state) =>
        {
            if (changed == shareId && state == OfferState.Ready)
                offered.TrySetResult();
        };
        await computer.OfferAsync(shareId, original);
        await offered.Task.WaitAsync(Timeout);

        var id = (await ClickAsync(Commands.TransfersReceive, new ReceiveArgs((await computer.GetInviteAsync(shareId))!))).GetProperty("id").GetString()!;
        Assert.True(TransferId.TryParse(id, out var transferId));
        await _app.Engine.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, computer.ListenEndPoint!.Port));
        var until = DateTime.UtcNow + Timeout;
        while (!_app.Events.Any(e => e.GetProperty("event").GetString() == Events.TransfersChanged
            && e.GetProperty("payload").EnumerateArray().Any(t => t.GetProperty("id").GetString() == id && t.GetProperty("state").GetString() == "done")))
        {
            Assert.True(DateTime.UtcNow < until, "The screen never showed the video as arrived.");
            await Task.Delay(50);
        }

        await ViewerPlaysAsync(ItemKindDto.Transfer, id, "video", "Formatura.mp4", content);
    }

    [Fact]
    public async Task A_video_the_phone_cannot_play_offers_its_folder_and_the_folder_opens()
    {
        _picker.NextPick = _phone.AddItem("Casamento.mkv", Starting([0x1A, 0x45, 0xDF, 0xA3], 50_000));
        var id = (await ClickAsync(Commands.SharesAdd)).GetProperty("share").GetProperty("id").GetString()!;
        await WhenReadyAsync(id);

        var json = await _app.Router.InvokeAsync(Commands.MediaOpen, JsonSerializer.Serialize(new MediaArgs(ItemKindDto.Share, id), Contract.Json));
        Assert.Equal(ErrorCodes.MediaNotPlayable, JsonDocument.Parse(json).RootElement.GetProperty("error").GetProperty("code").GetString());

        // "Open the folder": the phone's reveal shows the Files app there.
        await ClickAsync(Commands.SharesReveal, new IdArgs(id));
        Assert.Equal([_picker.NextPick], _opener.Revealed);
    }
}
