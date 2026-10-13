using ComParta.Core.Ports;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

public sealed class TransferUseCasesTests
{
    private readonly FakeTransferRepository _repository = new();
    private readonly FakeEngine _engine = new();
    private readonly FakeFilePicker _picker = new();
    private readonly FakeFileSystem _files = new();
    private readonly FakeStorage _storage = new();

    private TransferUseCases Create() => new(_repository, _engine, picker: _picker, files: _files, storage: _storage);

    private const string DownloadFile = @"C:\Users\Ana\Downloads\film.download";

    [Fact]
    public async Task Receiving_from_an_invite_starts_a_transfer_that_is_looking_for_the_other_device()
    {
        var transfers = Create();

        var result = await transfers.ReceiveAsync("  invite:abc \n");

        Assert.True(result.Ok);
        var listed = Assert.Single(transfers.List());
        Assert.Equal(TransferState.Connecting, listed.State);
        Assert.Null(listed.Transfer.Name);
        Assert.Equal("abc", _engine.Receiving[listed.Transfer.Id]);
        Assert.Single(_repository.Items);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello grandma")]
    public async Task Text_that_is_not_an_invite_is_refused(string? text)
    {
        var transfers = Create();

        var result = await transfers.ReceiveAsync(text);

        Assert.Equal(ErrorKind.InviteInvalid, result.Error);
        Assert.Empty(transfers.List());
        Assert.Empty(_repository.Items);
    }

    [Fact]
    public async Task The_same_invite_twice_while_still_receiving_is_refused()
    {
        var transfers = Create();
        await transfers.ReceiveAsync("invite:abc");

        var result = await transfers.ReceiveAsync("invite:abc");

        Assert.Equal(ErrorKind.TransferDuplicate, result.Error);
        Assert.Single(transfers.List());
    }

    [Fact]
    public async Task Progress_moves_a_transfer_to_receiving_and_then_done()
    {
        var transfers = Create();
        var id = (await transfers.ReceiveAsync("invite:abc")).Value!.Transfer.Id;

        _engine.Report(new ReceiveProgress(id, "birthday.mp4", 1_000, 0, false));
        Assert.Equal(TransferState.Connecting, transfers.List()[0].State);

        _engine.Report(new ReceiveProgress(id, "birthday.mp4", 1_000, 400, false));
        var receiving = transfers.List()[0];
        Assert.Equal(TransferState.Receiving, receiving.State);
        Assert.Equal(400, receiving.ReceivedBytes);
        Assert.Equal("birthday.mp4", receiving.Transfer.Name);

        _engine.Report(new ReceiveProgress(id, "birthday.mp4", 1_000, 1_000, true));
        var done = transfers.List()[0];
        Assert.Equal(TransferState.Done, done.State);
        Assert.Equal(1_000, done.ReceivedBytes);
        Assert.True(Assert.Single(_repository.Items).Done);
    }

    [Fact]
    public async Task Removing_an_unfinished_transfer_stops_it()
    {
        var transfers = Create();
        var id = (await transfers.ReceiveAsync("invite:abc")).Value!.Transfer.Id;

        var result = await transfers.RemoveAsync(id);

        Assert.True(result.Ok);
        Assert.Empty(transfers.List());
        Assert.Empty(_repository.Items);
        Assert.Equal([id], _engine.Stopped);
    }

    [Fact]
    public async Task Removing_a_finished_transfer_only_clears_it_from_the_list()
    {
        var transfers = Create();
        var id = (await transfers.ReceiveAsync("invite:abc")).Value!.Transfer.Id;
        _engine.Report(new ReceiveProgress(id, "a.txt", 10, 10, true));

        await transfers.RemoveAsync(id);

        Assert.Empty(transfers.List());
        Assert.Empty(_engine.Stopped);
    }

    [Fact]
    public async Task Unfinished_transfers_continue_after_a_restart_and_finished_ones_stay_listed()
    {
        var first = Create();
        var unfinished = (await first.ReceiveAsync("invite:abc")).Value!.Transfer.Id;
        var finished = (await first.ReceiveAsync("invite:def")).Value!.Transfer.Id;
        _engine.Report(new ReceiveProgress(finished, "a.txt", 10, 10, true));
        _engine.Receiving.Remove(finished);

        var restarted = Create();
        await restarted.StartAsync();

        var listed = restarted.List();
        Assert.Equal(2, listed.Count);
        Assert.Equal(TransferState.Connecting, listed.Single(v => v.Transfer.Id == unfinished).State);
        Assert.Equal(TransferState.Done, listed.Single(v => v.Transfer.Id == finished).State);
    }

    [Fact]
    public async Task After_a_restart_progress_is_what_the_engine_has_and_done_stays_done()
    {
        var first = Create();
        var unfinished = (await first.ReceiveAsync("invite:abc")).Value!.Transfer.Id;
        var finished = (await first.ReceiveAsync("invite:def")).Value!.Transfer.Id;
        _engine.Report(new ReceiveProgress(unfinished, "film.mp4", 1_000, 300, false));
        _engine.Report(new ReceiveProgress(finished, "a.txt", 10, 10, true));
        _engine.Receiving.Remove(finished);

        var restarted = Create();
        await restarted.StartAsync();
        // The engine's first report after the restart comes from what is already on disk.
        _engine.Report(new ReceiveProgress(unfinished, "film.mp4", 1_000, 600, false));
        _engine.Report(new ReceiveProgress(finished, "a.txt", 10, 0, false));

        var resumed = restarted.List().Single(v => v.Transfer.Id == unfinished);
        Assert.Equal(TransferState.Receiving, resumed.State);
        Assert.Equal(600, resumed.ReceivedBytes);
        var done = restarted.List().Single(v => v.Transfer.Id == finished);
        Assert.Equal(TransferState.Done, done.State);
        Assert.Equal(10, done.ReceivedBytes);
    }

    [Fact]
    public async Task A_transfer_the_engine_lost_is_dropped_at_start_up()
    {
        await Create().ReceiveAsync("invite:abc");
        _engine.Receiving.Clear();

        var restarted = Create();
        await restarted.StartAsync();

        Assert.Empty(restarted.List());
        Assert.Empty(_repository.Items);
    }

    [Fact]
    public async Task A_download_file_starts_a_transfer_like_an_invite()
    {
        var transfers = Create();
        _picker.NextPick = DownloadFile;
        _files.Contents[DownloadFile] = "download-file:film"u8.ToArray();

        var result = await transfers.OpenFileAsync();

        Assert.True(result.Ok);
        var listed = Assert.Single(transfers.List());
        Assert.Equal(TransferState.Connecting, listed.State);
        Assert.Equal("film", _engine.Receiving[listed.Transfer.Id]);
        Assert.Single(_repository.Items);
    }

    [Fact]
    public async Task Cancelling_the_download_file_dialog_changes_nothing()
    {
        var transfers = Create();
        _picker.NextPick = null;

        var result = await transfers.OpenFileAsync();

        Assert.True(result.Ok);
        Assert.Null(result.Value);
        Assert.Empty(transfers.List());
    }

    [Fact]
    public async Task A_file_that_is_not_a_download_file_or_cannot_be_read_is_refused()
    {
        var transfers = Create();
        _picker.NextPick = DownloadFile;
        _files.Contents[DownloadFile] = "a holiday letter"u8.ToArray();

        Assert.Equal(ErrorKind.DownloadFileInvalid, (await transfers.OpenFileAsync()).Error);

        _files.Locked.Add(DownloadFile);
        Assert.Equal(ErrorKind.FileUnreadable, (await transfers.OpenFileAsync()).Error);
        Assert.Empty(transfers.List());
    }

    [Fact]
    public async Task The_same_download_file_twice_while_still_receiving_is_refused()
    {
        var transfers = Create();
        _picker.NextPick = DownloadFile;
        _files.Contents[DownloadFile] = "download-file:film"u8.ToArray();
        await transfers.OpenFileAsync();

        Assert.Equal(ErrorKind.TransferDuplicate, (await transfers.OpenFileAsync()).Error);
    }

    [Fact]
    public async Task Pausing_stops_progress_until_the_transfer_continues()
    {
        var transfers = Create();
        var id = (await transfers.ReceiveAsync("invite:abc")).Value!.Transfer.Id;
        _engine.Report(new ReceiveProgress(id, "a.mp4", 100, 20, false, BytesPerSecond: 10));

        Assert.True((await transfers.PauseAsync(id)).Ok);
        _engine.Report(new ReceiveProgress(id, "a.mp4", 100, 30, false, BytesPerSecond: 10));

        var paused = Assert.Single(transfers.List());
        Assert.Equal(TransferState.Paused, paused.State);
        Assert.Equal(20, paused.ReceivedBytes);
        Assert.Null(paused.SecondsLeft);
        Assert.Contains(id, _engine.Paused);
        Assert.True(Assert.Single(_repository.Items).Paused);

        Assert.True((await transfers.ContinueAsync(id)).Ok);
        Assert.Equal(TransferState.Receiving, Assert.Single(transfers.List()).State);
        Assert.DoesNotContain(id, _engine.Paused);
        Assert.False(Assert.Single(_repository.Items).Paused);
    }

    [Fact]
    public async Task A_paused_transfer_stays_paused_after_a_restart()
    {
        var id = (await Create().ReceiveAsync("invite:abc")).Value!.Transfer.Id;
        var first = Create();
        await first.StartAsync();
        await first.PauseAsync(id);
        _engine.Paused.Clear();

        var restarted = Create();
        await restarted.StartAsync();

        Assert.Equal(TransferState.Paused, Assert.Single(restarted.List()).State);
        Assert.Contains(id, _engine.Paused);
    }

    [Fact]
    public async Task A_finished_transfer_cannot_be_paused()
    {
        var transfers = Create();
        var id = (await transfers.ReceiveAsync("invite:abc")).Value!.Transfer.Id;
        _engine.Report(new ReceiveProgress(id, "a.txt", 10, 10, true));

        Assert.Equal(ErrorKind.TransferFinished, (await transfers.PauseAsync(id)).Error);
        Assert.Equal(ErrorKind.TransferNotFound, (await transfers.PauseAsync(TransferId.New())).Error);
    }

    [Fact]
    public async Task Time_left_comes_from_a_smoothed_speed_and_is_unknown_after_a_stall()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var transfers = new TransferUseCases(_repository, _engine, clock, _picker, _files, _storage);
        var id = (await transfers.ReceiveAsync("invite:abc")).Value!.Transfer.Id;

        _engine.Report(new ReceiveProgress(id, "a.mp4", 1000, 300, false, BytesPerSecond: 100));
        Assert.Null(Assert.Single(transfers.List()).SecondsLeft);
        clock.Now += TimeSpan.FromSeconds(1);
        _engine.Report(new ReceiveProgress(id, "a.mp4", 1000, 400, false, BytesPerSecond: 100));

        var listed = Assert.Single(transfers.List());
        Assert.Equal(100, listed.BytesPerSecond);
        Assert.Equal(6, listed.SecondsLeft);

        clock.Now += TimeSpan.FromSeconds(TimeLeft.StallAfter.TotalSeconds);
        _engine.Report(new ReceiveProgress(id, "a.mp4", 1000, 400, false));
        Assert.Null(Assert.Single(transfers.List()).SecondsLeft);
    }

