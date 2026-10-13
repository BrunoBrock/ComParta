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
        public Task<string?> GetTextAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    public async Task InitializeAsync()
    {
        var paths = new FakePaths(_folder);
        Directory.CreateDirectory(paths.ReceivedRoot);
        var store = await SqliteStore.OpenAsync(paths.DatabasePath);
        _engine = new MonoTorrentTransferEngine(paths, new RouterOnTheNetwork(new SlowRouter()));

        // As in AppServices.StartAsync.
        var shares = new ShareUseCases(store, _engine, _picker, _files);
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
        Assert.StartsWith("Hi! Open this to get the file: comparta://", _clipboard.Text);

        await ClickAsync(Commands.ActivitySet, new SetActivityArgs(SendingStopped: true, ReceivingStopped: true));
        await ClickAsync(Commands.SharesCopyInvite, message with { Message = null });
        Assert.StartsWith("comparta://", _clipboard.Text);

        await ClickAsync(Commands.ActivitySet, new SetActivityArgs(SendingStopped: false, ReceivingStopped: false));
        await ClickAsync(Commands.SharesPause, new IdArgs(id));
        await ClickAsync(Commands.SharesCopyInvite, message);
        Assert.Contains("comparta://", _clipboard.Text);
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
            Assert.True(InviteCodec.TryRead(_clipboard.Text, out var invite));
            Assert.Contains("tr=" + Uri.EscapeDataString(server), invite.EngineText, StringComparison.Ordinal);
        }
        finally
        {
            listener.Stop();
        }
    }
}
