using ComParta.Core.Ports;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

public sealed class ReceivedUseCasesTests
{
    private readonly FakeTransferRepository _repository = new();
    private readonly FakeEngine _engine = new();
    private readonly FakeFileSystem _files = new();
    private readonly FakeOpener _opener = new();
    private readonly FakePaths _paths = new(Path.Combine(Path.GetTempPath(), "comparta-received-tests"));
    private readonly TransferUseCases _transfers;
    private readonly ReceivedUseCases _received;

    public ReceivedUseCasesTests()
    {
        _transfers = new TransferUseCases(_repository, _engine, files: _files);
        _received = new ReceivedUseCases(_transfers, _paths, _opener, _files);
    }

    /// <summary>A transfer that has arrived under this name, with its file on disk.</summary>
    private async Task<TransferId> ArrivedAsync(string name, bool onDisk = true)
    {
        var id = (await _transfers.ReceiveAsync($"invite:{name}")).Value!.Transfer.Id;
        _engine.Report(new ReceiveProgress(id, name, 10, 10, true));
        if (onDisk)
            _files.Files[Path.Combine(_paths.ReceivedRoot, name)] = 10;
        return id;
    }

    [Fact]
    public async Task Open_and_show_in_folder_use_the_file_under_Received()
    {
        var id = await ArrivedAsync("recipe.pdf");

        Assert.True((await _received.OpenAsync(id)).Ok);
        Assert.True((await _received.RevealAsync(id)).Ok);

        var expected = Path.Combine(Path.GetFullPath(_paths.ReceivedRoot), "recipe.pdf");
        Assert.Equal([expected], _opener.Opened);
        Assert.Equal([expected], _opener.Revealed);
    }

    [Fact]
    public async Task A_file_that_is_gone_or_has_not_arrived_says_so()
    {
        var gone = await ArrivedAsync("gone.pdf", onDisk: false);
        var waiting = (await _transfers.ReceiveAsync("invite:later")).Value!.Transfer.Id;

        Assert.Equal(ErrorKind.ReceivedNotFound, (await _received.OpenAsync(gone)).Error);
        Assert.Equal(ErrorKind.ReceivedNotFound, (await _received.RevealAsync(waiting)).Error);
        Assert.Equal(ErrorKind.TransferNotFound, (await _received.OpenAsync(TransferId.New())).Error);
        Assert.Empty(_opener.Opened);
    }

    [Fact]
    public async Task Programs_are_never_opened()
    {
        var id = await ArrivedAsync("setup.EXE");

        Assert.Equal(ErrorKind.ReceivedOpenFailed, (await _received.OpenAsync(id)).Error);
        Assert.Empty(_opener.Opened);
        Assert.True((await _received.RevealAsync(id)).Ok);
    }

    [Fact]
    public async Task A_file_the_system_cannot_open_says_so()
    {
        var id = await ArrivedAsync("drawing.xyz");
        _opener.OpenWorks = false;

        Assert.Equal(ErrorKind.ReceivedOpenFailed, (await _received.OpenAsync(id)).Error);
    }

    [Fact]
    public async Task Delete_removes_the_file_and_the_record()
    {
        var id = await ArrivedAsync("film.mkv");

        Assert.True((await _received.DeleteAsync(id)).Ok);

        Assert.Equal([Path.Combine(Path.GetFullPath(_paths.ReceivedRoot), "film.mkv")], _files.Deleted);
        Assert.Empty(_transfers.List());
        Assert.Empty(_repository.Items);
    }

    [Fact]
    public async Task Delete_takes_an_already_missing_file_off_the_list()
    {
        var id = await ArrivedAsync("gone.mkv", onDisk: false);

        Assert.True((await _received.DeleteAsync(id)).Ok);
        Assert.Empty(_transfers.List());
    }

    [Fact]
    public async Task A_file_in_use_is_kept_and_stays_on_the_list()
    {
        var id = await ArrivedAsync("open.docx");
        _files.Locked.Add(Path.Combine(Path.GetFullPath(_paths.ReceivedRoot), "open.docx"));

        Assert.Equal(ErrorKind.FileUnreadable, (await _received.DeleteAsync(id)).Error);
        Assert.Single(_transfers.List());
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../outside.txt")]
    public async Task Nothing_outside_the_Received_folder_is_touched(string name)
    {
        var id = await ArrivedAsync(name);
        _files.Files[Path.GetFullPath(Path.Combine(_paths.ReceivedRoot, name))] = 10;

        Assert.Equal(ErrorKind.ReceivedNotFound, (await _received.DeleteAsync(id)).Error);
        Assert.Equal(ErrorKind.ReceivedNotFound, (await _received.OpenAsync(id)).Error);
        Assert.Empty(_files.Deleted);
        Assert.Empty(_opener.Opened);
    }
}
