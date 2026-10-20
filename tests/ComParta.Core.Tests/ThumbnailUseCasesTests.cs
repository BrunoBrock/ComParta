using ComParta.Core.Ports;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

public sealed class ThumbnailUseCasesTests
{
    private readonly FakeEngine _engine = new();
    private readonly FakeFilePicker _picker = new();
    private readonly FakeFileSystem _files = new();
    private readonly FakeThumbnails _provider = new();
    private readonly FakePaths _paths = new(Path.Combine(Path.GetTempPath(), "comparta-thumbnail-tests"));
    private readonly ShareUseCases _shares;
    private readonly TransferUseCases _transfers;
    private readonly ThumbnailUseCases _thumbnails;

    public ThumbnailUseCasesTests()
    {
        _shares = new ShareUseCases(new FakeShareRepository(), _engine, _picker, _files);
        _transfers = new TransferUseCases(new FakeTransferRepository(), _engine, files: _files);
        var received = new ReceivedUseCases(_transfers, _paths, new FakeOpener(), _files);
        _thumbnails = new ThumbnailUseCases(_shares, received, _provider);
    }

    private async Task<ShareId> ShareAsync(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "Pictures", name);
        _files.Files[path] = 2_000;
        _picker.NextPick = path;
        return (await _shares.AddAsync()).Value!.Share!.Share.Id;
    }

    [Fact]
    public async Task A_shared_photo_is_drawn_from_its_own_file_at_a_sensible_size()
    {
        var id = await ShareAsync("beach.JPG");

        var small = await _thumbnails.ForShareAsync(id, 4);
        var huge = await _thumbnails.ForShareAsync(id, 10_000);

        Assert.Equal([1, 2, 3], small.Value!.Bytes);
        Assert.NotNull(huge.Value);
        Assert.Equal([ThumbnailUseCases.SmallestSize, ThumbnailUseCases.LargestSize], _provider.Asked.Select(a => a.Size));
        Assert.All(_provider.Asked, a => Assert.EndsWith("beach.JPG", a.Path));
    }

    [Fact]
    public async Task Other_files_get_no_picture_and_the_system_is_not_asked()
    {
        var id = await ShareAsync("letter.pdf");

        var result = await _thumbnails.ForShareAsync(id, 96);

        Assert.True(result.Ok);
        Assert.Null(result.Value);
        Assert.Empty(_provider.Asked);
    }

    [Fact]
    public async Task A_picture_the_system_cannot_draw_falls_back_to_the_icon()
    {
        var id = await ShareAsync("clip.mp4");

        _provider.Throws = true;
        Assert.Null((await _thumbnails.ForShareAsync(id, 96)).Value);

        _provider.Throws = false;
        _provider.Next = new Thumbnail("text/html", [1]);
        Assert.Null((await _thumbnails.ForShareAsync(id, 96)).Value);

        _provider.Next = new Thumbnail("image/jpeg", new byte[ThumbnailUseCases.MaxBytes + 1]);
        Assert.Null((await _thumbnails.ForShareAsync(id, 96)).Value);
    }

    [Fact]
    public async Task Only_a_received_file_that_has_arrived_has_a_picture()
    {
        var waiting = (await _transfers.ReceiveAsync("invite:holiday.mp4")).Value!.Transfer.Id;
        Assert.Null((await _thumbnails.ForTransferAsync(waiting, 96)).Value);

        _engine.Report(new ReceiveProgress(waiting, "holiday.mp4", 10, 10, true));
        _files.Files[Path.Combine(_paths.ReceivedRoot, "holiday.mp4")] = 10;
        Assert.NotNull((await _thumbnails.ForTransferAsync(waiting, 96)).Value);

        Assert.Equal(ErrorKind.TransferNotFound, (await _thumbnails.ForTransferAsync(TransferId.New(), 96)).Error);
        Assert.Equal(ErrorKind.ShareNotFound, (await _thumbnails.ForShareAsync(ShareId.New(), 96)).Error);
    }

    [Fact]
    public async Task A_folder_shows_its_first_four_photos_and_videos_and_nothing_else()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Pictures", "Trip");
        string[] names = ["notes.txt", "a.jpg", "b.mp4", "song.mp3", "c.png", "d.jpg", "e.jpg"];
        foreach (var name in names)
            _files.Files[Path.Combine(folder, name)] = 100;
        _picker.NextFolder = folder;
        var id = (await _shares.AddFolderAsync()).Value!.Share.Id;

        for (var item = 0; item < 6; item++)
            await _thumbnails.ForShareAsync(id, 96, item);

        Assert.Equal(["a.jpg", "b.mp4", "c.png", "d.jpg"], _provider.Asked.Select(a => Path.GetFileName(a.Path)));
        Assert.Null((await _thumbnails.ForShareAsync(id, 96, -1)).Value);
    }

    [Fact]
    public async Task A_single_file_has_only_its_own_picture()
    {
        var id = await ShareAsync("beach.jpg");

        Assert.NotNull((await _thumbnails.ForShareAsync(id, 96, 0)).Value);
        Assert.Null((await _thumbnails.ForShareAsync(id, 96, 1)).Value);
        Assert.Single(_provider.Asked);
    }
}
