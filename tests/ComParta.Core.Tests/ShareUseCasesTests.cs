using ComParta.Core.Ports;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

public sealed class ShareUseCasesTests
{
    // A rooted path on whichever system runs the tests, so the full-path check leaves it alone.
    private static readonly string Video = Path.Combine(Path.GetTempPath(), "Videos", "birthday.mp4");

    private readonly FakeShareRepository _repository = new();
    private readonly FakeEngine _engine = new();
    private readonly FakeFilePicker _picker = new();
    private readonly FakeFileSystem _files = new();

    private ShareUseCases Create() => new(_repository, _engine, _picker, _files);

    private async Task<ShareView> AddVideoAsync(ShareUseCases shares)
    {
        _files.Files[Video] = 5_000;
        _picker.NextPick = Video;
        return (await shares.AddAsync()).Value!.Share!;
    }

    [Fact]
    public async Task Add_then_list_returns_the_share_with_name_and_size()
    {
        var shares = Create();

        var added = await AddVideoAsync(shares);

        var listed = Assert.Single(await shares.ListAsync());
        Assert.Equal(added.Share, listed.Share);
        Assert.Equal("birthday.mp4", listed.Share.Name);
        Assert.Equal(5_000, listed.Share.SizeBytes);
        Assert.Equal(ShareStatus.Ready, listed.Status);
        Assert.Equal(Video, _engine.Offered[added.Share.Id]);
        Assert.Single(_repository.Items);
    }

    [Fact]
    public async Task Cancelling_the_file_dialog_adds_nothing()
    {
        var shares = Create();
        _picker.NextPick = null;

        var result = await shares.AddAsync();

        Assert.True(result.Ok);
        Assert.Equal(new AddedShare(null), result.Value);
        Assert.Empty(await shares.ListAsync());
    }

