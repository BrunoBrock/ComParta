using ComParta.Core.Ports;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

/// <summary>More options on a download (epic 14 story 4) and download files opened in Windows (story 5).</summary>
public sealed class TransferOptionsTests
{
    private readonly FakeTransferRepository _repository = new();
    private readonly FakeEngine _engine = new();
    private readonly FakeFileSystem _files = new();
    private readonly FakeStorage _storage = new();

    private TransferUseCases Create() => new(_repository, _engine, files: _files, storage: _storage);

    private static readonly string OpenedFile = Path.Combine(Path.GetTempPath(), "Downloads", "films.download");

    private async Task<(TransferUseCases Transfers, TransferId Id)> DownloadingThreeFilesAsync()
    {
        var transfers = Create();
        var id = (await transfers.ReceiveAsync("invite:films")).Value!.Transfer.Id;
        _engine.Files[id] = [("Forest.mkv", 1000), ("Extras/Making of.mkv", 500), ("Subtitles.srt", 10)];
        return (transfers, id);
    }

    [Fact]
    public async Task A_public_download_is_shared_once_it_arrives_unless_the_person_says_not_to()
    {
        var (transfers, id) = await DownloadingThreeFilesAsync();

        Assert.True(_engine.SharingWhenDone[id]);
        var options = transfers.GetOptions(id).Value!;
        Assert.True(options.KeepSharing);
        Assert.False(options.Sharing);
        Assert.Equal(["Forest.mkv", "Extras/Making of.mkv", "Subtitles.srt"], options.Files!.Select(f => f.Path));
        Assert.All(options.Files!, f => Assert.True(f.Wanted));
    }

