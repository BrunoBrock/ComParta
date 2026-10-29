using ComParta.Core.Ports;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

/// <summary>
/// Sharing what a phone's picker hands over (epic 7 story 2, AD-20): references with no path, kept as
/// they are and never taken apart as paths.
/// </summary>
public sealed class PickedOnAPhoneTests
{
    private readonly FakeShareRepository _repository = new();
    private readonly FakeEngine _engine = new();
    private readonly FakeFilePicker _picker = new();
    private readonly FakePhoneStorage _phone = new();

    private ShareUseCases Create() => new(_repository, _engine, _picker, _phone);

    [Fact]
    public async Task One_picked_video_is_shared_under_its_own_name_and_read_from_its_reference()
    {
        var video = _phone.AddItem("Aniversário da vovó.mp4", 4_000);
        _picker.NextPick = video;

        var added = (await Create().AddAsync()).Value!.Share!;

        Assert.Equal(("Aniversário da vovó.mp4", 4_000L, ShareKind.File), (added.Share.Name, added.Share.SizeBytes, added.Share.Kind));
        Assert.Equal(new ShareItem("Aniversário da vovó.mp4", 4_000, video), Assert.Single(added.Share.Items));
        Assert.Equal(video, added.Share.FilePath);
    }

    [Fact]
    public async Task Several_picked_photos_become_one_share_each_under_its_name_even_when_two_names_match()
    {
        _picker.NextPicks = [_phone.AddItem("IMG_1.jpg", 100), _phone.AddItem("IMG_2.jpg", 200), _phone.AddItem("IMG_1.jpg", 300)];
        var shares = Create();

        Assert.Equal(3, (await shares.AddAsync()).Value!.FilesToName);
        var named = (await shares.AddPickedAsync("Festa")).Value!;

        Assert.Equal(["IMG_1.jpg", "IMG_2.jpg", "IMG_1 (2).jpg"], named.Share.Items.Select(i => i.RelativePath));
        Assert.Equal(_picker.NextPicks, named.Share.Items.Select(i => i.ContentRef));
        Assert.Equal(600, named.Share.SizeBytes);
    }

    [Fact]
    public async Task A_picked_folder_is_shared_with_its_structure_each_file_read_from_its_own_reference()
    {
        var tree = _phone.AddTree("Viagem", ("praia.jpg", 100), ("dia 2/morro.jpg", 50));
        _picker.NextFolder = tree;

        var added = (await Create().AddFolderAsync()).Value!;

        Assert.Equal(("Viagem", ShareKind.Folder, tree), (added.Share.Name, added.Share.Kind, added.Share.FilePath));
        Assert.Equal(["dia 2/morro.jpg", "praia.jpg"], added.Share.Items.Select(i => i.RelativePath));
        Assert.Equal(_phone.Trees[tree].Files["dia 2/morro.jpg"], added.Share.Items[0].ContentRef);
        Assert.Equal(added.Share.Items, _engine.OfferedItems[added.Share.Id]);
    }

    [Fact]
    public async Task The_same_picked_folder_twice_is_refused()
    {
        var tree = _phone.AddTree("Viagem", ("praia.jpg", 100));
        _picker.NextFolder = tree;
        var shares = Create();
        await shares.AddFolderAsync();

        Assert.Equal(ErrorKind.ShareDuplicate, (await shares.AddFolderAsync()).Error);
    }

    [Fact]
    public async Task An_item_whose_permission_was_taken_back_is_refused_as_unreadable()
    {
        var video = _phone.AddItem("video.mp4", 4_000);
        _phone.Revoked.Add(video);
        _picker.NextPick = video;

        Assert.Equal(ErrorKind.FileUnreadable, (await Create().AddAsync()).Error);
    }

    [Fact]
    public async Task After_a_restart_a_picked_item_is_offered_again_from_its_reference()
    {
        var video = _phone.AddItem("video.mp4", 4_000);
        _picker.NextPick = video;
        var added = (await Create().AddAsync()).Value!.Share!;
        var restartedEngine = new FakeEngine();

        await new ShareUseCases(_repository, restartedEngine, _picker, _phone).StartAsync();

        Assert.Equal(video, restartedEngine.Offered[added.Share.Id]);
    }
}