    [Fact]
    public async Task A_ComParta_link_in_a_message_is_received_with_the_engine_text_inside_it()
    {
        var transfers = Create();
        var link = InviteCodec.Write("invite:abc", new InviteHints("Paulo", "..\\..\\evil.exe", 10, 1));

        var result = await transfers.ReceiveAsync($"Oi! Abra isto: {link} 😀");

        Assert.True(result.Ok);
        Assert.Equal("abc", _engine.Receiving[result.Value!.Transfer.Id]);
        // A name from the invite is never used as the transfer's name; that comes from the received metadata.
        Assert.Null(result.Value.Transfer.Name);
    }

    [Fact]
    public async Task The_engines_own_link_in_a_message_is_received_and_previewed()
    {
        _engine.Announced["abc"] = new InviteSummary("Natal.mp4", 4_200);
        var transfers = Create();
        var message = $"Oi! Abra isto: {FakeEngine.InvitePrefix}abc\nBeijos";

        var preview = transfers.Preview(message);
        var result = await transfers.ReceiveAsync(message);

        Assert.Equal("Natal.mp4", preview.Value!.Hints.Name);
        Assert.Equal(4_200, preview.Value.Hints.SizeBytes);
        Assert.True(result.Ok);
        Assert.Equal("abc", _engine.Receiving[result.Value!.Transfer.Id]);
    }