    [Fact]
    public async Task Files_left_out_go_to_the_engine_and_are_kept()
    {
        var (transfers, id) = await DownloadingThreeFilesAsync();
        var changed = 0;
        transfers.Changed += () => changed++;

        Assert.True((await transfers.SetOptionsAsync(id, [1, 1, 7], null)).Ok);

        Assert.Equal([1], _engine.Skipped[id]);
        Assert.Equal([1], Assert.Single(_repository.Items).SkippedFiles!);
        Assert.Equal([true, false, true], transfers.GetOptions(id).Value!.Files!.Select(f => f.Wanted));
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task At_least_one_file_stays()
    {
        var (transfers, id) = await DownloadingThreeFilesAsync();

        Assert.Equal(ErrorKind.NoFilesChosen, (await transfers.SetOptionsAsync(id, [0, 1, 2], null)).Error);

        Assert.False(_engine.Skipped.ContainsKey(id));
        Assert.Null(Assert.Single(_repository.Items).SkippedFiles);
    }

    [Fact]
    public async Task Ticking_every_file_again_leaves_nothing_out()
    {
        var (transfers, id) = await DownloadingThreeFilesAsync();
        await transfers.SetOptionsAsync(id, [2], null);

        await transfers.SetOptionsAsync(id, [], null);

        Assert.Empty(_engine.Skipped[id]);
        Assert.Null(Assert.Single(_repository.Items).SkippedFiles);
    }

    [Fact]
    public async Task Files_cannot_change_once_it_arrived_but_sharing_can()
    {
        var (transfers, id) = await DownloadingThreeFilesAsync();
        _engine.Report(new ReceiveProgress(id, "films", 1510, 1510, true));
        _engine.SharingReceived.Add(id);

        Assert.Equal(ErrorKind.TransferFinished, (await transfers.SetOptionsAsync(id, [1], null)).Error);
        var options = transfers.GetOptions(id).Value!;
        Assert.Null(options.Files);
        Assert.True(options.Sharing);

        Assert.True((await transfers.SetOptionsAsync(id, null, false)).Ok);

        Assert.False(_engine.SharingWhenDone[id]);
        Assert.False(transfers.GetOptions(id).Value!.Sharing);
        Assert.False(Assert.Single(_repository.Items).KeepSharing);
    }

    [Fact]
    public async Task The_choices_are_given_to_the_engine_again_after_a_restart()
    {
        var (transfers, id) = await DownloadingThreeFilesAsync();
        await transfers.SetOptionsAsync(id, [0], false);
        _engine.Skipped.Clear();
        _engine.SharingWhenDone.Clear();

        var restarted = Create();
        await restarted.StartAsync();

        Assert.Equal([0], _engine.Skipped[id]);
        Assert.False(_engine.SharingWhenDone[id]);
        Assert.False(restarted.GetOptions(id).Value!.KeepSharing);
    }

    [Fact]
    public async Task A_file_from_a_ComParta_invite_of_an_earlier_version_is_never_shared_back()
    {
        var transfers = Create();
        var link = InviteCodec.Write("invite:old", InviteHints.None);
        var id = (await transfers.ReceiveAsync(link)).Value!.Transfer.Id;

        Assert.False(_engine.SharingWhenDone[id]);
        Assert.True((await transfers.SetOptionsAsync(id, null, true)).Ok);
        Assert.False(_engine.SharingWhenDone[id]);
        Assert.False(transfers.GetOptions(id).Value!.KeepSharing);
    }

    [Fact]
    public async Task Clearing_or_deleting_a_file_that_arrived_stops_sharing_it()
    {
        var (transfers, id) = await DownloadingThreeFilesAsync();
        _engine.Report(new ReceiveProgress(id, "films", 1510, 1510, true));
        _engine.SharingReceived.Add(id);

        await transfers.RemoveAsync(id);

        Assert.False(_engine.SharingWhenDone[id]);
        Assert.Empty(_engine.SharingReceived);
    }

    [Fact]
    public async Task Options_of_a_transfer_that_is_gone_say_so()
    {
        var transfers = Create();

        Assert.Equal(ErrorKind.TransferNotFound, transfers.GetOptions(TransferId.New()).Error);
        Assert.Equal(ErrorKind.TransferNotFound, (await transfers.SetOptionsAsync(TransferId.New(), [0], null)).Error);
    }

    [Fact]
    public async Task A_download_file_opened_in_Windows_is_shown_first_and_received_once()
    {
        var transfers = Create();
        _files.Contents[OpenedFile] = "download-file:films"u8.ToArray();
        _engine.Announced["films"] = new InviteSummary("Nature films", 1510);

        var preview = transfers.PreviewOpenedFile(OpenedFile);

        Assert.Equal("Nature films", preview.Value!.Hints.Name);
        Assert.Equal(1510, preview.Value.Hints.SizeBytes);
        Assert.False(preview.Value.IsInvite);
        Assert.Empty(transfers.List());

        var started = await transfers.ReceiveOpenedFileAsync();

        Assert.Equal(TransferSource.DownloadFile, started.Value!.Transfer.Source);
        Assert.Equal("films", _engine.Receiving[started.Value.Transfer.Id]);
        Assert.Equal(ErrorKind.DownloadFileInvalid, (await transfers.ReceiveOpenedFileAsync()).Error);
    }

    [Fact]
    public void An_opened_file_that_cannot_be_read_or_is_not_a_download_file_says_so()
    {
        var transfers = Create();
        _files.Contents[OpenedFile] = "just some text"u8.ToArray();

        Assert.Equal(ErrorKind.DownloadFileInvalid, transfers.PreviewOpenedFile(OpenedFile).Error);
        Assert.Equal(ErrorKind.FileUnreadable, transfers.PreviewOpenedFile(OpenedFile + ".gone").Error);
    }

    [Fact]
    public async Task An_opened_file_larger_than_the_room_left_is_refused()
    {
        var transfers = Create();
        _files.Contents[OpenedFile] = "download-file:films"u8.ToArray();
        _engine.Announced["films"] = new InviteSummary("Nature films", 5000);
        _storage.FreeBytes = 100;

        Assert.True(transfers.PreviewOpenedFile(OpenedFile).Value!.NotEnoughSpace);
        Assert.Equal(ErrorKind.DiskFull, (await transfers.ReceiveOpenedFileAsync()).Error);
        Assert.Empty(transfers.List());
    }
}
