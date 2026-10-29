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
        var opener = new FakeOpener();
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
}
