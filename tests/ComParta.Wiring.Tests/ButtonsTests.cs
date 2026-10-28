using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ComParta.Contracts;
using ComParta.Core;
using ComParta.Core.Ports;
using ComParta.Core.Tests;
using ComParta.Core.UseCases;
using ComParta.Engine.MonoTorrent;
using ComParta.Shell;
using ComParta.Storage.Sqlite;
using MonoTorrent.Connections.TrackerServer;
using MonoTorrent.PortForwarding;
using MonoTorrent.TrackerServer;

namespace ComParta.Wiring.Tests;

/// <summary>
/// The screen's buttons, sent as the JSON commands the screen sends, through the app wired as it is in
/// AppServices: the real router, use cases, database and torrent engine, with router port mapping on
/// as at home. Only the window's own parts (file dialog, notifications, tray icon) are stand-ins, and
/// the home router is a slow one that never finishes removing a mapping.
/// Each test checks what the screen would show afterwards, from the events the app sends it.
/// </summary>
public sealed class ButtonsTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    /// <summary>A button that takes longer than this looks broken to the person.</summary>
    private static readonly TimeSpan Quick = TimeSpan.FromSeconds(8);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "comparta-tests", Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<JsonElement> _events = new();
    private readonly FakeFilePicker _picker = new();
    private readonly FakeFileSystem _files = new();
    private MonoTorrentTransferEngine _engine = null!;
    private BridgeRouter _router = null!;
    private Exception? _failure;
    private readonly Clipboard _clipboard = new();
    private FakePaths _paths = null!;
    private readonly FakeOpener _opener = new();
    // As in AppServices: what Windows opens the app with is read with the engine's own rules.
    private readonly OpenedInvites _opened = new(MonoTorrentTransferEngine.FindLinkIn, MonoTorrentTransferEngine.DownloadFileExtensions);
    private readonly FakePresence _presence = new();
    private BackgroundUseCases _background = null!;
    // The window is not in front, so a file that arrives is announced as in the app.
    private readonly FakeNotifier _notifier = new();
    private readonly FakeWindow _window = new();
    private readonly SwitchGuard _guard = new(PlatformGuard());

    /// <summary>
    /// Windows' own protection, as the app hands each received file to it (AD-13), or one that refuses
    /// every file as an antivirus does with a harmful one, when a test says so.
    /// </summary>
    private sealed class SwitchGuard(IReceivedFileGuard inner) : IReceivedFileGuard
    {
        private readonly ConcurrentQueue<(string Path, bool ShownAsReceived)> _checked = new();

        public bool Blocks { get; set; }
        /// <summary>Whether the screen already showed a file as received when it was checked.</summary>
        public Func<bool> ShownAsReceived { get; set; } = () => false;
        public IReadOnlyList<(string Path, bool ShownAsReceived)> Checked => [.. _checked];

        public ReceivedFileVerdict Check(string fullPath)
        {
            _checked.Enqueue((fullPath, ShownAsReceived()));
            var verdict = inner.Check(fullPath);
            return Blocks ? ReceivedFileVerdict.Blocked : verdict;
        }
    }

    /// <summary>The app's own guard: Windows' attachment services on Windows, none elsewhere.</summary>
    private static IReceivedFileGuard PlatformGuard() =>
        OperatingSystem.IsWindows() ? new AttachmentGuard() : NoReceivedFileCheck.Instance;

    private sealed class SlowRouter : INatGateway
    {
        public string Key => "slow router";
        public Task<int> MapAsync(Protocol protocol, int privatePort, int publicPort, TimeSpan lifetime) => Task.FromResult(publicPort);
        public Task UnmapAsync(Protocol protocol, int privatePort, int publicPort) => new TaskCompletionSource().Task;
        public Task<IPAddress?> GetExternalAddressAsync() => Task.FromResult<IPAddress?>(IPAddress.Parse("203.0.113.9"));
    }

    private sealed class RouterOnTheNetwork(INatGateway router) : INatDiscovery
    {
        public event Action<INatGateway>? Found;
        public void Start() => Found?.Invoke(router);
        public void Stop() { }
    }

    private sealed class Clipboard : IClipboard
    {
        public string? Text { get; private set; }
        public Task SetTextAsync(string text, CancellationToken ct = default) { Text = text; return Task.CompletedTask; }
        /// <summary>What the person copied somewhere else, for the receive dialog to find.</summary>
        public string? Holds { get; set; }
        public Task<string?> GetTextAsync(CancellationToken ct = default) => Task.FromResult(Holds);
    }

    /// <summary>
    /// The notices file at the root of the repository: the same file the app ships as a MauiAsset
    /// (read there by AppPackageNotices). check-package.ps1 checks the copy inside the installed package.
    /// </summary>
    private sealed class NoticesInTheRepository : IThirdPartyNotices
    {
        public async Task<string?> ReadAsync(CancellationToken ct = default)
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "ComParta.sln")))
                folder = folder.Parent;
            var file = folder is null ? null : Path.Combine(folder.FullName, NoticesFile.Name);
            return file is not null && File.Exists(file) ? await File.ReadAllTextAsync(file, ct) : null;
        }
    }

    public async Task InitializeAsync()
    {
        var paths = _paths = new FakePaths(_folder);
        Directory.CreateDirectory(paths.ReceivedRoot);
        var store = await SqliteStore.OpenAsync(paths.DatabasePath);
        _engine = new MonoTorrentTransferEngine(paths, new RouterOnTheNetwork(new SlowRouter()));

        // As in AppServices.StartAsync.
        var shares = new ShareUseCases(store, _engine, _picker, _files, paths: paths);
        // The folder dialog is a stand-in; whether a folder can be written is checked on the real disk.
        var receivedFolder = new ReceivedFolder(store, paths, _picker, new LocalFileSystem());
        var transfers = new TransferUseCases(
            store, _engine, picker: _picker, files: _files, storage: new FakeStorage(), received: receivedFolder, guard: _guard);
        var settings = new SettingsUseCases(store, new FakeSystem("en"), _engine, receivedFolder);
        var connection = new ConnectionUseCases(_engine, new FakeFirewall());
        var activity = new ActivityUseCases(_engine, store);
        var opener = _opener;
        // What has arrived is read from the real Received folder, as in the app.
        var disk = new LocalFileSystem();
        var received = new ReceivedUseCases(transfers, paths, opener, disk);
        var thumbnails = new ThumbnailUseCases(shares, received, new FakeThumbnails());
        var sending = new SendingUseCases(_engine, shares);
        var notifications = new NotificationUseCases(_engine, transfers, shares, sending, settings, _notifier, _window);
        var background = _background = new BackgroundUseCases(_engine, shares, transfers, sending, settings, store, notifications, _presence);
        _router = new BridgeRouter(
            shares, transfers, settings, connection, _opened, activity, received, _clipboard, opener, thumbnails,
            new DroppedFiles(), sending, notifications, background, new MediaUseCases(shares, received, disk),
            new SupportUseCases(store, new FakeSystem("en"), settings, shares, transfers, connection),
            new AboutUseCases(new NoticesInTheRepository()));
        _router.EventRaised += json => _events.Enqueue(JsonDocument.Parse(json).RootElement.Clone());
        _router.CommandFailed += (_, e) => _failure = e;

        // Download links and download files keep the servers they name, as with the app's default policy.
        await _engine.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort(), AllowPortForwarding = true, AllowServersInDownloadLinks = true });
        await settings.StartAsync();
        await activity.StartAsync();
        await shares.StartAsync();
        await transfers.StartAsync();
        await notifications.StartAsync();
        await background.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _engine.StopAsync().WaitAsync(Quick);
        Assert.Null(_failure);
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>Sends a command as the screen does and checks it answered ok within <see cref="Quick"/>.</summary>
    private async Task<JsonElement> ClickAsync(string command, object? args = null)
    {
        var json = await _router.InvokeAsync(command, args is null ? "null" : JsonSerializer.Serialize(args, Contract.Json)).WaitAsync(Quick);
        var result = JsonDocument.Parse(json).RootElement;
        Assert.True(result.GetProperty("ok").GetBoolean(), json);
        return result.TryGetProperty("value", out var value) ? value : default;
    }

    /// <summary>The last payload of an event, once it says what <paramref name="shows"/> looks for.</summary>
    private async Task<JsonElement> ScreenShowsAsync(string eventName, Func<JsonElement, bool> shows)
    {
        var until = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < until)
        {
            var last = _events.LastOrDefault(e => e.GetProperty("event").GetString() == eventName);
            if (last.ValueKind != JsonValueKind.Undefined && shows(last.GetProperty("payload")))
                return last.GetProperty("payload");
            await Task.Delay(50);
        }
        throw new TimeoutException($"The screen never showed the expected {eventName}.");
    }

    /// <param name="head">The bytes the file starts with, as a picture, song or video would.</param>
    private async Task<string> ShareAFileAsync(string name, byte[]? head = null)
    {
        var folder = Path.Combine(_folder, "originals");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        var content = RandomNumberGenerator.GetBytes(200_000);
        head?.CopyTo(content, 0);
        File.WriteAllBytes(path, content);
        _files.Files[path] = 200_000;
        _picker.NextPick = path;

        var id = (await ClickAsync(Commands.SharesAdd)).GetProperty("share").GetProperty("id").GetString()!;
        await ScreenShowsAsync(Events.SharesChanged, shares => shares.EnumerateArray().Any(s =>
            s.GetProperty("id").GetString() == id && s.GetProperty("status").GetString() == "ready"));
        // Once the router has mapped the port, stopping the last file used to wait for it to unmap.
        for (var i = 0; i < 100 && _engine.PortMapper!.Mappings.Created.Count == 0; i++)
            await Task.Delay(50);
        return id;
    }

    [Fact]
    public async Task A_folder_and_several_named_files_each_become_one_share_kept_in_the_database()
    {
        var folder = Path.Combine(_folder, "originals", "Trip");
        Directory.CreateDirectory(Path.Combine(folder, "day 2"));
        foreach (var name in new[] { "beach.jpg", Path.Combine("day 2", "hill.jpg") })
        {
            File.WriteAllBytes(Path.Combine(folder, name), RandomNumberGenerator.GetBytes(1_000));
            _files.Files[Path.Combine(folder, name)] = 1_000;
        }

        _picker.NextFolder = folder;
        var shared = await ClickAsync(Commands.SharesAddFolder);
        Assert.Equal(("Trip", "folder", 2), (shared.GetProperty("name").GetString(), shared.GetProperty("kind").GetString(), shared.GetProperty("itemCount").GetInt32()));
        await ScreenShowsAsync(Events.SharesChanged, list => list.EnumerateArray().Any(s => s.GetProperty("kind").GetString() == "folder"));
        var folderId = shared.GetProperty("id").GetString()!;
        // Its card shows its photos, and its details how many files it has.
        Assert.Equal(JsonValueKind.String, (await ClickAsync(Commands.ThumbnailsGet, new ThumbnailArgs(ThumbnailKindDto.Share, folderId, 96, 1))).ValueKind);
        Assert.NotEqual(JsonValueKind.String, (await ClickAsync(Commands.ThumbnailsGet, new ThumbnailArgs(ThumbnailKindDto.Share, folderId, 96, 2))).ValueKind);
        var details = await ClickAsync(Commands.SharesDetails, new IdArgs(folderId));
        Assert.Equal((2, "folder"), (details.GetProperty("itemCount").GetInt32(), details.GetProperty("kind").GetString()));

        _picker.NextPicks = [Path.Combine(folder, "beach.jpg"), Path.Combine(folder, "day 2", "hill.jpg")];
        Assert.Equal(2, (await ClickAsync(Commands.SharesAdd)).GetProperty("filesToName").GetInt32());
        var named = await ClickAsync(Commands.SharesAddPicked, new AddPickedArgs("Best of the trip"));
        Assert.Equal(("collection", 2000L), (named.GetProperty("kind").GetString(), named.GetProperty("sizeBytes").GetInt64()));

        // The engine reads each file where it is, so both are ready to send with one invite each.
        await ScreenShowsAsync(Events.SharesChanged, list => list.GetArrayLength() == 2
            && list.EnumerateArray().All(s => s.GetProperty("status").GetString() == "ready"));
        await ClickAsync(Commands.SharesCopyInvite, new CopyInviteArgs(named.GetProperty("id").GetString()!, null));
        Assert.Contains("Best", _clipboard.Text);

        var stored = await ((IShareRepository)await SqliteStore.OpenAsync(_paths.DatabasePath)).GetAllAsync();
        Assert.Equal([ShareKind.Folder, ShareKind.Collection], stored.Select(s => s.Kind));
        Assert.Equal(["beach.jpg", "day 2/hill.jpg"], stored[0].Items.Select(i => i.RelativePath));
        Assert.Equal(Path.Combine(folder, "day 2", "hill.jpg"), stored[1].Items[1].ContentRef);
        _picker.NextPicks = null;
    }

    [Fact]
    public async Task A_shared_file_is_one_item_kept_in_the_database_and_marked_when_it_goes_missing()
    {
        var id = await ShareAFileAsync("letter.pdf");
        var path = Path.Combine(_folder, "originals", "letter.pdf");

        var listed = Assert.Single((await ClickAsync(Commands.SharesList)).EnumerateArray());
        Assert.Equal((1, "file"), (listed.GetProperty("itemCount").GetInt32(), listed.GetProperty("kind").GetString()));
        var stored = Assert.Single(await ((IShareRepository)await SqliteStore.OpenAsync(_paths.DatabasePath)).GetAllAsync());
        Assert.Equal(new ShareItem("letter.pdf", 200_000, path), Assert.Single(stored.Items));

        _files.Files.Remove(path);
        var missing = Assert.Single((await ClickAsync(Commands.SharesList)).EnumerateArray());
        Assert.Equal((id, "unavailable"), (missing.GetProperty("id").GetString(), missing.GetProperty("status").GetString()));
    }

    [Theory]
    [InlineData("photo.jpg", "picture", "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 })]
    [InlineData("song.mp3", "song", "audio/mpeg", new byte[] { (byte)'I', (byte)'D', (byte)'3', 4 })]
    [InlineData("party.mp4", "video", "video/mp4", new byte[] { 0, 0, 0, 0x20, (byte)'f', (byte)'t', (byte)'y', (byte)'p' })]
    public async Task Opening_a_shared_picture_song_or_video_streams_it_from_disk_in_the_parts_a_player_asks_for(
        string name, string kind, string type, byte[] head)
    {
        var id = await ShareAFileAsync(name, head);
        var onDisk = File.ReadAllBytes(Path.Combine(_folder, "originals", name));

        var opened = await ClickAsync(Commands.MediaOpen, new MediaArgs(ItemKindDto.Share, id));
        Assert.Equal((kind, name), (opened.GetProperty("kind").GetString(), opened.GetProperty("name").GetString()));
        Assert.DoesNotContain(_folder, opened.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var url = opened.GetProperty("url").GetString()!;

        // As the web view asks: the start, then a jump near the end, as a player seeking does.
        var whole = _router.ServeMedia(url, null);
        Assert.Equal((200, type, 200_000L), (whole.Status, whole.ContentType, whole.Length));
        Assert.Equal(onDisk, ReadAll(whole));
        var seek = _router.ServeMedia(url, "bytes=150000-150999");
        Assert.Equal((206, "bytes 150000-150999/200000"), (seek.Status, seek.ContentRange));
        Assert.Equal(onDisk[150_000..151_000], ReadAll(seek));
        Assert.Equal(416, _router.ServeMedia(url, "bytes=200000-").Status);
    }

    [Fact]
    public async Task Opening_a_program_or_an_unknown_file_answers_that_it_cannot_be_played()
    {
        foreach (var name in new[] { "setup.exe", "notes.xyz" })
        {
            var id = await ShareAFileAsync(name, "MZ"u8.ToArray());
            var json = await _router.InvokeAsync(Commands.MediaOpen, JsonSerializer.Serialize(new MediaArgs(ItemKindDto.Share, id), Contract.Json));
            var result = JsonDocument.Parse(json).RootElement;
            Assert.Equal(ErrorCodes.MediaNotPlayable, result.GetProperty("error").GetProperty("code").GetString());
        }

        // A program renamed to look like a video is refused too, and Open the folder shows it selected there.
        var renamed = await ShareAFileAsync("film.mp4", "MZ"u8.ToArray());
        var answer = await _router.InvokeAsync(Commands.MediaOpen, JsonSerializer.Serialize(new MediaArgs(ItemKindDto.Share, renamed), Contract.Json));
        Assert.Contains(ErrorCodes.MediaNotPlayable, answer, StringComparison.Ordinal);
        await ClickAsync(Commands.SharesReveal, new IdArgs(renamed));
        Assert.Equal([Path.Combine(_folder, "originals", "film.mp4")], _opener.Revealed);
    }

    [Fact]
    public async Task A_received_program_opens_only_after_the_person_confirms_a_second_time()
    {
        // Another device on this computer shares a program.
        var originals = Path.Combine(_folder, "originals");
        Directory.CreateDirectory(originals);
        var program = Path.Combine(originals, "setup.exe");
        File.WriteAllBytes(program, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00]);
        await using var sender = new MonoTorrentTransferEngine(new FakePaths(Path.Combine(_folder, "sender-program")));
        await sender.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort() });
        var offered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shareId = ShareId.New();
        sender.OfferChanged += (changed, state) =>
        {
            if (changed == shareId && state == OfferState.Ready)
                offered.TrySetResult();
        };
        await sender.OfferAsync(shareId, program);
        await offered.Task.WaitAsync(Timeout);
        var invite = (await sender.GetInviteAsync(shareId))!;

        var id = (await ClickAsync(Commands.TransfersReceive, new ReceiveArgs(invite))).GetProperty("id").GetString()!;
        Assert.True(TransferId.TryParse(id, out var transferId));
        await _engine.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));
        var shown = await ScreenShowsAsync(Events.TransfersChanged, list => list.EnumerateArray().Any(t =>
            t.GetProperty("id").GetString() == id && t.GetProperty("state").GetString() == "done"));
        var row = shown.EnumerateArray().Single(t => t.GetProperty("id").GetString() == id);
        Assert.True(row.GetProperty("risky").GetBoolean());

        // The first ask is refused and nothing runs; the second ask, after the person confirms, opens it.
        var first = JsonDocument.Parse(await _router.InvokeAsync(
            Commands.ReceivedOpen, JsonSerializer.Serialize(new OpenReceivedArgs(id), Contract.Json)).WaitAsync(Quick)).RootElement;
        Assert.Equal(ErrorCodes.ReceivedConfirmRisky, first.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(_opener.Opened);

        await ClickAsync(Commands.ReceivedOpen, new OpenReceivedArgs(id, true));
        Assert.Single(_opener.Opened);
        Assert.EndsWith("setup.exe", _opener.Opened[0], StringComparison.Ordinal);
    }

    /// <summary>Another device on this computer offers a file and the screen's Receive starts getting it; the sender stays for the caller to stop.</summary>
    private async Task<(MonoTorrentTransferEngine Sender, string Id)> StartReceivingFromAnotherDeviceAsync(string name)
    {
        var originals = Path.Combine(_folder, "originals");
        Directory.CreateDirectory(originals);
        var file = Path.Combine(originals, name);
        File.WriteAllBytes(file, RandomNumberGenerator.GetBytes(150_000));
        var sender = new MonoTorrentTransferEngine(new FakePaths(Path.Combine(_folder, "sender-" + Guid.NewGuid().ToString("N"))));
        await sender.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort() });
        var offered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shareId = ShareId.New();
        sender.OfferChanged += (changed, state) =>
        {
            if (changed == shareId && state == OfferState.Ready)
                offered.TrySetResult();
        };
        await sender.OfferAsync(shareId, file);
        await offered.Task.WaitAsync(Timeout);
        var invite = (await sender.GetInviteAsync(shareId))!;

        var id = (await ClickAsync(Commands.TransfersReceive, new ReceiveArgs(invite))).GetProperty("id").GetString()!;
        Assert.True(TransferId.TryParse(id, out var transferId));
        await _engine.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));
        return (sender, id);
    }

    private bool ShownAs(string id, string state) =>
        _events.Any(e => e.GetProperty("event").GetString() == Events.TransfersChanged
            && e.GetProperty("payload").EnumerateArray().Any(t => t.GetProperty("id").GetString() == id && t.GetProperty("state").GetString() == state));

    [Fact]
    public async Task A_received_file_is_handed_to_Windows_protection_once_and_marked_before_it_shows_as_received()
    {
        _guard.ShownAsReceived = () => _events.Any(e => e.GetProperty("event").GetString() == Events.TransfersChanged
            && e.GetProperty("payload").EnumerateArray().Any(t => t.GetProperty("state").GetString() == "done"));
        var (sender, id) = await StartReceivingFromAnotherDeviceAsync("letter.pdf");
        await using var _ = sender;

        await ScreenShowsAsync(Events.TransfersChanged, list => list.EnumerateArray().Any(t =>
            t.GetProperty("id").GetString() == id && t.GetProperty("state").GetString() == "done"));

        var received = Path.GetFullPath(Path.Combine(_paths.ReceivedRoot, "letter.pdf"));
        var check = Assert.Single(_guard.Checked);
        Assert.Equal(received, check.Path);
        Assert.False(check.ShownAsReceived);
        Assert.Contains(_notifier.Shown, n => n.Id == id);
        // Windows' "came from another computer" mark, which Properties shows with Unblock.
        if (OperatingSystem.IsWindows())
            Assert.Contains("ZoneId=3", File.ReadAllText(received + ":Zone.Identifier"), StringComparison.Ordinal);
        await ClickAsync(Commands.ReceivedOpen, new OpenReceivedArgs(id));
        Assert.Single(_opener.Opened);
    }

    [Fact]
    public async Task A_received_file_the_antivirus_refuses_shows_as_blocked_cannot_be_opened_and_is_not_announced()
    {
        _guard.Blocks = true;
        var (sender, id) = await StartReceivingFromAnotherDeviceAsync("invoice.exe");
        await using var _ = sender;

        var shown = await ScreenShowsAsync(Events.TransfersChanged, list => list.EnumerateArray().Any(t =>
            t.GetProperty("id").GetString() == id && t.GetProperty("state").GetString() == "blocked"));

        Assert.Single(_guard.Checked);
        Assert.False(ShownAs(id, "done"));
        Assert.DoesNotContain(_notifier.Shown, n => n.Id == id);
        var open = JsonDocument.Parse(await _router.InvokeAsync(
            Commands.ReceivedOpen, JsonSerializer.Serialize(new OpenReceivedArgs(id, true), Contract.Json)).WaitAsync(Quick)).RootElement;
        Assert.Equal(ErrorCodes.ReceivedNotFound, open.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(_opener.Opened);
        // Removing it from the list still works, as the card offers.
        await ClickAsync(Commands.TransfersRemove, new IdArgs(id));
        await ScreenShowsAsync(Events.TransfersChanged, list => list.EnumerateArray().All(t => t.GetProperty("id").GetString() != id));
    }

    [Fact]
    public async Task A_received_folder_is_one_item_marked_as_a_folder_and_lists_its_files()
    {
        // Another device on this computer shares a folder with a file one level down.
        var originals = Path.Combine(_folder, "originals-trip");
        Directory.CreateDirectory(Path.Combine(originals, "day 1"));
        var items = new List<ShareItem>();
        foreach (var place in new[] { "day 1/photo.jpg", "notes.txt" })
        {
            var file = Path.Combine(originals, place.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllBytes(file, RandomNumberGenerator.GetBytes(150_000));
            items.Add(new ShareItem(place, new FileInfo(file).Length, file));
        }
        await using var sender = new MonoTorrentTransferEngine(new FakePaths(Path.Combine(_folder, "sender-trip")));
        await sender.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort() });
        var offered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shareId = ShareId.New();
        sender.OfferChanged += (changed, state) =>
        {
            if (changed == shareId && state == OfferState.Ready)
                offered.TrySetResult();
        };
        await sender.OfferItemsAsync(shareId, "Trip", items);
        await offered.Task.WaitAsync(Timeout);
        var invite = (await sender.GetInviteAsync(shareId))!;

        var id = (await ClickAsync(Commands.TransfersReceive, new ReceiveArgs(invite))).GetProperty("id").GetString()!;
        Assert.True(TransferId.TryParse(id, out var transferId));
        await _engine.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));
        await ScreenShowsAsync(Events.TransfersChanged, list => list.EnumerateArray().Any(t =>
            t.GetProperty("id").GetString() == id && t.GetProperty("state").GetString() == "done"));

        var rows = await ClickAsync(Commands.TransfersList);
        var row = rows.EnumerateArray().Single(t => t.GetProperty("id").GetString() == id);
        Assert.Equal("Trip", row.GetProperty("name").GetString());
        Assert.True(row.GetProperty("folder").GetBoolean());

        var listed = await ClickAsync(Commands.ReceivedFiles, new IdArgs(id));
        Assert.Equal(["day 1/photo.jpg", "notes.txt"], listed.EnumerateArray().Select(f => f.GetProperty("relativePath").GetString()));
    }

    [Fact]
    public async Task A_received_photo_opens_in_the_viewer_from_the_Received_folder()
    {
        // Another device on this computer shares a photo.
        var originals = Path.Combine(_folder, "originals");
        Directory.CreateDirectory(originals);
        var photo = Path.Combine(originals, "from grandma.jpg");
        var content = RandomNumberGenerator.GetBytes(150_000);
        new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.CopyTo(content, 0);
        File.WriteAllBytes(photo, content);
        await using var sender = new MonoTorrentTransferEngine(new FakePaths(Path.Combine(_folder, "sender")));
        await sender.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort() });
        var offered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shareId = ShareId.New();
        sender.OfferChanged += (changed, state) =>
        {
            if (changed == shareId && state == OfferState.Ready)
                offered.TrySetResult();
        };
        await sender.OfferAsync(shareId, photo);
        await offered.Task.WaitAsync(Timeout);
        var invite = (await sender.GetInviteAsync(shareId))!;

        var id = (await ClickAsync(Commands.TransfersReceive, new ReceiveArgs(invite))).GetProperty("id").GetString()!;
        // Discovery is closed in tests, so the two are introduced by hand.
        Assert.True(TransferId.TryParse(id, out var transferId));
        await _engine.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));
        await ScreenShowsAsync(Events.TransfersChanged, list => list.EnumerateArray().Any(t =>
            t.GetProperty("id").GetString() == id && t.GetProperty("state").GetString() == "done"));

        var opened = await ClickAsync(Commands.MediaOpen, new MediaArgs(ItemKindDto.Transfer, id));
        Assert.Equal(("picture", "from grandma.jpg"), (opened.GetProperty("kind").GetString(), opened.GetProperty("name").GetString()));
        var reply = _router.ServeMedia(opened.GetProperty("url").GetString()!, null);
        Assert.Equal((200, "image/jpeg"), (reply.Status, reply.ContentType));
        Assert.Equal(content, ReadAll(reply));
    }

    /// <summary>Another device on this computer shares a file of this name; this app receives it through the screen's Receive button.</summary>
    private async Task<(string Id, byte[] Content)> ReceiveFromAnotherDeviceAsync(string name)
    {
        var originals = Path.Combine(_folder, "originals");
        Directory.CreateDirectory(originals);
        var original = Path.Combine(originals, name);
        var content = RandomNumberGenerator.GetBytes(120_000);
        File.WriteAllBytes(original, content);
        await using var sender = new MonoTorrentTransferEngine(new FakePaths(Path.Combine(_folder, "sender-" + Guid.NewGuid().ToString("N"))));
        await sender.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort() });
        var offered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shareId = ShareId.New();
        sender.OfferChanged += (changed, state) =>
        {
            if (changed == shareId && state == OfferState.Ready)
                offered.TrySetResult();
        };
        await sender.OfferAsync(shareId, original);
        await offered.Task.WaitAsync(Timeout);
        var invite = (await sender.GetInviteAsync(shareId))!;

        var id = (await ClickAsync(Commands.TransfersReceive, new ReceiveArgs(invite))).GetProperty("id").GetString()!;
        Assert.True(TransferId.TryParse(id, out var transferId));
        await _engine.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));
        await ScreenShowsAsync(Events.TransfersChanged, list => list.EnumerateArray().Any(t =>
            t.GetProperty("id").GetString() == id && t.GetProperty("state").GetString() == "done"));
        return (id, content);
    }

    [Fact]
    public async Task A_received_folder_chosen_in_settings_takes_new_files_and_earlier_ones_stay_where_they_are()
    {
        var before = await ReceiveFromAnotherDeviceAsync("before.jpg");

        // The person picks a folder on another drive in Windows' folder dialog.
        var family = Path.Combine(_folder, "other drive", "Family");
        Directory.CreateDirectory(family);
        _files.EmptyFolders.Add(family);
        _picker.NextFolder = family;
        Assert.Equal("Family", (await ClickAsync(Commands.SettingsChooseReceivedFolder)).GetString());
        await ScreenShowsAsync(Events.SettingsChanged, s =>
            s.GetProperty("receivedFolder").GetString() == "Family" && s.GetProperty("receivedFolderChosen").GetBoolean());
        // The screen is given the folder's name, never its path (AD-7).
        Assert.DoesNotContain(_folder, (await ClickAsync(Commands.SettingsGet)).GetRawText(), StringComparison.OrdinalIgnoreCase);

        var after = await ReceiveFromAnotherDeviceAsync("after.jpg");

        // The new file is in the chosen folder, the earlier one where it was, and nothing unfinished is left.
        Assert.Equal(after.Content, File.ReadAllBytes(Path.Combine(family, "after.jpg")));
        Assert.Equal(before.Content, File.ReadAllBytes(Path.Combine(_paths.ReceivedRoot, "before.jpg")));
        Assert.False(File.Exists(Path.Combine(_paths.ReceivedRoot, "after.jpg")));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(family, ".in-progress"), "*", SearchOption.AllDirectories));

        // Open, and Open my received files, each go to the right place.
        await ClickAsync(Commands.ReceivedOpen, new OpenReceivedArgs(before.Id));
        await ClickAsync(Commands.ReceivedOpen, new OpenReceivedArgs(after.Id));
        Assert.Equal([Path.Combine(_paths.ReceivedRoot, "before.jpg"), Path.Combine(family, "after.jpg")], _opener.Opened);
        await ClickAsync(Commands.ReceivedOpenFolder);
        Assert.Equal(family, _opener.OpenedFolder);

        // The app opens the same choice and the same places next time.
        var store = await SqliteStore.OpenAsync(_paths.DatabasePath);
        var next = new ReceivedFolder(store, _paths);
        await next.LoadAsync();
        Assert.Equal(family, next.Chosen);
        var stored = await ((ITransferRepository)store).GetAllAsync();
        Assert.Equal([null, family], stored.Select(t => t.Folder));

        // Back to the usual folder.
        await ClickAsync(Commands.SettingsUseUsualReceivedFolder);
        await ScreenShowsAsync(Events.SettingsChanged, s => !s.GetProperty("receivedFolderChosen").GetBoolean());
        await ClickAsync(Commands.ReceivedOpenFolder);
        Assert.Null(_opener.OpenedFolder);
    }

    [Fact]
    public async Task A_received_folder_that_cannot_be_written_is_refused_with_its_own_words()
    {
        _picker.NextFolder = Path.Combine(_folder, "not there");
        var json = await _router.InvokeAsync(Commands.SettingsChooseReceivedFolder, "null").WaitAsync(Quick);
        Assert.Equal(ErrorCodes.ReceivedFolderUnusable, JsonDocument.Parse(json).RootElement.GetProperty("error").GetProperty("code").GetString());

        // Closing the dialog is not a problem and changes nothing.
        _picker.NextFolder = null;
        Assert.Equal(JsonValueKind.Undefined, (await ClickAsync(Commands.SettingsChooseReceivedFolder)).ValueKind);
        Assert.False((await ClickAsync(Commands.SettingsGet)).GetProperty("receivedFolderChosen").GetBoolean());
    }

    [Fact]
    public async Task A_receive_limit_chosen_in_settings_slows_a_file_down_and_is_kept()
    {
        await ClickAsync(Commands.SettingsSet, new SetSettingsArgs(null, null, null, 0, false, null, WelcomeDone: true, ReceiveLimit: 100_000));
        await ScreenShowsAsync(Events.SettingsChanged, s => s.GetProperty("receiveLimit").ValueKind == JsonValueKind.Number);

        var originals = Path.Combine(_folder, "originals");
        Directory.CreateDirectory(originals);
        var file = Path.Combine(originals, "slow.bin");
        File.WriteAllBytes(file, RandomNumberGenerator.GetBytes(600_000));
        await using var sender = new MonoTorrentTransferEngine(new FakePaths(Path.Combine(_folder, "sender")));
        await sender.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort() });
        var offered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shareId = ShareId.New();
        sender.OfferChanged += (changed, state) =>
        {
            if (changed == shareId && state == OfferState.Ready)
                offered.TrySetResult();
        };
        await sender.OfferAsync(shareId, file);
        await offered.Task.WaitAsync(Timeout);
        var invite = (await sender.GetInviteAsync(shareId))!;

        var id = (await ClickAsync(Commands.TransfersReceive, new ReceiveArgs(invite))).GetProperty("id").GetString()!;
        Assert.True(TransferId.TryParse(id, out var transferId));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await _engine.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));
        await ScreenShowsAsync(Events.TransfersChanged, list => list.EnumerateArray().Any(t =>
            t.GetProperty("id").GetString() == id && t.GetProperty("state").GetString() == "done"));

        // 600 KB at 100 KB a second: about six seconds, where it would otherwise take under one.
        Assert.True(watch.Elapsed > TimeSpan.FromSeconds(3), $"It arrived in {watch.Elapsed}.");
        var store = await SqliteStore.OpenAsync(_paths.DatabasePath);
        Assert.Equal(100_000, (await new SettingsUseCases(store, new FakeSystem("en")).GetAsync()).ReceiveLimit);
    }

    private static byte[] ReadAll(MediaReply reply)
    {
        using var body = reply.Body!;
        using var copy = new MemoryStream();
        body.CopyTo(copy);
        return copy.ToArray();
    }

    [Fact]
    public async Task The_phones_back_button_reaches_the_screen_and_leaving_keeps_a_shared_file_offered()
    {
        // Nothing shared: leaving ends the app, as closing the window does.
        _background.BackPressed();
        Assert.True((await ScreenShowsAsync(Events.BackChanged, pressed => pressed.GetBoolean())).GetBoolean());
        await ClickAsync(Commands.AppLeave);
        Assert.Equal((1, 0), (_presence.Quits, _presence.Hides));

        // With a file shared, the app only goes out of sight and the file stays offered.
        var id = await ShareAFileAsync("keep.bin");
        await ClickAsync(Commands.AppLeave);
        Assert.Equal((1, 1), (_presence.Quits, _presence.Hides));
        ShareId.TryParse(id, out var shareId);
        Assert.NotEqual(global::MonoTorrent.Client.TorrentState.Stopped, _engine.OfferStateOf(shareId));
        Assert.Equal(id, (await ClickAsync(Commands.SharesList))[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Stop_shows_stopped_stops_the_file_and_Continue_sends_it_again()
    {
        var id = await ShareAFileAsync("stop.bin");

        await ClickAsync(Commands.ActivitySet, new SetActivityArgs(SendingStopped: true, ReceivingStopped: true));

        await ScreenShowsAsync(Events.ActivityChanged, a => a.GetProperty("sendingStopped").GetBoolean()
            && a.GetProperty("receivingStopped").GetBoolean());
        ShareId.TryParse(id, out var shareId);
        Assert.Equal(global::MonoTorrent.Client.TorrentState.Stopped, _engine.OfferStateOf(shareId));
        var got = (await ClickAsync(Commands.ActivityGet));
        Assert.True(got.GetProperty("sendingStopped").GetBoolean());

        await ClickAsync(Commands.ActivitySet, new SetActivityArgs(SendingStopped: false, ReceivingStopped: false));

        await ScreenShowsAsync(Events.ActivityChanged, a => !a.GetProperty("sendingStopped").GetBoolean());
        Assert.NotEqual(global::MonoTorrent.Client.TorrentState.Stopped, _engine.OfferStateOf(shareId));
    }

    [Fact]
    public async Task Stopping_only_sending_from_the_menu_leaves_receiving_on()
    {
        await ShareAFileAsync("menu.bin");

        await ClickAsync(Commands.ActivitySet, new SetActivityArgs(SendingStopped: true, ReceivingStopped: false));

        await ScreenShowsAsync(Events.ActivityChanged, a => a.GetProperty("sendingStopped").GetBoolean()
            && !a.GetProperty("receivingStopped").GetBoolean());
    }

    [Fact]
    public async Task Stop_sharing_takes_the_only_file_off_the_screen()
    {
        var id = await ShareAFileAsync("only.bin");

        await ClickAsync(Commands.SharesRemove, new IdArgs(id));

        await ScreenShowsAsync(Events.SharesChanged, shares => shares.GetArrayLength() == 0);
        Assert.Equal(0, (await ClickAsync(Commands.SharesList)).GetArrayLength());
        // Invites given out earlier still find this computer through the router.
        Assert.NotEmpty(_engine.PortMapper!.Mappings.Created);
    }

    [Fact]
    public async Task Pausing_a_shared_file_shows_it_paused_and_play_sends_it_again()
    {
        var id = await ShareAFileAsync("paused.bin");
        ShareId.TryParse(id, out var shareId);

        await ClickAsync(Commands.SharesPause, new IdArgs(id));

        await ScreenShowsAsync(Events.SharesChanged, shares => shares[0].GetProperty("paused").GetBoolean());
        Assert.Equal(global::MonoTorrent.Client.TorrentState.Stopped, _engine.OfferStateOf(shareId));

        await ClickAsync(Commands.SharesResume, new IdArgs(id));

        await ScreenShowsAsync(Events.SharesChanged, shares => !shares[0].GetProperty("paused").GetBoolean());
        Assert.NotEqual(global::MonoTorrent.Client.TorrentState.Stopped, _engine.OfferStateOf(shareId));
    }

    [Fact]
    public async Task Pausing_and_continuing_a_download_shows_each_state()
    {
        var transfer = await ClickAsync(Commands.TransfersReceive,
            new ReceiveArgs("magnet:?xt=urn:btih:4123456789abcdef0123456789abcdef01234567&dn=film.mkv"));
        var id = transfer.GetProperty("id").GetString()!;

        await ClickAsync(Commands.TransfersPause, new IdArgs(id));
        await ScreenShowsAsync(Events.TransfersChanged, list => list[0].GetProperty("state").GetString() == "paused");

        await ClickAsync(Commands.TransfersResume, new IdArgs(id));
        await ScreenShowsAsync(Events.TransfersChanged, list => list[0].GetProperty("state").GetString() != "paused");

        await ClickAsync(Commands.TransfersRemove, new IdArgs(id));
        await ScreenShowsAsync(Events.TransfersChanged, list => list.GetArrayLength() == 0);
    }

    [Fact]
    public async Task Copy_invite_puts_the_message_and_link_on_the_clipboard_even_while_stopped_or_paused()
    {
        var id = await ShareAFileAsync("invite.bin");
        var message = new CopyInviteArgs(id, "Hi! Open this to get the file: {link}");

        await ClickAsync(Commands.SharesCopyInvite, message);
        Assert.StartsWith("Hi! Open this to get the file: magnet:?xt=urn:btih:", _clipboard.Text);

        await ClickAsync(Commands.ActivitySet, new SetActivityArgs(SendingStopped: true, ReceivingStopped: true));
        await ClickAsync(Commands.SharesCopyInvite, message with { Message = null });
        Assert.StartsWith("magnet:?xt=urn:btih:", _clipboard.Text);

        await ClickAsync(Commands.ActivitySet, new SetActivityArgs(SendingStopped: false, ReceivingStopped: false));
        await ClickAsync(Commands.SharesPause, new IdArgs(id));
        await ClickAsync(Commands.SharesCopyInvite, message);
        Assert.Contains("magnet:?xt=urn:btih:", _clipboard.Text);
    }

    [Fact]
    public async Task The_copied_invite_is_a_link_other_apps_open_and_the_app_reads_it_back_from_a_whole_message()
    {
        var id = await ShareAFileAsync("for other apps.bin");

        await ClickAsync(Commands.SharesCopyInvite, new CopyInviteArgs(id, "Hi! Open this to get the file: {link}\nSee you"));
        var message = _clipboard.Text!;

        // Any torrent app reads this: the content, its name and its size, in the usual words.
        var link = message[message.IndexOf("magnet:?", StringComparison.Ordinal)..].Split('\n')[0];
        Assert.True(global::MonoTorrent.MagnetLink.TryParse(link, out var magnet), link);
        Assert.Equal("for other apps.bin", magnet!.Name);
        Assert.True(magnet.Size > 0, link);

        // And ComParta reads its own message back: the receive dialog finds it and shows the file.
        _clipboard.Holds = message;
        Assert.Equal(link, (await ClickAsync(Commands.ClipboardFindInvite)).GetString());
        var preview = await ClickAsync(Commands.TransfersPreview, new ReceiveArgs(message));
        Assert.Equal("for other apps.bin", preview.GetProperty("name").GetString());
        Assert.Equal(magnet.Size, preview.GetProperty("sizeBytes").GetInt64());
    }

    [Fact]
    public async Task Your_name_and_colors_chosen_in_settings_are_shown_and_kept_after_a_restart()
    {
        await ClickAsync(Commands.SettingsSet, new SetSettingsArgs(null, null, null, 0, false, null, YourName: "Ana", Theme: "dark", WelcomeDone: true));

        await ScreenShowsAsync(Events.SettingsChanged, s => s.GetProperty("yourName").GetString() == "Ana" && s.GetProperty("theme").GetString() == "dark");
        var shown = await ClickAsync(Commands.SettingsGet);
        Assert.Equal(("Ana", "dark"), (shown.GetProperty("yourName").GetString(), shown.GetProperty("theme").GetString()));

        // The app opens the same stored choices next time.
        var store = await SqliteStore.OpenAsync(_paths.DatabasePath);
        var next = await new SettingsUseCases(store, new FakeSystem("en")).GetAsync();
        Assert.Equal(("Ana", "dark", true), (next.YourName, next.Theme, next.WelcomeDone));

        var json = await _router.InvokeAsync(Commands.SettingsSet, JsonSerializer.Serialize(new { theme = "blue" }, Contract.Json)).WaitAsync(Quick);
        Assert.Equal(ErrorCodes.SettingsInvalid, JsonDocument.Parse(json).RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_help_page_copies_a_report_with_the_last_problems_and_no_file_names()
    {
        await ShareAFileAsync("Family secrets.bin");
        var json = await _router.InvokeAsync(Commands.SharesRemove, JsonSerializer.Serialize(new { id = Guid.NewGuid() }, Contract.Json)).WaitAsync(Quick);
        Assert.Equal(ErrorCodes.ShareNotFound, JsonDocument.Parse(json).RootElement.GetProperty("error").GetProperty("code").GetString());

        var report = (await ClickAsync(Commands.SupportReport)).GetString()!;

        Assert.Equal(report, _clipboard.Text);
        Assert.Contains("Shared: 1 (", report);
        Assert.Contains("UTC  share.notFound\n", report);
        Assert.DoesNotContain("secrets", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_folder, report);

        // The problems are kept in the app's own data, so they are still in the report after a restart.
        var store = await SqliteStore.OpenAsync(_paths.DatabasePath);
        var settings = new SettingsUseCases(store, new FakeSystem("en"));
        var again = await new SupportUseCases(
            store, new FakeSystem("en"), settings, new ShareUseCases(store, _engine, _picker, _files),
            new TransferUseCases(store, _engine), new ConnectionUseCases(_engine)).ReportAsync();
        Assert.Contains("UTC  share.notFound\n", again);
    }

    [Fact]
    public async Task About_shows_the_licenses_of_the_parts_ComParta_uses()
    {
        var text = (await ClickAsync(Commands.AboutNotices)).GetString()!;

        Assert.Matches(new Regex(@"^MonoTorrent \S+\nLicense: MIT$", RegexOptions.Multiline), text);
        Assert.Matches(new Regex(@"^react \S+\nLicense: MIT$", RegexOptions.Multiline), text);
        Assert.Matches(new Regex(@"^lucide-react \S+\nLicense: ISC$", RegexOptions.Multiline), text);
        Assert.DoesNotContain(_folder, text);
    }

    [Fact]
    public async Task Public_servers_chosen_in_settings_announce_the_shared_file_and_go_in_its_invite()
    {
        using var tracker = new TrackerServer { AllowUnregisteredTorrents = true };
        int port;
        using (var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            port = ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
        var listener = TrackerListenerFactory.CreateUdp(IPAddress.Loopback, port);
        tracker.RegisterListener(listener);
        listener.Start();
        try
        {
            var server = $"udp://127.0.0.1:{port}";
            await ClickAsync(Commands.SettingsSet, new SetSettingsArgs(null, null, null, 0, false, [server]));
            await ScreenShowsAsync(Events.SettingsChanged, s => s.GetProperty("publicServers").GetArrayLength() == 1);

            var id = await ShareAFileAsync("public.bin");

            var until = DateTime.UtcNow + Timeout;
            while (tracker.GetTrackerItems().Sum(item => item.Count) == 0)
            {
                Assert.True(DateTime.UtcNow < until, "The shared file was never announced to the public server.");
                await Task.Delay(100);
            }
            await ClickAsync(Commands.SharesCopyInvite, new CopyInviteArgs(id, null));
            Assert.StartsWith("magnet:?", _clipboard.Text);
            Assert.Contains("tr=" + Uri.EscapeDataString(server), _clipboard.Text, StringComparison.Ordinal);
        }
        finally
        {
            // The engine first: stopping says goodbye to the public server, which must still be there to answer.
            await _engine.StopAsync().WaitAsync(Quick);
            listener.Stop();
        }
    }

    [Fact]
    public async Task A_helper_chosen_in_settings_passes_a_shared_file_on_and_stop_sharing_takes_it_back()
    {
        await using var helper = await StartHelperAsync();
        var id = await ShareThroughHelperAsync(helper, "through-helper.bin");

        await ClickAsync(Commands.SharesCopyInvite, new CopyInviteArgs(id, null));
        Assert.Contains("tr=" + Uri.EscapeDataString(helper.TrackerUri.OriginalString), _clipboard.Text, StringComparison.Ordinal);

        await StopSendingAsync();
        await ReceiveOnlyFromHelperAsync("through-helper.bin", receiver => receiver.ReceiveAsync(TransferId.New(), _clipboard.Text!));

        // Stop sharing takes the file back from the helper.
        await ClickAsync(Commands.SharesRemove, new IdArgs(id));
        await ScreenShowsAsync(Events.SharesChanged, shares => shares.GetArrayLength() == 0);
        var until = DateTime.UtcNow + Timeout;
        while (helper.Files.Count > 0)
        {
            Assert.True(DateTime.UtcNow < until, "The helper kept the file after Stop sharing.");
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task A_saved_download_file_names_the_helper_and_another_app_gets_the_file_from_it()
    {
        await using var helper = await StartHelperAsync();
        var id = await ShareThroughHelperAsync(helper, "download-file.bin");

        // "Save download file": only the name comes back, and the folder shows the file selected.
        var name = (await ClickAsync(Commands.SharesSaveDownloadFile, new IdArgs(id))).GetString();
        Assert.Equal("download-file.bin.torrent", name);
        var content = _files.Contents[Path.Combine(_paths.SavedRoot, name!)];
        Assert.Equal(helper.TrackerUri.OriginalString, MonoTorrent.Torrent.Load(content).AnnounceUrls[0][0]);

        await StopSendingAsync();
        await ReceiveOnlyFromHelperAsync("download-file.bin", receiver => receiver.ReceiveFromFileAsync(TransferId.New(), content));
    }

    private async Task<ComParta.Helper.HelperHost> StartHelperAsync()
    {
        // Tried across a range: Windows reserves blocks of ports for UDP that the system's own TCP
        // choices can fall in, and then no such choice is free on UDP.
        int udpAndTcp;
        do udpAndTcp = Random.Shared.Next(20_000, 45_000); while (!UdpFree(udpAndTcp) || !TcpFree(udpAndTcp));
        return await ComParta.Helper.HelperHost.StartAsync(new ComParta.Helper.HelperOptions
        {
            PublicAddress = "127.0.0.1",
            Key = "a-helper-key-for-the-wiring-test",
            PeerPort = FreePort(),
            TrackerPort = udpAndTcp,
            ControlPort = FreePort(),
            DataFolder = Path.Combine(_folder, "helper"),
        }, _folder);
    }

    /// <summary>Saves the helper in Settings, shares a file, and waits until the helper holds all of it.</summary>
    private async Task<string> ShareThroughHelperAsync(ComParta.Helper.HelperHost helper, string name)
    {
        // The person types the helper's address and key in Settings and saves.
        await ClickAsync(Commands.SettingsSet, new SetSettingsArgs(
            null, null, null, 0, false, [], helper.ControlUri.AbsoluteUri, "a-helper-key-for-the-wiring-test"));
        await ScreenShowsAsync(Events.SettingsChanged, s => s.GetProperty("helperAddress").GetString() == helper.ControlUri.AbsoluteUri);

        var id = await ShareAFileAsync(name);

        // The helper gets the whole file, and the connection says others get it through the helper.
        var until = DateTime.UtcNow + Timeout;
        while (helper.Files.Count != 1 || helper.Files[0].Percent < 100)
        {
            Assert.True(DateTime.UtcNow < until, "The helper never got the whole shared file.");
            await Task.Delay(100);
        }
        await ScreenShowsAsync(Events.ConnectionChanged, c => c.GetProperty("throughHelper").GetBoolean()
            && c.GetProperty("health").GetString() == "good");
        return id;
    }

    /// <summary>This computer stops sending, so a file can only come from the helper.</summary>
    private async Task StopSendingAsync()
    {
        await ClickAsync(Commands.ActivitySet, new SetActivityArgs(SendingStopped: true, ReceivingStopped: false));
        await ScreenShowsAsync(Events.ActivityChanged, a => a.GetProperty("sendingStopped").GetBoolean());
    }

    /// <summary>Another app that takes no connections starts receiving, and gets a byte-identical file.</summary>
    private async Task ReceiveOnlyFromHelperAsync(string name, Func<MonoTorrentTransferEngine, Task<bool>> start)
    {
        var receiverPaths = new FakePaths(Path.Combine(_folder, "receiver"));
        await using var receiver = new MonoTorrentTransferEngine(receiverPaths) { AcceptIncoming = false };
        await receiver.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort(), AllowServersInDownloadLinks = true });
        var done = new TaskCompletionSource<ReceiveProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.ReceiveProgressed += progress =>
        {
            if (progress.Done || progress.Failed)
                done.TrySetResult(progress);
        };
        Assert.True(await start(receiver));
        Assert.True((await done.Task.WaitAsync(Timeout)).Done);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(_folder, "originals", name)))),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(receiverPaths.ReceivedRoot, name)))));
    }

    private static bool TcpFree(int port)
    {
        try
        {
            var tcp = new TcpListener(IPAddress.Any, port);
            tcp.Start();
            tcp.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static bool UdpFree(int port)
    {
        try
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>Another computer offers these files as one; returns it and the link to them.</summary>
    private async Task<(MonoTorrentTransferEngine Sender, ShareId Id, string Link)> OfferElsewhereAsync(
        string name, params (string Path, int Size)[] files)
    {
        var originals = Path.Combine(_folder, "elsewhere", name);
        var items = files.Select(file =>
        {
            var path = Path.Combine(originals, file.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(file.Size));
            return new ShareItem(file.Path, file.Size, path);
        }).ToArray();
        var sender = new MonoTorrentTransferEngine(new FakePaths(Path.Combine(_folder, "sender-" + name)));
        await sender.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort() });
        var id = ShareId.New();
        var offered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sender.OfferChanged += (changed, state) =>
        {
            if (changed == id && state == OfferState.Ready)
                offered.TrySetResult();
        };
        if (items.Length == 1)
            await sender.OfferAsync(id, items[0].ContentRef);
        else
            await sender.OfferItemsAsync(id, name, items);
        await offered.Task.WaitAsync(Timeout);
        return (sender, id, (await sender.GetInviteAsync(id))!);
    }

    private static string Checksum(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private async Task<JsonElement> OptionsShowFilesAsync(string id)
    {
        var until = DateTime.UtcNow + Timeout;
        while (true)
        {
            var options = await ClickAsync(Commands.TransfersOptions, new IdArgs(id));
            if (options.GetProperty("files").ValueKind == JsonValueKind.Array)
                return options;
            Assert.True(DateTime.UtcNow < until, "More options never listed the files.");
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task A_file_unticked_under_More_options_never_arrives_and_the_rest_does()
    {
        // Slow enough that the person unticks the file long before everything is there.
        await ClickAsync(Commands.SettingsSet, new SetSettingsArgs(null, null, null, 0, false, null, WelcomeDone: true, ReceiveLimit: 300_000));
        var (sender, _, link) = await OfferElsewhereAsync(
            "Nature films", ("forest.mkv", 1_000_000), ("Extras/making-of.mkv", 900_000), ("subtitles.srt", 30_000));
        try
        {
            var id = (await ClickAsync(Commands.TransfersReceive, new ReceiveArgs(link))).GetProperty("id").GetString()!;
            TransferId.TryParse(id, out var transferId);
            await _engine.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));

            var files = (await OptionsShowFilesAsync(id)).GetProperty("files").EnumerateArray().ToList();
            var extras = files.Single(f => f.GetProperty("path").GetString() == "Extras/making-of.mkv").GetProperty("index").GetInt32();
            await ClickAsync(Commands.TransfersSetOptions, new SetTransferOptionsArgs(id, [extras], null));

            var shown = await ScreenShowsAsync(Events.TransfersChanged, list => list.EnumerateArray().Any(t =>
                t.GetProperty("id").GetString() == id && t.GetProperty("state").GetString() == "done"));
            var done = shown.EnumerateArray().Single(t => t.GetProperty("id").GetString() == id);
            Assert.Equal(1_030_000, done.GetProperty("sizeBytes").GetInt64());
            var received = Path.Combine(_paths.ReceivedRoot, "Nature films");
            Assert.Equal(Checksum(Path.Combine(_folder, "elsewhere", "Nature films", "forest.mkv")), Checksum(Path.Combine(received, "forest.mkv")));
            Assert.True(File.Exists(Path.Combine(received, "subtitles.srt")));
            Assert.False(File.Exists(Path.Combine(received, "Extras", "making-of.mkv")));
            // The choice is in the database too, so a restart would have kept it.
            var store = await SqliteStore.OpenAsync(_paths.DatabasePath);
            Assert.Equal([extras], Assert.Single(await ((ITransferRepository)store).GetAllAsync()).SkippedFiles!);
        }
        finally { await sender.StopAsync(); }
    }

    [Fact]
    public async Task A_download_that_arrived_is_shared_on_until_turned_off_and_then_deletes_at_once()
    {
        var (sender, shareId, link) = await OfferElsewhereAsync("documentary.mkv", ("documentary.mkv", 600_000));
        var third = new MonoTorrentTransferEngine(new FakePaths(Path.Combine(_folder, "third")));
        try
        {
            var id = (await ClickAsync(Commands.TransfersReceive, new ReceiveArgs(link))).GetProperty("id").GetString()!;
            TransferId.TryParse(id, out var transferId);
            Assert.True((await ClickAsync(Commands.TransfersOptions, new IdArgs(id))).GetProperty("keepSharing").GetBoolean());
            await _engine.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));
            await ScreenShowsAsync(Events.TransfersChanged, list => list.EnumerateArray().Any(t =>
                t.GetProperty("id").GetString() == id && t.GetProperty("state").GetString() == "done"));
            Assert.True((await ClickAsync(Commands.TransfersOptions, new IdArgs(id))).GetProperty("sharing").GetBoolean());

            // The first computer goes away; a third one gets the whole file from this one.
            await sender.StopOfferingAsync(shareId);
            await third.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort() });
            var thirdDone = new TaskCompletionSource<ReceiveProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
            third.ReceiveProgressed += p => { if (p.Done || p.Failed) thirdDone.TrySetResult(p); };
            var thirdId = TransferId.New();
            Assert.True(await third.ReceiveAsync(thirdId, link));
            await third.ConnectAsync(thirdId, new IPEndPoint(IPAddress.Loopback, _engine.ListenEndPoint!.Port));
            Assert.True((await thirdDone.Task.WaitAsync(Timeout)).Done);
            var details = await ClickAsync(Commands.TransfersDetails, new IdArgs(id));
            Assert.True(details.GetProperty("bytesSent").GetInt64() >= 600_000);

            // Turning it off under More options stops sharing; the file stays.
            await ClickAsync(Commands.TransfersSetOptions, new SetTransferOptionsArgs(id, null, false));
            var options = await ClickAsync(Commands.TransfersOptions, new IdArgs(id));
            Assert.False(options.GetProperty("sharing").GetBoolean());
            Assert.False(options.GetProperty("keepSharing").GetBoolean());
            var received = Path.Combine(_paths.ReceivedRoot, "documentary.mkv");
            Assert.True(File.Exists(received));

            await ClickAsync(Commands.ReceivedDelete, new IdArgs(id));

            Assert.False(File.Exists(received));
            await ScreenShowsAsync(Events.TransfersChanged, list => list.GetArrayLength() == 0);
        }
        finally
        {
            await third.StopAsync();
            await sender.StopAsync();
        }
    }

    [Fact]
    public async Task Deleting_a_download_still_shared_stops_sharing_it_first()
    {
        var (sender, _, link) = await OfferElsewhereAsync("concert.mkv", ("concert.mkv", 300_000));
        try
        {
            var id = (await ClickAsync(Commands.TransfersReceive, new ReceiveArgs(link))).GetProperty("id").GetString()!;
            TransferId.TryParse(id, out var transferId);
            await _engine.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));
            await ScreenShowsAsync(Events.TransfersChanged, list => list.EnumerateArray().Any(t =>
                t.GetProperty("id").GetString() == id && t.GetProperty("state").GetString() == "done"));
            Assert.True(_engine.IsSharingReceived(transferId));

            // On Windows a file still shared is held open; Delete has to let go of it first.
            await ClickAsync(Commands.ReceivedDelete, new IdArgs(id));

            Assert.False(File.Exists(Path.Combine(_paths.ReceivedRoot, "concert.mkv")));
            Assert.False(_engine.IsSharingReceived(transferId));
        }
        finally { await sender.StopAsync(); }
    }

    [Fact]
    public async Task A_download_file_opened_in_Windows_opens_Get_this_file_and_receives_it()
    {
        var (sender, shareId, _) = await OfferElsewhereAsync("open-me.mkv", ("open-me.mkv", 250_000));
        try
        {
            var downloadFile = (await sender.GetDownloadFileAsync(shareId))!;
            var path = Path.Combine(_folder, "Downloads", "open-me.mkv.torrent");
            _files.Contents[path] = downloadFile.Content;

            // Windows starts ComParta with the file; the running app hands it to the screen.
            Assert.True(_opened.Open(["ComParta.exe", path]));
            await ScreenShowsAsync(Events.InvitesChanged, waiting => waiting.GetBoolean());
            Assert.Equal(JsonValueKind.Undefined, (await ClickAsync(Commands.InvitesTakeOpened)).ValueKind);
            var preview = await ClickAsync(Commands.TransfersTakeOpenedFile);
            Assert.Equal("open-me.mkv", preview.GetProperty("name").GetString());
            Assert.Equal(250_000, preview.GetProperty("sizeBytes").GetInt64());

            var id = (await ClickAsync(Commands.TransfersReceiveOpenedFile)).GetProperty("id").GetString()!;
            TransferId.TryParse(id, out var transferId);
            await _engine.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, sender.ListenEndPoint!.Port));

            await ScreenShowsAsync(Events.TransfersChanged, list => list.EnumerateArray().Any(t =>
                t.GetProperty("id").GetString() == id && t.GetProperty("state").GetString() == "done"));
            Assert.Equal("downloadFile", (await ClickAsync(Commands.TransfersDetails, new IdArgs(id))).GetProperty("source").GetString());
            Assert.True(File.Exists(Path.Combine(_paths.ReceivedRoot, "open-me.mkv")));
        }
        finally { await sender.StopAsync(); }
    }

    [Fact]
    public async Task A_download_link_clicked_in_the_browser_opens_Get_this_file()
    {
        const string link = "magnet:?xt=urn:btih:5123456789abcdef0123456789abcdef01234567&dn=Open%20film.mkv&xl=1000";

        Assert.True(_opened.Open(["ComParta.exe", link]));

        await ScreenShowsAsync(Events.InvitesChanged, waiting => waiting.GetBoolean());
        var taken = (await ClickAsync(Commands.InvitesTakeOpened)).GetString()!;
        var preview = await ClickAsync(Commands.TransfersPreview, new ReceiveArgs(taken));
        Assert.Equal("Open film.mkv", preview.GetProperty("name").GetString());
        Assert.False(preview.GetProperty("isInvite").GetBoolean());
        Assert.Equal(JsonValueKind.Undefined, (await ClickAsync(Commands.TransfersTakeOpenedFile)).ValueKind);
    }
}
