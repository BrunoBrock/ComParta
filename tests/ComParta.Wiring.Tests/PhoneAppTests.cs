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
/// The phone app working with the rest of the phone (epic 7), through the app wired as on Android:
/// what the phone hands the app (a tapped link, a shared message) goes where the Android activity
/// hands it, and from there the screen's commands as JSON go through the real router, use cases,
/// database and torrent engine to a file a computer offers.
/// </summary>
public sealed class PhoneAppTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Quick = TimeSpan.FromSeconds(8);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "comparta-tests", Guid.NewGuid().ToString("N"));
    private readonly FakePhoneStorage _phone = new();
    private readonly FakeFilePicker _picker = new();
    private readonly List<MonoTorrentTransferEngine> _engines = [];
    // As in AppServices: what the phone opens the app with is read with the engine's own rules.
    private readonly OpenedInvites _opened = new(MonoTorrentTransferEngine.FindLinkIn, MonoTorrentTransferEngine.DownloadFileExtensions);
    private readonly ConcurrentQueue<JsonElement> _events = new();
    private FakePaths _paths = null!;
    private BridgeRouter _router = null!;
    private MonoTorrentTransferEngine _engine = null!;

    private sealed class NoClipboard : IClipboard
    {
        public Task SetTextAsync(string text, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string?> GetTextAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private sealed class NoNotices : IThirdPartyNotices
    {
        public Task<string?> ReadAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    /// <summary>As in AppServices.StartAsync with the Android parts.</summary>
    public async Task InitializeAsync()
    {
        _paths = new FakePaths(Path.Combine(_folder, "phone"));
        Directory.CreateDirectory(_paths.ReceivedRoot);
        var store = await SqliteStore.OpenAsync(_paths.DatabasePath);
        var engine = _engine = new MonoTorrentTransferEngine(_paths) { Content = _phone };
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
        _router = new BridgeRouter(
            shares, transfers, settings, connection, _opened, activity, received, new NoClipboard(), opener, thumbnails,
            new DroppedFiles(), sending, notifications, background, new MediaUseCases(shares, received, _phone),
            new SupportUseCases(store, new FakeSystem("pt-BR"), settings, shares, transfers, connection),
            new AboutUseCases(new NoNotices()));
        _router.EventRaised += json => _events.Enqueue(JsonDocument.Parse(json).RootElement.Clone());

        await engine.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort() });
        await settings.StartAsync();
        await activity.StartAsync();
        await shares.StartAsync();
        await transfers.StartAsync();
        await notifications.StartAsync();
        await background.StartAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var engine in _engines)
            await engine.StopAsync().WaitAsync(Quick);
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private async Task<JsonElement> InvokeAsync(string command, object? args = null) =>
        JsonDocument.Parse(await _router.InvokeAsync(command, args is null ? "null" : JsonSerializer.Serialize(args, Contract.Json)).WaitAsync(Quick)).RootElement;

    private async Task<JsonElement> ClickAsync(string command, object? args = null)
    {
        var result = await InvokeAsync(command, args);
        Assert.True(result.GetProperty("ok").GetBoolean(), result.ToString());
        return result.TryGetProperty("value", out var value) ? value : default;
    }

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

    /// <summary>A computer shares a file and copies its invite; it stays running for the phone to get the file.</summary>
    private async Task<(MonoTorrentTransferEngine Computer, string Invite, byte[] Bytes)> ComputerSharesAsync(string name, int size)
    {
        var originals = Path.Combine(_folder, "computer-files");
        Directory.CreateDirectory(originals);
        var file = Path.Combine(originals, name);
        var bytes = RandomNumberGenerator.GetBytes(size);
        File.WriteAllBytes(file, bytes);
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
        await computer.OfferAsync(shareId, file);
        await offered.Task.WaitAsync(Timeout);
        return (computer, (await computer.GetInviteAsync(shareId))!, bytes);
    }

    /// <summary>
    /// What the screen does when the app says an invite is waiting: takes it, and shows "Get this
    /// file?" with what the preview says. Returns the invite the confirmation is for.
    /// </summary>
    private async Task<(string Invite, JsonElement Preview)> ScreenAsksAsync()
    {
        await ScreenShowsAsync(Events.InvitesChanged, waiting => waiting.GetBoolean());
        var invite = (await ClickAsync(Commands.InvitesTakeOpened)).GetString()!;
        var preview = await ClickAsync(Commands.TransfersPreview, new ReceiveArgs(invite));
        return (invite, preview);
    }

    /// <summary>The person taps "Get it" on the confirmation; returns where the file arrived.</summary>
    private async Task<string> AcceptAsync(string invite, MonoTorrentTransferEngine computer, string name)
    {
        // Nothing was received before the tap.
        Assert.Equal(0, (await ClickAsync(Commands.TransfersList)).GetArrayLength());
        var id = (await ClickAsync(Commands.TransfersReceive, new ReceiveArgs(invite))).GetProperty("id").GetString()!;
        Assert.True(TransferId.TryParse(id, out var transferId));
        await _engine.ConnectAsync(transferId, new IPEndPoint(IPAddress.Loopback, computer.ListenEndPoint!.Port));
        await ScreenShowsAsync(Events.TransfersChanged, list => list.EnumerateArray().Any(t =>
            t.GetProperty("id").GetString() == id && t.GetProperty("state").GetString() == "done"));
        return Path.Combine(_paths.ReceivedRoot, name);
    }

    private static string Checksum(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    [Fact]
    public async Task A_link_tapped_in_an_email_opens_Get_this_file_and_the_tap_on_it_receives_the_file()
    {
        var (computer, invite, bytes) = await ComputerSharesAsync("Natal.mp4", 400_000);

        // MainActivity hands the tapped link (the intent's data) to the app as an opened link.
        Assert.True(_opened.Open([invite]));

        var (asked, preview) = await ScreenAsksAsync();
        Assert.Equal("Natal.mp4", preview.GetProperty("name").GetString());
        var arrived = await AcceptAsync(asked, computer, "Natal.mp4");

        Assert.Equal(Checksum(bytes), Checksum(File.ReadAllBytes(arrived)));
    }

    [Fact]
    public async Task A_chat_message_shared_to_the_app_opens_Get_this_file_for_the_invite_inside_it()
    {
        var (computer, invite, bytes) = await ComputerSharesAsync("Aniversário.mp4", 300_000);

        // As WhatsApp shares a message: the invite with words around it, on several lines.
        Assert.True(_opened.OpenMessage($"Oi vó! Segue o vídeo:\n{invite}\nBeijos"));

        var (asked, preview) = await ScreenAsksAsync();
        Assert.Equal("Aniversário.mp4", preview.GetProperty("name").GetString());
        var arrived = await AcceptAsync(asked, computer, "Aniversário.mp4");

        Assert.Equal(Checksum(bytes), Checksum(File.ReadAllBytes(arrived)));
    }

    [Fact]
    public async Task A_message_shared_to_the_app_without_an_invite_is_answered_plainly_and_nothing_is_received()
    {
        Assert.False(_opened.OpenMessage("Até domingo!"));

        await ScreenShowsAsync(Events.InvitesChanged, waiting => waiting.GetBoolean());
        var answer = await InvokeAsync(Commands.InvitesTakeOpened);

        Assert.Equal(ErrorCodes.InviteNotInMessage, answer.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, (await ClickAsync(Commands.TransfersList)).GetArrayLength());
    }
}
