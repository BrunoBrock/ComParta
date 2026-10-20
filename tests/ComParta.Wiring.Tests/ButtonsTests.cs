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

    public async Task InitializeAsync()
    {
        var paths = _paths = new FakePaths(_folder);
        Directory.CreateDirectory(paths.ReceivedRoot);
        var store = await SqliteStore.OpenAsync(paths.DatabasePath);
        _engine = new MonoTorrentTransferEngine(paths, new RouterOnTheNetwork(new SlowRouter()));

        // As in AppServices.StartAsync.
        var shares = new ShareUseCases(store, _engine, _picker, _files, paths: paths);
        var transfers = new TransferUseCases(store, _engine, picker: _picker, files: _files, storage: new FakeStorage());
        var settings = new SettingsUseCases(store, new FakeSystem("en"), _engine);
        var connection = new ConnectionUseCases(_engine, new FakeFirewall());
        var activity = new ActivityUseCases(_engine, store);
        var opener = new FakeOpener();
        var received = new ReceivedUseCases(transfers, paths, opener, _files);
        var thumbnails = new ThumbnailUseCases(shares, received, new FakeThumbnails());
        var sending = new SendingUseCases(_engine, shares);
        var notifications = new NotificationUseCases(_engine, transfers, shares, sending, settings, new FakeNotifier(), new FakeWindow());
        var background = new BackgroundUseCases(_engine, shares, transfers, sending, settings, store, notifications, new FakePresence());
        _router = new BridgeRouter(
            shares, transfers, settings, connection, new OpenedInvites(), activity, received, _clipboard, opener, thumbnails,
            new DroppedFiles(), sending, notifications, background);
        _router.EventRaised += json => _events.Enqueue(JsonDocument.Parse(json).RootElement.Clone());
        _router.CommandFailed += (_, e) => _failure = e;

        await _engine.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort(), AllowPortForwarding = true });
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

    private async Task<string> ShareAFileAsync(string name)
    {
        var folder = Path.Combine(_folder, "originals");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(200_000));
        _files.Files[path] = 200_000;
        _picker.NextPick = path;

        var id = (await ClickAsync(Commands.SharesAdd)).GetProperty("id").GetString()!;
        await ScreenShowsAsync(Events.SharesChanged, shares => shares.EnumerateArray().Any(s =>
            s.GetProperty("id").GetString() == id && s.GetProperty("status").GetString() == "ready"));
        // Once the router has mapped the port, stopping the last file used to wait for it to unmap.
        for (var i = 0; i < 100 && _engine.PortMapper!.Mappings.Created.Count == 0; i++)
            await Task.Delay(50);
        return id;
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
}
