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
    private readonly List<JsonElement> _events = [];
    private readonly BridgeRouter _router;

    public BridgeRouterTests()
    {
        var shares = new ShareUseCases(new FakeShareRepository(), _engine, _picker, _files);
        var transfers = new TransferUseCases(new FakeTransferRepository(), _engine, picker: _picker, files: _files);
        _router = new BridgeRouter(shares, transfers, _shell, _shell);
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

        var receiving = (await InvokeAsync(Commands.TransfersList)).GetProperty("value")[0];
        Assert.Equal(50, receiving.GetProperty("bytesPerSecond").GetInt64());
        Assert.Equal(15, receiving.GetProperty("secondsLeft").GetInt64());

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
}
