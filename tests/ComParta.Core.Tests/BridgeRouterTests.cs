using System.Text.Json;
using ComParta.Contracts;
using ComParta.Core.Ports;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

/// <summary>The whole contract, exercised as JSON the way the UI does, with no window.</summary>
public sealed class BridgeRouterTests
{
    // A rooted path on whichever system runs the tests, so the full-path check leaves it alone.
    private static readonly string Video = Path.Combine(Path.GetTempPath(), "Videos", "birthday.mp4");

    private readonly FakeEngine _engine = new();
    private readonly FakeFilePicker _picker = new();
    private readonly FakeFileSystem _files = new();
    private readonly FakeShell _shell = new();
    private readonly FakeSettingsRepository _settings = new();
    private readonly FakeFirewall _firewall = new();
    private readonly OpenedInvites _opened = new();
    private readonly FakeThumbnails _thumbnails = new();
    private readonly DroppedFiles _dropped = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero));
    private readonly List<JsonElement> _events = [];
    private readonly BridgeRouter _router;
    private readonly NotificationUseCases _notifications;
    private readonly FakePresence _presence = new();

    public BridgeRouterTests()
    {
        var shares = new ShareUseCases(new FakeShareRepository(), _engine, _picker, _files);
        var transfers = new TransferUseCases(new FakeTransferRepository(), _engine, _clock, picker: _picker, files: _files);
        var settings = new SettingsUseCases(_settings, new FakeSystem("pt-BR"));
        var connection = new ConnectionUseCases(_engine, _firewall);
        var activity = new ActivityUseCases(_engine, _settings);
        var received = new ReceivedUseCases(transfers, new FakePaths(Path.GetTempPath()), _shell, _files);
        var thumbnails = new ThumbnailUseCases(shares, received, _thumbnails);
        var sending = new SendingUseCases(_engine, shares, _clock);
        _notifications = new NotificationUseCases(_engine, transfers, shares, sending, settings, new FakeNotifier(), new FakeWindow());
        _router = new BridgeRouter(shares, transfers, settings, connection, _opened, activity, received, _shell, _shell, thumbnails, _dropped,
            sending, _notifications,
            new BackgroundUseCases(_engine, shares, transfers, sending, settings, _settings, _notifications, _presence, _clock));
        _router.EventRaised += json => _events.Add(JsonDocument.Parse(json).RootElement);
        _files.Files[Video] = 5_000;
        _picker.NextPick = Video;
    }

    private sealed class FakeShell : IClipboard, IContentOpener
    {
        public string? Copied { get; private set; }
        public string? OnClipboard { get; set; }
        public Task<string?> GetTextAsync(CancellationToken ct = default) => Task.FromResult(OnClipboard);
        public int Opened { get; private set; }
        public Task SetTextAsync(string text, CancellationToken ct = default) { Copied = text; return Task.CompletedTask; }
        public Task OpenReceivedFolderAsync(CancellationToken ct = default) { Opened++; return Task.CompletedTask; }
        public Task<bool> OpenAsync(string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task RevealAsync(string path, CancellationToken ct = default) => Task.CompletedTask;
    }

    private async Task<JsonElement> InvokeAsync(string command, object? args = null) =>
        JsonDocument.Parse(await _router.InvokeAsync(command, args is null ? "null" : JsonSerializer.Serialize(args))).RootElement;

    private static string ErrorCode(JsonElement result)
    {
        Assert.False(result.GetProperty("ok").GetBoolean());
        return result.GetProperty("error").GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task Add_list_and_remove_a_share()
    {
        var added = await InvokeAsync(Commands.SharesAdd);
        Assert.True(added.GetProperty("ok").GetBoolean());
        var share = added.GetProperty("value");
        Assert.Equal("birthday.mp4", share.GetProperty("name").GetString());
        Assert.Equal(5_000, share.GetProperty("sizeBytes").GetInt64());

        var listed = (await InvokeAsync(Commands.SharesList)).GetProperty("value");
        Assert.Equal("ready", listed[0].GetProperty("status").GetString());
        // No path ever crosses the bridge (AD-7).
        Assert.DoesNotContain("Videos", listed[0].GetRawText());

        var id = share.GetProperty("id").GetString();
        Assert.True((await InvokeAsync(Commands.SharesRemove, new { id })).GetProperty("ok").GetBoolean());
        Assert.Equal(0, (await InvokeAsync(Commands.SharesList)).GetProperty("value").GetArrayLength());
    }

    [Fact]
    public async Task Cancelling_the_file_dialog_is_ok_with_no_value()
    {
        _picker.NextPick = null;

        var result = await InvokeAsync(Commands.SharesAdd);

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.False(result.TryGetProperty("value", out var value) && value.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task Errors_come_back_as_codes()
    {
        await InvokeAsync(Commands.SharesAdd);

        Assert.Equal(ErrorCodes.ShareDuplicate, ErrorCode(await InvokeAsync(Commands.SharesAdd)));
        Assert.Equal(ErrorCodes.ShareNotFound, ErrorCode(await InvokeAsync(Commands.SharesRemove, new { id = "nonsense" })));
        Assert.Equal(ErrorCodes.InviteInvalid, ErrorCode(await InvokeAsync(Commands.TransfersReceive, new { invite = "hello" })));
        Assert.Equal(ErrorCodes.TransferNotFound, ErrorCode(await InvokeAsync(Commands.TransfersRemove, new { id = Guid.NewGuid() })));
        Assert.Equal(ErrorCodes.Unexpected, ErrorCode(await InvokeAsync("no.suchCommand")));
        Assert.Equal(ErrorCodes.Unexpected, ErrorCode(JsonDocument.Parse(await _router.InvokeAsync(Commands.SharesRemove, "{ not json")).RootElement));
    }

    [Fact]
    public void Every_core_error_has_its_own_declared_code()
    {
        var codes = Enum.GetValues<ErrorKind>().Select(BridgeRouter.ToCode).ToList();

        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.Equal(ErrorCodes.All.Order(), codes.Order());
    }

    [Fact]
    public async Task Copying_an_invite_puts_it_on_the_clipboard()
    {
        var id = (await InvokeAsync(Commands.SharesAdd)).GetProperty("value").GetProperty("id").GetString();

        var result = await InvokeAsync(Commands.SharesCopyInvite, new { id, message = "Paulo sends you a file.\n{link}\nOpen it in ComParta." });

        Assert.True(result.GetProperty("ok").GetBoolean());
        var link = InviteCodec.FindLink(_shell.Copied)!;
        Assert.Equal($"Paulo sends you a file.\n{link}\nOpen it in ComParta.", _shell.Copied);
        Assert.True(InviteCodec.TryRead(link, out var invite));
        Assert.Equal(FakeEngine.InvitePrefix + id, invite.EngineText);
        Assert.Equal("birthday.mp4", invite.Hints.Name);
        Assert.Equal(5_000, invite.Hints.SizeBytes);
    }

    [Fact]
    public async Task Without_a_message_only_the_link_is_copied()
    {
        var id = (await InvokeAsync(Commands.SharesAdd)).GetProperty("value").GetProperty("id").GetString();

        await InvokeAsync(Commands.SharesCopyInvite, new { id });

        Assert.Equal(InviteCodec.FindLink(_shell.Copied), _shell.Copied);
    }

    [Fact]
    public async Task An_invite_is_previewed_with_what_it_says_about_the_file()
    {
        var link = InviteCodec.Write("invite:abc", new InviteHints("Paulo", "Natal 2024.mp4", 4_200, 1));

        var preview = (await InvokeAsync(Commands.TransfersPreview, new { invite = $"Oi! {link} 🎄" })).GetProperty("value");

        Assert.Equal("Paulo", preview.GetProperty("senderName").GetString());
        Assert.Equal("Natal 2024.mp4", preview.GetProperty("name").GetString());
        Assert.Equal(4_200, preview.GetProperty("sizeBytes").GetInt64());
        Assert.True(preview.GetProperty("isInvite").GetBoolean());
        Assert.Empty(_engine.Receiving);
        Assert.Equal(ErrorCodes.InviteInvalid, ErrorCode(await InvokeAsync(Commands.TransfersPreview, new { invite = "hello" })));
    }

    [Fact]
    public async Task An_invite_on_the_clipboard_is_found_and_other_text_is_not()
    {
        var link = InviteCodec.Write("invite:abc", InviteHints.None);
        _shell.OnClipboard = "Here you go:\n" + link;

        Assert.Equal(link, (await InvokeAsync(Commands.ClipboardFindInvite)).GetProperty("value").GetString());

        _shell.OnClipboard = "shopping list";
        var nothing = await InvokeAsync(Commands.ClipboardFindInvite);
        Assert.True(nothing.GetProperty("ok").GetBoolean());
        Assert.False(nothing.TryGetProperty("value", out _));
    }

    [Fact]
    public async Task Receiving_reports_progress_through_events()
    {
        var started = await InvokeAsync(Commands.TransfersReceive, new { invite = "invite:abc" });
        var id = Guid.Parse(started.GetProperty("value").GetProperty("id").GetString()!);
        Assert.Equal("connecting", started.GetProperty("value").GetProperty("state").GetString());

        _engine.Report(new ReceiveProgress(new TransferId(id), "a.mp4", 1_000, 250, false));

        var last = _events.Last(e => e.GetProperty("event").GetString() == Events.TransfersChanged).GetProperty("payload")[0];
        Assert.Equal("receiving", last.GetProperty("state").GetString());
        Assert.Equal(250, last.GetProperty("receivedBytes").GetInt64());
        Assert.Equal("a.mp4", last.GetProperty("name").GetString());

        Assert.True((await InvokeAsync(Commands.TransfersRemove, new { id })).GetProperty("ok").GetBoolean());
        Assert.Equal(0, (await InvokeAsync(Commands.TransfersList)).GetProperty("value").GetArrayLength());
    }

    [Fact]
    public async Task A_transfer_can_be_paused_and_continued_and_shows_its_speed()
    {
        var started = await InvokeAsync(Commands.TransfersReceive, new { invite = "invite:abc" });
        var id = started.GetProperty("value").GetProperty("id").GetString()!;
        _engine.Report(new ReceiveProgress(new TransferId(Guid.Parse(id)), "a.mp4", 1_000, 250, false, BytesPerSecond: 50));
        _clock.Now += TimeSpan.FromSeconds(1);
        _engine.Report(new ReceiveProgress(new TransferId(Guid.Parse(id)), "a.mp4", 1_000, 300, false, BytesPerSecond: 50));

        var receiving = (await InvokeAsync(Commands.TransfersList)).GetProperty("value")[0];
        Assert.Equal(50, receiving.GetProperty("bytesPerSecond").GetInt64());
        Assert.Equal(14, receiving.GetProperty("secondsLeft").GetInt64());

        Assert.True((await InvokeAsync(Commands.TransfersPause, new { id })).GetProperty("ok").GetBoolean());
        var paused = (await InvokeAsync(Commands.TransfersList)).GetProperty("value")[0];
        Assert.Equal("paused", paused.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, paused.GetProperty("secondsLeft").ValueKind);

        Assert.True((await InvokeAsync(Commands.TransfersResume, new { id })).GetProperty("ok").GetBoolean());
        Assert.Equal("transfer.notFound", ErrorCode(await InvokeAsync(Commands.TransfersPause, new { id = "nope" })));
    }

    [Fact]
    public async Task A_download_file_is_opened_through_the_file_dialog()
    {
        const string file = @"C:\Downloads\film.download";
        _picker.NextPick = file;
        _files.Contents[file] = "download-file:film"u8.ToArray();

        var opened = await InvokeAsync(Commands.TransfersOpenFile);
        Assert.Equal("connecting", opened.GetProperty("value").GetProperty("state").GetString());

        _files.Contents[file] = "not one"u8.ToArray();
        Assert.Equal("download.invalidFile", ErrorCode(await InvokeAsync(Commands.TransfersOpenFile)));

        _picker.NextPick = null;
        var cancelled = await InvokeAsync(Commands.TransfersOpenFile);
        Assert.True(cancelled.GetProperty("ok").GetBoolean());
        Assert.False(cancelled.TryGetProperty("value", out _));
    }

    [Fact]
    public async Task Share_changes_are_sent_as_events_and_the_folder_can_be_opened()
    {
        await InvokeAsync(Commands.SharesAdd);

        var changed = _events.Last(e => e.GetProperty("event").GetString() == Events.SharesChanged);
        Assert.Equal("birthday.mp4", changed.GetProperty("payload")[0].GetProperty("name").GetString());

        Assert.True((await InvokeAsync(Commands.ReceivedOpenFolder)).GetProperty("ok").GetBoolean());
        Assert.Equal(1, _shell.Opened);
    }

    [Fact]
    public void Every_declared_command_and_event_is_named_area_dot_verb()
    {
        Assert.All(Contract.CommandSpecs, spec => Assert.Matches(@"^[a-z]+\.[a-z][A-Za-z]+$", spec.Name));
        Assert.All(Contract.EventSpecs, spec => Assert.Matches(@"^[a-z]+\.changed$", spec.Name));
    }

    [Fact]
    public async Task The_language_follows_the_system_until_the_person_chooses_one()
    {
        var first = (await InvokeAsync(Commands.SettingsGet)).GetProperty("value");
        Assert.Equal(JsonValueKind.Null, first.GetProperty("language").ValueKind);
        Assert.Equal("pt-BR", first.GetProperty("systemLanguage").GetString());

        Assert.True((await InvokeAsync(Commands.SettingsSet, new { language = "en" })).GetProperty("ok").GetBoolean());

        Assert.Equal("en", (await InvokeAsync(Commands.SettingsGet)).GetProperty("value").GetProperty("language").GetString());
        var changed = Assert.Single(_events, e => e.GetProperty("event").GetString() == Events.SettingsChanged);
        Assert.Equal("en", changed.GetProperty("payload").GetProperty("language").GetString());
        Assert.Equal(ErrorCodes.SettingsInvalid, ErrorCode(await InvokeAsync(Commands.SettingsSet, new { language = "../evil" })));
    }

    [Fact]
    public async Task The_view_and_order_are_stored_with_the_language_and_checked()
    {
        Assert.True((await InvokeAsync(Commands.SettingsSet, new { language = "en", view = "list", sort = "size" }))
            .GetProperty("ok").GetBoolean());

        var stored = (await InvokeAsync(Commands.SettingsGet)).GetProperty("value");
        Assert.Equal("list", stored.GetProperty("view").GetString());
        Assert.Equal("size", stored.GetProperty("sort").GetString());
        Assert.Equal(ErrorCodes.SettingsInvalid, ErrorCode(await InvokeAsync(Commands.SettingsSet, new { view = "table" })));
        Assert.Equal(ErrorCodes.SettingsInvalid, ErrorCode(await InvokeAsync(Commands.SettingsSet, new { sort = "colour" })));
        Assert.Equal("list", (await InvokeAsync(Commands.SettingsGet)).GetProperty("value").GetProperty("view").GetString());

        Assert.True((await InvokeAsync(Commands.SettingsSet, new { language = "en" })).GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, (await InvokeAsync(Commands.SettingsGet)).GetProperty("value").GetProperty("view").ValueKind);
    }

    [Fact]
    public async Task A_shared_video_has_a_thumbnail_as_a_data_url_and_no_path_crosses_the_bridge()
    {
        var id = (await InvokeAsync(Commands.SharesAdd)).GetProperty("value").GetProperty("id").GetString();

        var picture = await InvokeAsync(Commands.ThumbnailsGet, new { kind = "share", id, size = 96 });

        Assert.Equal("data:image/png;base64,AQID", picture.GetProperty("value").GetString());
        Assert.Equal([(Video, 96)], _thumbnails.Asked);
        Assert.Equal(ErrorCodes.ShareNotFound, ErrorCode(await InvokeAsync(Commands.ThumbnailsGet, new { kind = "share", id = "nope", size = 96 })));
        Assert.Equal(ErrorCodes.TransferNotFound, ErrorCode(await InvokeAsync(Commands.ThumbnailsGet, new { kind = "transfer", id = TransferId.New().ToString(), size = 96 })));
    }

    [Fact]
    public async Task Dropped_files_are_shared_by_their_token_and_the_first_problem_is_reported()
    {
        var token = Guid.NewGuid();
        var photo = Path.Combine(Path.GetTempPath(), "Pictures", "beach.jpg");
        _files.Files[photo] = 3_000;
        _dropped.Put(token, [Video, photo, Path.Combine(Path.GetTempPath(), "gone.txt")]);

        var result = (await InvokeAsync(Commands.SharesAddDropped, new { token = token.ToString() })).GetProperty("value");

        Assert.Equal(2, result.GetProperty("added").GetInt32());
        Assert.Equal(ErrorCodes.FileUnreadable, result.GetProperty("problem").GetString());
        Assert.Equal(2, (await InvokeAsync(Commands.SharesList)).GetProperty("value").GetArrayLength());
        Assert.Equal(ErrorCodes.ShareDropFailed, ErrorCode(await InvokeAsync(Commands.SharesAddDropped, new { token = "not-a-token" })));
    }

    [Fact]
    public async Task The_guided_send_count_is_stored_and_kept_within_its_three_runs()
    {
        Assert.Equal(0, (await InvokeAsync(Commands.SettingsGet)).GetProperty("value").GetProperty("sendStepsSeen").GetInt32());

        Assert.True((await InvokeAsync(Commands.SettingsSet, new { sendStepsSeen = 2 })).GetProperty("ok").GetBoolean());
        Assert.Equal(2, (await InvokeAsync(Commands.SettingsGet)).GetProperty("value").GetProperty("sendStepsSeen").GetInt32());

        Assert.Equal(ErrorCodes.SettingsInvalid, ErrorCode(await InvokeAsync(Commands.SettingsSet, new { sendStepsSeen = 4 })));
        Assert.Equal(ErrorCodes.SettingsInvalid, ErrorCode(await InvokeAsync(Commands.SettingsSet, new { sendStepsSeen = -1 })));
        Assert.Equal(2, (await InvokeAsync(Commands.SettingsGet)).GetProperty("value").GetProperty("sendStepsSeen").GetInt32());
    }

    [Fact]
    public async Task Start_with_Windows_is_off_by_default_and_stored_when_chosen()
    {
        Assert.False((await InvokeAsync(Commands.SettingsGet)).GetProperty("value").GetProperty("startWithWindows").GetBoolean());

        Assert.True((await InvokeAsync(Commands.SettingsSet, new { startWithWindows = true })).GetProperty("ok").GetBoolean());

        Assert.True((await InvokeAsync(Commands.SettingsGet)).GetProperty("value").GetProperty("startWithWindows").GetBoolean());
    }

    [Fact]
    public async Task Quitting_names_what_would_stop_and_quits_once_confirmed()
    {
        var id = JsonDocument.Parse(await _router.InvokeAsync(Commands.TransfersReceive, JsonSerializer.Serialize(new { invite = "invite:abc" })))
            .RootElement.GetProperty("value").GetProperty("id").GetString();
        _engine.Report(new ReceiveProgress(TransferId.TryParse(id, out var transfer) ? transfer : default, "film.mp4", 1_000, 10, false));

        var asked = (await InvokeAsync(Commands.AppQuit, new { force = false })).GetProperty("value");
        Assert.False(asked.GetProperty("quitting").GetBoolean());
        Assert.Equal("film.mp4", asked.GetProperty("moving")[0].GetString());
        Assert.Equal(0, _presence.Quits);

        Assert.True((await InvokeAsync(Commands.AppQuit, new { force = true })).GetProperty("value").GetProperty("quitting").GetBoolean());
        Assert.Equal(1, _presence.Quits);
    }

    [Fact]
    public async Task Connection_health_is_read_and_reported_when_it_changes()
    {
        _engine.Reachability = ReachabilityFacts.Unknown with { Listening = true };
        var first = await InvokeAsync(Commands.ConnectionGet);
        Assert.Equal("limited", first.GetProperty("value").GetProperty("health").GetString());

        _engine.Reach(_engine.Reachability with { PortMapped = true, MappedPort = 40_000 });

        var changed = Assert.Single(_events, e => e.GetProperty("event").GetString() == Events.ConnectionChanged);
        Assert.Equal("good", changed.GetProperty("payload").GetProperty("health").GetString());

        _firewall.Allows = false;
        Assert.Equal("blocked", (await InvokeAsync(Commands.ConnectionGet)).GetProperty("value").GetProperty("health").GetString());
    }

    [Fact]
    public async Task The_firewall_settings_open_on_request()
    {
        Assert.True((await InvokeAsync(Commands.ConnectionOpenSettings)).GetProperty("ok").GetBoolean());
        Assert.Equal(1, _firewall.SettingsOpened);
    }

    [Fact]
    public async Task An_invite_opened_from_outside_is_announced_and_taken_once()
    {
        var link = InviteCodec.Write(FakeEngine.InvitePrefix + "abc", InviteHints.None);

        Assert.True(_opened.Open(["ComParta.exe", link]));

        Assert.Single(_events, e => e.GetProperty("event").GetString() == Events.InvitesChanged);
        Assert.Equal(link, (await InvokeAsync(Commands.InvitesTakeOpened)).GetProperty("value").GetString());
        Assert.False((await InvokeAsync(Commands.InvitesTakeOpened)).TryGetProperty("value", out _));
    }

    [Fact]
    public async Task A_share_says_how_many_devices_are_getting_it_and_how_many_finished()
    {
        var id = (await InvokeAsync(Commands.SharesAdd)).GetProperty("value").GetProperty("id").GetString()!;
        var idle = (await InvokeAsync(Commands.SharesList)).GetProperty("value")[0];
        Assert.Equal("idle", idle.GetProperty("activity").GetString());
        Assert.Equal(JsonValueKind.Null, idle.GetProperty("secondsLeft").ValueKind);
        ShareId.TryParse(id, out var shareId);

        _engine.Move(EngineActivity.None with
        {
            Shares = new Dictionary<ShareId, ItemActivity> { [shareId] = new(0, 100, 1, 0, 2_250, Receivers: 1, ReceiversPercent: 45) },
        });
        var sending = _events.Last(e => e.GetProperty("event").GetString() == Events.SharesChanged).GetProperty("payload")[0];
        Assert.Equal("sending", sending.GetProperty("activity").GetString());
        Assert.Equal(1, sending.GetProperty("receiverCount").GetInt32());
        Assert.Equal(45, sending.GetProperty("percent").GetInt32());

        _engine.Move(EngineActivity.None with
        {
            Shares = new Dictionary<ShareId, ItemActivity> { [shareId] = new(0, 0, 0, 0, 5_000, Finished: 1) },
        });
        var sent = (await InvokeAsync(Commands.SharesList)).GetProperty("value")[0];
        Assert.Equal("sent", sent.GetProperty("activity").GetString());
        Assert.Equal(1, sent.GetProperty("finishedCount").GetInt32());
        Assert.Equal(0, sent.GetProperty("receiverCount").GetInt32());
    }

    [Fact]
    public void A_clicked_notification_asks_the_screen_to_open_the_items_details()
    {
        _notifications.Open(NotifiedItem.Transfer, "t1", "film.mp4");

        var open = _events.Last(e => e.GetProperty("event").GetString() == Events.DetailsChanged).GetProperty("payload");
        Assert.Equal("transfer", open.GetProperty("kind").GetString());
        Assert.Equal("t1", open.GetProperty("id").GetString());
        Assert.Equal("film.mp4", open.GetProperty("name").GetString());
    }

    [Fact]
    public async Task The_speed_bar_reads_totals_and_stops_sending_and_receiving()
    {
        _engine.Move(EngineActivity.None with { ReceivingBytesPerSecond = 2_000, SendingBytesPerSecond = 300 });
        var activity = (await InvokeAsync(Commands.ActivityGet)).GetProperty("value");
        Assert.Equal(2_000, activity.GetProperty("receivingBytesPerSecond").GetInt64());
        Assert.Equal(300, activity.GetProperty("sendingBytesPerSecond").GetInt64());
        Assert.False(activity.GetProperty("sendingStopped").GetBoolean());

        Assert.True((await InvokeAsync(Commands.ActivitySet, new { sendingStopped = true, receivingStopped = true })).GetProperty("ok").GetBoolean());

        Assert.Equal((true, true), _engine.StoppedFlags);
        var changed = _events.Last(e => e.GetProperty("event").GetString() == Events.ActivityChanged).GetProperty("payload");
        Assert.True(changed.GetProperty("sendingStopped").GetBoolean());
        Assert.True(changed.GetProperty("receivingStopped").GetBoolean());
    }

    [Fact]
    public async Task A_shared_file_has_details_with_its_folder_and_what_it_sends()
    {
        var id = (await InvokeAsync(Commands.SharesAdd)).GetProperty("value").GetProperty("id").GetString()!;
        _engine.Activity = EngineActivity.None with
        {
            SendingBytesPerSecond = 900,
            Shares = new Dictionary<ShareId, ItemActivity> { [ShareId.TryParse(id, out var shareId) ? shareId : default] = new(0, 900, 2, 0, 4_000) },
        };

        var details = (await InvokeAsync(Commands.SharesDetails, new { id })).GetProperty("value");

        Assert.Equal("birthday.mp4", details.GetProperty("name").GetString());
        Assert.Equal(Path.GetDirectoryName(Video), details.GetProperty("folder").GetString());
        Assert.Equal("ready", details.GetProperty("status").GetString());
        Assert.Equal(900, details.GetProperty("sendingBytesPerSecond").GetInt64());
        Assert.Equal(2, details.GetProperty("connections").GetInt32());
        Assert.Equal(4_000, details.GetProperty("bytesSent").GetInt64());
        Assert.True(DateTimeOffset.TryParse(details.GetProperty("addedAt").GetString(), out _));
    }

    [Fact]
    public async Task A_received_file_has_details_with_where_it_came_from()
    {
        var link = InviteCodec.Write(FakeEngine.InvitePrefix + "abc", InviteHints.None);
        var id = (await InvokeAsync(Commands.TransfersReceive, new { invite = link })).GetProperty("value").GetProperty("id").GetString()!;

        var details = (await InvokeAsync(Commands.TransfersDetails, new { id })).GetProperty("value");

        Assert.Equal("invite", details.GetProperty("source").GetString());
        Assert.Equal("connecting", details.GetProperty("state").GetString());
        Assert.Equal(0, details.GetProperty("connections").GetInt32());

        var download = (await InvokeAsync(Commands.TransfersReceive, new { invite = FakeEngine.InvitePrefix + "public" })).GetProperty("value").GetProperty("id").GetString()!;
        Assert.Equal("downloadLink", (await InvokeAsync(Commands.TransfersDetails, new { id = download })).GetProperty("value").GetProperty("source").GetString());
    }

    [Fact]
    public async Task Details_of_something_that_is_not_there_say_so()
    {
        Assert.Equal(ErrorCodes.ShareNotFound, ErrorCode(await InvokeAsync(Commands.SharesDetails, new { id = Guid.NewGuid().ToString() })));
        Assert.Equal(ErrorCodes.TransferNotFound, ErrorCode(await InvokeAsync(Commands.TransfersDetails, new { id = "nope" })));
    }
}