    [Fact]
    public async Task A_link_with_a_broken_payload_is_refused()
    {
        var result = await Create().ReceiveAsync("comparta://receive/v1/broken!");

        Assert.Equal(ErrorKind.InviteInvalid, result.Error);
        Assert.Empty(_engine.Receiving);
    }

    [Fact]
    public void The_preview_shows_the_invites_hints_and_the_room_left_without_receiving()
    {
        _storage.FreeBytes = 120_000;
        var link = InviteCodec.Write("invite:abc", new InviteHints("Paulo", "Natal.mp4", 4_200, 1));

        var preview = Create().Preview(link);

        Assert.True(preview.Ok);
        Assert.Equal(new InviteHints("Paulo", "Natal.mp4", 4_200, 1), preview.Value!.Hints);
        Assert.True(preview.Value.IsInvite);
        Assert.Equal(120_000, preview.Value.FreeBytes);
        Assert.False(preview.Value.NotEnoughSpace);
        Assert.Empty(_engine.Receiving);
    }

    [Fact]
    public void The_preview_of_a_download_link_uses_what_the_engine_reads_from_it()
    {
        _engine.Announced["abc"] = new InviteSummary("Open film.mkv", 1_800);

        var preview = Create().Preview("invite:abc");

        Assert.Equal("Open film.mkv", preview.Value!.Hints.Name);
        Assert.Equal(1_800, preview.Value.Hints.SizeBytes);
        Assert.False(preview.Value.IsInvite);
        Assert.Equal(ErrorKind.InviteInvalid, Create().Preview("hello grandma").Error);
    }

    [Fact]
    public async Task A_file_larger_than_the_room_left_is_not_started()
    {
        _storage.FreeBytes = 1_000;
        var transfers = Create();
        var link = InviteCodec.Write("invite:abc", new InviteHints(null, "big.mp4", 5_000, 1));

        Assert.True(transfers.Preview(link).Value!.NotEnoughSpace);
        var result = await transfers.ReceiveAsync(link);

        Assert.Equal(ErrorKind.DiskFull, result.Error);
        Assert.Empty(_engine.Receiving);
        Assert.Empty(transfers.List());
    }
}
