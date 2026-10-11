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
        return (await shares.AddAsync()).Value!;
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
        Assert.Null(result.Value);
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
    public async Task An_invite_is_given_only_for_a_share_that_is_ready()
    {
        var shares = Create();
        var ready = await AddVideoAsync(shares);
        Assert.True(InviteCodec.TryRead((await shares.GetInviteAsync(ready.Share.Id)).Value, out var invite));
        Assert.Equal(FakeEngine.InvitePrefix + ready.Share.Id, invite.EngineText);
        Assert.Equal(ready.Share.Name, invite.Hints.Name);
        Assert.Equal(ready.Share.SizeBytes, invite.Hints.SizeBytes);

        _engine.OfferOutcome = OfferState.Preparing;
        var big = Path.Combine(Path.GetTempPath(), "big.iso");
        _files.Files[big] = 9;
        _picker.NextPick = big;
        var preparing = (await shares.AddAsync()).Value!;

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
}