    [Fact]
    public async Task The_same_file_twice_is_refused()
    {
        var shares = Create();
        await AddVideoAsync(shares);
        _picker.NextPick = Video.ToUpperInvariant();

        var result = await shares.AddAsync();

        Assert.Equal(ErrorKind.ShareDuplicate, result.Error);
        Assert.Single(await shares.ListAsync());
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_is_refused()
    {
        var shares = Create();
        _files.Files[Video] = 5_000;
        _files.Locked.Add(Video);
        _picker.NextPick = Video;

        var result = await shares.AddAsync();

        Assert.Equal(ErrorKind.FileUnreadable, result.Error);
        Assert.Empty(_repository.Items);
    }

    [Fact]
    public async Task Remove_deletes_the_record_and_stops_offering()
    {
        var shares = Create();
        var added = await AddVideoAsync(shares);

        var result = await shares.RemoveAsync(added.Share.Id);

        Assert.True(result.Ok);
        Assert.Empty(await shares.ListAsync());
        Assert.Empty(_repository.Items);
        Assert.Empty(_engine.Offered);
        // The file system port has no write or delete operation, so the original is untouched.
        Assert.Equal(5_000, _files.Files[Video]);
    }

    [Fact]
    public async Task Removing_an_unknown_share_reports_not_found()
    {
        var result = await Create().RemoveAsync(ShareId.New());

        Assert.Equal(ErrorKind.ShareNotFound, result.Error);
    }

    [Fact]
    public async Task Saved_shares_are_offered_again_after_a_restart()
    {
        var added = await AddVideoAsync(Create());
        var engineAfterRestart = new FakeEngine();
        var restarted = new ShareUseCases(_repository, engineAfterRestart, _picker, _files);

        await restarted.StartAsync();

        var listed = Assert.Single(await restarted.ListAsync());
        Assert.Equal(added.Share, listed.Share);
        Assert.Equal(ShareStatus.Ready, listed.Status);
        Assert.True(engineAfterRestart.Offered.ContainsKey(added.Share.Id));
    }

    [Fact]
    public async Task A_missing_file_is_unavailable_still_removable_and_offered_again_when_back()
    {
        var shares = Create();
        var added = await AddVideoAsync(shares);

        _files.Files.Remove(Video);
        Assert.Equal(ShareStatus.Unavailable, Assert.Single(await shares.ListAsync()).Status);
        Assert.Empty(_engine.Offered);

        _files.Files[Video] = 5_000;
        Assert.Equal(ShareStatus.Ready, Assert.Single(await shares.ListAsync()).Status);
        Assert.True(_engine.Offered.ContainsKey(added.Share.Id));

        _files.Files.Remove(Video);
        await shares.ListAsync();
        Assert.True((await shares.RemoveAsync(added.Share.Id)).Ok);
        Assert.Empty(_repository.Items);
    }

    [Fact]
    public async Task A_share_missing_at_start_up_is_unavailable_and_not_offered()
    {
        await AddVideoAsync(Create());
        _files.Files.Remove(Video);
        var engineAfterRestart = new FakeEngine();
        var restarted = new ShareUseCases(_repository, engineAfterRestart, _picker, _files);

        await restarted.StartAsync();

        Assert.Equal(ShareStatus.Unavailable, Assert.Single(await restarted.ListAsync()).Status);
        Assert.Empty(engineAfterRestart.Offered);
    }

    [Fact]
    public async Task A_download_file_is_saved_only_for_a_share_that_is_ready_and_never_over_another_file()
    {
        var paths = new FakePaths(Path.Combine(Path.GetTempPath(), "home"));
        var shares = new ShareUseCases(_repository, _engine, _picker, _files, paths: paths);
        var ready = await AddVideoAsync(shares);
        var taken = Path.Combine(paths.SavedRoot, Path.GetFileName(ready.Share.FilePath) + ".download");
        _files.Files[taken] = 1;

        var saved = await shares.SaveDownloadFileAsync(ready.Share.Id);

        Assert.Equal(Path.Combine(paths.SavedRoot, Path.GetFileNameWithoutExtension(taken) + " (2).download"), saved.Value);
        Assert.Equal(1, _files.Files[taken]);

        _engine.OfferOutcome = OfferState.Preparing;
        var big = Path.Combine(Path.GetTempPath(), "big.iso");
        _files.Files[big] = 9;
        _picker.NextPick = big;
        var preparing = (await shares.AddAsync()).Value!.Share!;
        Assert.Equal(ErrorKind.ShareNotReady, (await shares.SaveDownloadFileAsync(preparing.Share.Id)).Error);
        Assert.Equal(ErrorKind.ShareNotFound, (await shares.SaveDownloadFileAsync(ShareId.New())).Error);
    }

    [Fact]
    public async Task An_invite_is_given_only_for_a_share_that_is_ready()
    {
        var shares = Create();
        var ready = await AddVideoAsync(shares);
        // The engine's own link, as it is, so that other apps can open it too (AD-16, amended).
        Assert.Equal(FakeEngine.InvitePrefix + ready.Share.Id, (await shares.GetInviteAsync(ready.Share.Id)).Value);

        _engine.OfferOutcome = OfferState.Preparing;
        var big = Path.Combine(Path.GetTempPath(), "big.iso");
        _files.Files[big] = 9;
        _picker.NextPick = big;
        var preparing = (await shares.AddAsync()).Value!.Share!;

        Assert.Equal(ErrorKind.ShareNotReady, (await shares.GetInviteAsync(preparing.Share.Id)).Error);
        Assert.Equal(ErrorKind.ShareNotFound, (await shares.GetInviteAsync(ShareId.New())).Error);
    }

    [Fact]
    public async Task Changes_are_announced()
    {
        var shares = Create();
        var announced = 0;
        shares.Changed += () => announced++;

        var added = await AddVideoAsync(shares);
        await shares.RemoveAsync(added.Share.Id);

        Assert.True(announced >= 2);
    }

    [Fact]
    public void The_default_policy_keeps_everything_but_invite_lookup_closed()
    {
        var policy = SharingPolicy.Default;

        Assert.True(policy.AllowLocalDiscovery);
        Assert.True(policy.AllowPublicLookup);
        Assert.True(policy.AllowAddressInInvite);
        Assert.False(policy.AllowPeerExchange);
        Assert.False(policy.AllowAnnounceServers);
        Assert.True(policy.RequireEncryption);
    }

    [Fact]
    public async Task Pausing_a_share_is_stored_shown_and_applied_and_continue_undoes_it()
    {
        var shares = Create();
        var added = await AddVideoAsync(shares);
        var changes = 0;
        shares.Changed += () => changes++;

        Assert.True((await shares.PauseAsync(added.Share.Id)).Ok);

        Assert.True(Assert.Single(shares.Current()).Share.Paused);
        Assert.True(Assert.Single(_repository.Items).Paused);
        Assert.Contains(added.Share.Id, _engine.PausedOffers);
        Assert.Equal(1, changes);

        Assert.True((await shares.ContinueAsync(added.Share.Id)).Ok);

        Assert.False(Assert.Single(shares.Current()).Share.Paused);
        Assert.False(Assert.Single(_repository.Items).Paused);
        Assert.Empty(_engine.PausedOffers);
    }

    [Fact]
    public async Task A_share_paused_before_a_restart_is_offered_paused()
    {
        _files.Files[Video] = 5_000;
        var share = new Share(ShareId.New(), Video, "birthday.mp4", 5_000, DateTimeOffset.UnixEpoch, Paused: true);
        _repository.Items.Add(share);

        var shares = Create();
        await shares.StartAsync();

        Assert.Equal(Video, _engine.Offered[share.Id]);
        Assert.Contains(share.Id, _engine.PausedOffers);
        Assert.True(Assert.Single(shares.Current()).Share.Paused);
    }

    [Fact]
    public async Task Pausing_an_unknown_share_reports_not_found()
    {
        Assert.Equal(ErrorKind.ShareNotFound, (await Create().PauseAsync(ShareId.New())).Error);
    }

    [Fact]
    public async Task Stop_sharing_leaves_the_list_before_the_engine_has_let_go()
    {
        var shares = Create();
        var added = await AddVideoAsync(shares);
        var shownWhileStopping = -1;
        _engine.OnStopOffering = () => shownWhileStopping = shares.Current().Count;
        var changes = 0;
        shares.Changed += () => changes++;

        Assert.True((await shares.RemoveAsync(added.Share.Id)).Ok);

        Assert.Equal(0, shownWhileStopping);
        Assert.Equal(1, changes);
    }

    private static readonly string Trip = Path.Combine(Path.GetTempPath(), "Photos", "Trip");
    private static readonly string[] TripFiles = ["beach.jpg", Path.Combine("day 2", "hill.jpg"), Path.Combine("day 2", "notes.txt")];

    private async Task<ShareView> AddTripAsync(ShareUseCases shares)
    {
        foreach (var (file, i) in TripFiles.Select((file, i) => (file, i)))
            _files.Files[Path.Combine(Trip, file)] = 100 * (i + 1);
        return (await shares.AddItemsAsync(Trip, "Trip", ShareKind.Folder, TripFiles)).Value!;
    }

    [Fact]
    public async Task A_folder_with_three_files_is_one_share_with_three_items()
    {
        var shares = Create();

        var added = await AddTripAsync(shares);

        var listed = Assert.Single(await shares.ListAsync());
        Assert.Equal(("Trip", ShareKind.Folder, 600L, ShareStatus.Ready), (listed.Share.Name, listed.Share.Kind, listed.Share.SizeBytes, listed.Status));
        Assert.Equal(["beach.jpg", "day 2/hill.jpg", "day 2/notes.txt"], listed.Share.Items.Select(i => i.RelativePath));
        Assert.Equal(Path.Combine(Trip, "day 2", "notes.txt"), listed.Share.Items[2].ContentRef);
        Assert.Equal(added.Share, Assert.Single(_repository.Items));
    }

    [Fact]
    public async Task One_missing_file_makes_a_folder_partly_unavailable_and_all_missing_unavailable()
    {
        var shares = Create();
        var added = await AddTripAsync(shares);

        _files.Files.Remove(Path.Combine(Trip, "beach.jpg"));
        Assert.Equal(ShareStatus.PartlyUnavailable, Assert.Single(await shares.ListAsync()).Status);
        Assert.False(_engine.Offered.ContainsKey(added.Share.Id));

        foreach (var file in TripFiles)
            _files.Files.Remove(Path.Combine(Trip, file));
        Assert.Equal(ShareStatus.Unavailable, Assert.Single(await shares.ListAsync()).Status);

        foreach (var file in TripFiles)
            _files.Files[Path.Combine(Trip, file)] = 100;
        Assert.Equal(ShareStatus.Ready, Assert.Single(await shares.ListAsync()).Status);
        Assert.Equal("Trip", _engine.Offered[added.Share.Id]);
        Assert.Equal(added.Share.Items, _engine.OfferedItems[added.Share.Id]);
    }

    [Fact]
    public async Task A_folder_partly_gone_since_last_time_is_not_offered_after_a_restart()
    {
        var added = await AddTripAsync(Create());
        _files.Files.Remove(Path.Combine(Trip, "day 2", "hill.jpg"));
        _engine.Offered.Clear();

        var restarted = Create();
        await restarted.StartAsync();

        Assert.Equal(ShareStatus.PartlyUnavailable, Assert.Single(restarted.Current()).Status);
        Assert.False(_engine.Offered.ContainsKey(added.Share.Id));
    }

    [Fact]
    public async Task A_file_shared_on_its_own_is_one_item()
    {
        var added = await AddVideoAsync(Create());

        Assert.Equal(ShareKind.File, added.Share.Kind);
        Assert.Equal(new ShareItem("birthday.mp4", 5_000, Video), Assert.Single(added.Share.Items));
    }

    [Fact]
    public async Task Items_that_leave_the_folder_are_empty_or_cannot_be_read_are_refused()
    {
        var shares = Create();
        var outside = Path.Combine(Path.GetTempPath(), "Photos", "secret.txt");
        _files.Files[outside] = 10;
        _files.Files[Path.Combine(Trip, "empty.txt")] = 0;
        _files.Files[Path.Combine(Trip, "ok.jpg")] = 10;

        Assert.Equal(ErrorKind.FileUnreadable, (await shares.AddItemsAsync(Trip, "Trip", ShareKind.Folder, [Path.Combine("..", "secret.txt")])).Error);
        Assert.Equal(ErrorKind.FileUnreadable, (await shares.AddItemsAsync(Trip, "Trip", ShareKind.Folder, ["ok.jpg", "empty.txt"])).Error);
        Assert.Equal(ErrorKind.FileUnreadable, (await shares.AddItemsAsync(Trip, "Trip", ShareKind.Folder, ["ok.jpg", "gone.jpg"])).Error);
        Assert.Equal(ErrorKind.FileUnreadable, (await shares.AddItemsAsync(Trip, "Trip", ShareKind.Folder, [])).Error);
        Assert.Empty(await shares.ListAsync());
        Assert.Empty(_repository.Items);
    }

    [Fact]
    public async Task Several_files_chosen_together_wait_for_a_name_then_become_one_share()
    {
        var shares = Create();
        var folder = Path.Combine(Path.GetTempPath(), "Party");
        string[] files = [Path.Combine(folder, "a.jpg"), Path.Combine(folder, "b.jpg"), Path.Combine(folder, "c.mp4")];
        foreach (var file in files)
            _files.Files[file] = 10;
        _picker.NextPicks = files;

        var picked = await shares.AddAsync();

        Assert.Equal(new AddedShare(null, 3), picked.Value);
        Assert.Empty(await shares.ListAsync());
        Assert.Equal(ErrorKind.ShareNameInvalid, (await shares.AddPickedAsync("  ")).Error);
        Assert.Equal(ErrorKind.ShareNameInvalid, (await shares.AddPickedAsync("a/b")).Error);
        Assert.Equal(ErrorKind.ShareNameInvalid, (await shares.AddPickedAsync(new string('x', ShareUseCases.MaxNameLength + 1))).Error);

        var named = (await shares.AddPickedAsync(" Party photos ")).Value!;

        Assert.Equal(("Party photos", ShareKind.Collection, 30L), (named.Share.Name, named.Share.Kind, named.Share.SizeBytes));
        Assert.Equal(["a.jpg", "b.jpg", "c.mp4"], named.Share.Items.Select(i => i.RelativePath));
        Assert.Null((await shares.AddPickedAsync("Again")).Value);
        Assert.Single(await shares.ListAsync());
    }

    [Fact]
    public async Task Cancelling_the_name_forgets_the_files_chosen()
    {
        var shares = Create();
        var folder = Path.Combine(Path.GetTempPath(), "Party");
        _files.Files[Path.Combine(folder, "a.jpg")] = 10;
        _files.Files[Path.Combine(folder, "b.jpg")] = 10;
        _picker.NextPicks = [Path.Combine(folder, "a.jpg"), Path.Combine(folder, "b.jpg")];
        await shares.AddAsync();

        Assert.Null((await shares.AddPickedAsync(null)).Value);
        Assert.Null((await shares.AddPickedAsync("Party")).Value);
        Assert.Empty(await shares.ListAsync());
    }

    [Fact]
    public async Task A_folder_chosen_in_the_folder_dialog_is_shared_with_every_file_in_it()
    {
        var shares = Create();
        foreach (var file in TripFiles)
            _files.Files[Path.Combine(Trip, file)] = 100;
        _picker.NextFolder = Trip + Path.DirectorySeparatorChar;

        var added = (await shares.AddFolderAsync()).Value!;

        Assert.Equal(("Trip", ShareKind.Folder, 3), (added.Share.Name, added.Share.Kind, added.Share.Items.Count));
        Assert.Equal(Trip, added.Share.FilePath);
        Assert.Equal(ErrorKind.ShareDuplicate, (await shares.AddFolderAsync()).Error);
        _picker.NextFolder = null;
        Assert.True((await shares.AddFolderAsync()).Ok);
        Assert.Single(await shares.ListAsync());
    }

    [Fact]
    public async Task An_empty_folder_or_one_that_cannot_be_read_is_refused()
    {
        var shares = Create();
        var empty = Path.Combine(Path.GetTempPath(), "Empty");
        _files.EmptyFolders.Add(empty);

        _picker.NextFolder = empty;
        Assert.Equal(ErrorKind.FolderEmpty, (await shares.AddFolderAsync()).Error);
        _picker.NextFolder = Path.Combine(Path.GetTempPath(), "Gone");
        Assert.Equal(ErrorKind.FileUnreadable, (await shares.AddFolderAsync()).Error);
        Assert.Empty(await shares.ListAsync());
    }

    [Fact]
    public async Task A_dropped_folder_is_shared_whole_beside_dropped_files()
    {
        var shares = Create();
        foreach (var file in TripFiles)
            _files.Files[Path.Combine(Trip, file)] = 100;
        _files.Files[Video] = 5_000;

        var (added, problem) = await shares.AddDroppedAsync([Trip, Video]);

        Assert.Equal((2, (ErrorKind?)null), (added, problem));
        var listed = await shares.ListAsync();
        Assert.Equal([ShareKind.Folder, ShareKind.File], listed.Select(v => v.Share.Kind));
    }
}
