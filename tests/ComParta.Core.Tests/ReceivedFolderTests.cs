using ComParta.Core.Ports;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

/// <summary>Choosing where received files go (epic 12 story 2).</summary>
public sealed class ReceivedFolderTests
{
    private readonly FakeTransferRepository _transferRepository = new();
    private readonly FakeSettingsRepository _settingsRepository = new();
    private readonly FakeEngine _engine = new();
    private readonly FakeFilePicker _picker = new();
    private readonly FakeFileSystem _files = new();
    private readonly FakeStorage _storage = new();
    private readonly FakeOpener _opener = new();
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "comparta-folder-tests");
    private readonly FakePaths _paths = new(Root);
    /// <summary>A folder on another drive, as the person would pick it.</summary>
    private static readonly string Family = Path.Combine(Root, "other drive", "Family");

    private readonly ReceivedFolder _folder;
    private readonly SettingsUseCases _settings;

    public ReceivedFolderTests()
    {
        _files.EmptyFolders.Add(Family);
        _folder = new ReceivedFolder(_settingsRepository, _paths, _picker, _files);
        _settings = new SettingsUseCases(_settingsRepository, new FakeSystem("en"), received: _folder);
    }

    private TransferUseCases Transfers() =>
        new(_transferRepository, _engine, picker: _picker, files: _files, storage: _storage, received: _folder);

    private async Task<Result<string?>> ChooseAsync(string? folder)
    {
        _picker.NextFolder = folder;
        return await _settings.ChooseReceivedFolderAsync();
    }

    [Fact]
    public async Task New_transfers_go_to_the_chosen_folder_and_earlier_ones_keep_theirs()
    {
        var transfers = Transfers();
        var running = (await transfers.ReceiveAsync("invite:running")).Value!.Transfer.Id;
        var finished = (await transfers.ReceiveAsync("invite:finished")).Value!.Transfer.Id;
        _engine.Report(new ReceiveProgress(finished, "photo.jpg", 10, 10, true));

        Assert.Equal("Family", (await ChooseAsync(Family)).Value);
        var later = (await transfers.ReceiveAsync("invite:later")).Value!.Transfer.Id;

        Assert.Equal([null, null, Family], _transferRepository.Items.Select(t => t.Folder));
        Assert.Null(_engine.Folders[running]);
        Assert.Equal(Family, _engine.Folders[later]);

        // After a restart each unfinished one continues in the folder it started in.
        _engine.Receiving.Remove(finished);
        await _settings.StartAsync();
        await Transfers().StartAsync();
        Assert.Null(_engine.Folders[running]);
        Assert.Equal(Family, _engine.Folders[later]);
    }

    [Fact]
    public async Task Settings_show_the_chosen_folders_name_and_the_choice_is_kept()
    {
        var changed = 0;
        _settings.Changed += () => changed++;
        var before = await _settings.GetAsync();
        Assert.Equal(("Received", false), (before.ReceivedFolderName, before.ReceivedFolderChosen));

        await ChooseAsync(Family);

        var after = await _settings.GetAsync();
        Assert.Equal(("Family", true), (after.ReceivedFolderName, after.ReceivedFolderChosen));
        Assert.Equal(1, changed);
        Assert.Equal(Family, _settings.ChosenReceivedFolder);
        // Nothing is left behind by the check that the folder can be written.
        Assert.Empty(_files.Files);

        var reopened = new ReceivedFolder(_settingsRepository, _paths);
        await reopened.LoadAsync();
        Assert.Equal(Family, reopened.Chosen);
    }

    [Fact]
    public async Task Closing_the_dialog_changes_nothing()
    {
        await ChooseAsync(Family);

        var closed = await ChooseAsync(null);

        Assert.True(closed.Ok);
        Assert.Null(closed.Value);
        Assert.Equal(Family, _folder.Chosen);
    }

    [Fact]
    public async Task The_usual_folder_comes_back_by_picking_it_or_by_asking_for_it()
    {
        await ChooseAsync(Family);
        Assert.Equal("Received", (await ChooseAsync(_paths.ReceivedRoot + Path.DirectorySeparatorChar)).Value);
        Assert.Null(_folder.Chosen);

        await ChooseAsync(Family);
        Assert.True((await _settings.UseUsualReceivedFolderAsync()).Ok);
        Assert.Null(_folder.Chosen);
        Assert.False((await _settings.GetAsync()).ReceivedFolderChosen);
        Assert.Empty(_settingsRepository.Values);
    }

    [Fact]
    public async Task A_folder_that_cannot_hold_received_files_is_refused_and_the_choice_stays()
    {
        var readOnly = Path.Combine(Root, "cd");
        _files.EmptyFolders.Add(readOnly);
        _files.ReadOnlyFolders.Add(readOnly);
        var engineData = Path.Combine(_paths.EngineRoot, "transfers");
        _files.EmptyFolders.Add(engineData);
        await ChooseAsync(Family);

        foreach (var picked in new[] { readOnly, Path.Combine(Root, "gone"), engineData, _paths.EngineRoot, "relative" })
            Assert.Equal(ErrorKind.ReceivedFolderUnusable, (await ChooseAsync(picked)).Error);

        Assert.Equal(Family, _folder.Chosen);
    }

    [Fact]
    public async Task A_chosen_folder_that_is_not_there_now_starts_nothing()
    {
        await ChooseAsync(Family);
        _files.EmptyFolders.Remove(Family);
        var transfers = Transfers();

        var result = await transfers.ReceiveAsync("invite:abc");

        Assert.Equal(ErrorKind.ReceivedFolderMissing, result.Error);
        Assert.Empty(_engine.Receiving);
        Assert.Empty(transfers.List());
    }

    [Fact]
    public async Task Room_is_measured_where_each_transfer_goes()
    {
        _storage.FreeBytes = 1_000_000;
        _storage.FreeIn[Family] = 1_000;
        var transfers = Transfers();
        var usual = (await transfers.ReceiveAsync("invite:usual")).Value!.Transfer.Id;
        await ChooseAsync(Family);
        var link = InviteCodec.Write("invite:big", new InviteHints(null, "big.mp4", 5_000, 1));

        Assert.Equal(1_000, transfers.Preview(link).Value!.FreeBytes);
        Assert.Equal(ErrorKind.DiskFull, (await transfers.ReceiveAsync(link)).Error);

        // The one already running still has the room of its own folder.
        _engine.Report(new ReceiveProgress(usual, "film.mp4", 5_000, 100, false));
        Assert.Equal(TransferState.Receiving, transfers.List().Single(v => v.Transfer.Id == usual).State);
    }

    [Fact]
    public async Task Received_files_are_opened_in_their_own_folder_and_never_outside_it()
    {
        var transfers = Transfers();
        var received = new ReceivedUseCases(transfers, _paths, _opener, _files);
        var before = (await transfers.ReceiveAsync("invite:before")).Value!.Transfer.Id;
        _engine.Report(new ReceiveProgress(before, "before.pdf", 10, 10, true));
        await ChooseAsync(Family);
        var after = (await transfers.ReceiveAsync("invite:after")).Value!.Transfer.Id;
        _engine.Report(new ReceiveProgress(after, "after.pdf", 10, 10, true));
        _files.Files[Path.Combine(_paths.ReceivedRoot, "before.pdf")] = 10;
        _files.Files[Path.Combine(Family, "after.pdf")] = 10;

        Assert.True((await received.OpenAsync(before)).Ok);
        Assert.True((await received.OpenAsync(after)).Ok);
        Assert.Equal([Path.Combine(_paths.ReceivedRoot, "before.pdf"), Path.Combine(Family, "after.pdf")], _opener.Opened);

        // A name that climbs out of the chosen folder is refused, as it is in the usual one.
        var escaping = (await transfers.ReceiveAsync("invite:escaping")).Value!.Transfer.Id;
        _engine.Report(new ReceiveProgress(escaping, Path.Combine("..", "secret.txt"), 10, 10, true));
        _files.Files[Path.Combine(Root, "other drive", "secret.txt")] = 10;
        Assert.Equal(ErrorKind.ReceivedNotFound, (await received.OpenAsync(escaping)).Error);
    }

    [Fact]
    public void A_drive_of_its_own_contains_what_is_on_it()
    {
        var drive = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.True(ReceivedNames.IsInside(drive, Path.Combine(drive, "film.mp4")));
        Assert.Equal(drive, ReceivedFolder.NameOf(drive));
        Assert.Equal("Family", ReceivedFolder.NameOf(Family + Path.DirectorySeparatorChar));
    }
}
