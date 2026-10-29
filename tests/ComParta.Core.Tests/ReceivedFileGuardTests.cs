using ComParta.Core.Ports;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

/// <summary>Every received file goes to the system's protection before it shows as received (epic 3 story 3, AD-13).</summary>
public sealed class ReceivedFileGuardTests
{
    private static readonly FakePaths Paths = new(Path.Combine(Path.GetTempPath(), "comparta-guard"));
    private static readonly string Received = Path.GetFullPath(Paths.ReceivedRoot);
    private static readonly string Film = Path.Combine(Received, "film.mp4");

    private readonly FakeTransferRepository _repository = new();
    private readonly FakeEngine _engine = new();
    private readonly FakeFileSystem _files = new();
    private readonly FakeReceivedFileGuard _guard = new();
    private readonly FakeNotifier _notifier = new();

    public ReceivedFileGuardTests() => _files.Files[Film] = 1_000;

    private TransferUseCases Create() =>
        new(_repository, _engine, files: _files, received: new ReceivedFolder(new FakeSettingsRepository(), Paths), guard: _guard);

    private static async Task<TransferId> ReceiveAsync(TransferUseCases transfers) =>
        (await transfers.ReceiveAsync("invite:abc")).Value!.Transfer.Id;

    /// <summary>Waits until the transfer shows <paramref name="state"/>, as the screen would see it.</summary>
    private static async Task<TransferView> WaitForAsync(TransferUseCases transfers, TransferId id, TransferState state)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (transfers.List().FirstOrDefault(v => v.Transfer.Id == id) is { } view && view.State == state)
                return view;
            await Task.Delay(10);
        }
        throw new TimeoutException($"never {state}");
    }

    [Fact]
    public async Task A_file_that_passes_shows_as_received_only_after_the_check()
    {
        var transfers = Create();
        var id = await ReceiveAsync(transfers);
        _guard.Hold = new TaskCompletionSource();

        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 1_000, true));

        var checking = Assert.Single(transfers.List());
        Assert.Equal(TransferState.Receiving, checking.State);
        Assert.Equal(1_000, checking.ReceivedBytes);
        Assert.Equal(0, checking.BytesPerSecond);
        Assert.Null(checking.SecondsLeft);

        _guard.Hold.SetResult();
        var done = await WaitForAsync(transfers, id, TransferState.Done);

        Assert.Equal([Film], _guard.Checked);
        Assert.Equal(ReceivedCheck.Passed, done.Transfer.Check);
        Assert.Equal(ReceivedCheck.Passed, Assert.Single(_repository.Items).Check);
    }

    [Fact]
    public async Task A_blocked_file_shows_as_blocked_and_is_never_announced()
    {
        var transfers = Create();
        var notifications = new NotificationUseCases(
            _engine, transfers, new ShareUseCases(new FakeShareRepository(), _engine, new FakeFilePicker(), _files),
            new SendingUseCases(_engine, new ShareUseCases(new FakeShareRepository(), _engine, new FakeFilePicker(), _files)),
            new SettingsUseCases(new FakeSettingsRepository(), new FakeSystem("en")), _notifier, new FakeWindow());
        await notifications.StartAsync();
        var id = await ReceiveAsync(transfers);
        _guard.BlocksAll = true;

        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 1_000, true));
        var blocked = await WaitForAsync(transfers, id, TransferState.Blocked);

        Assert.Equal(ReceivedCheck.Blocked, blocked.Transfer.Check);
        Assert.Equal(ReceivedCheck.Blocked, Assert.Single(_repository.Items).Check);
        Assert.Empty(_notifier.Shown);
        // A late report from the engine changes nothing.
        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 1_000, true));
        Assert.Equal(TransferState.Blocked, Assert.Single(transfers.List()).State);
        Assert.Empty(_notifier.Shown);
    }

    [Fact]
    public async Task A_file_that_passes_is_announced_once()
    {
        var transfers = Create();
        var notifications = new NotificationUseCases(
            _engine, transfers, new ShareUseCases(new FakeShareRepository(), _engine, new FakeFilePicker(), _files),
            new SendingUseCases(_engine, new ShareUseCases(new FakeShareRepository(), _engine, new FakeFilePicker(), _files)),
            new SettingsUseCases(new FakeSettingsRepository(), new FakeSystem("en")), _notifier, new FakeWindow());
        await notifications.StartAsync();
        var id = await ReceiveAsync(transfers);
        _guard.Hold = new TaskCompletionSource();

        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 1_000, true));
        Assert.Empty(_notifier.Shown);
        _guard.Hold.SetResult();
        await WaitForAsync(transfers, id, TransferState.Done);

        Assert.Single(_notifier.Shown);
    }

    [Fact]
    public async Task A_folder_with_one_blocked_file_is_blocked_and_every_file_was_looked_at()
    {
        var folder = Path.Combine(Received, "Holidays");
        var photo = Path.Combine(folder, "beach.jpg");
        var tool = Path.Combine(folder, "tools", "setup.exe");
        _files.Files[photo] = 10;
        _files.Files[tool] = 10;
        _guard.Blocks.Add(tool);
        var transfers = Create();
        var id = await ReceiveAsync(transfers);

        _engine.Report(new ReceiveProgress(id, "Holidays", 20, 20, true));
        await WaitForAsync(transfers, id, TransferState.Blocked);

        Assert.Equal([photo, tool], _guard.Checked);
    }

    [Fact]
    public async Task Repeated_reports_that_it_arrived_check_it_once()
    {
        var transfers = Create();
        var id = await ReceiveAsync(transfers);
        _guard.Hold = new TaskCompletionSource();

        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 1_000, true));
        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 1_000, true));
        _guard.Hold.SetResult();
        await WaitForAsync(transfers, id, TransferState.Done);
        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 1_000, true));

        Assert.Single(_guard.Checked);
    }

    [Theory]
    [InlineData(ReceivedCheck.Passed, TransferState.Done)]
    [InlineData(ReceivedCheck.Blocked, TransferState.Blocked)]
    public async Task After_a_restart_a_checked_file_is_not_checked_again(ReceivedCheck check, TransferState shown)
    {
        var transfer = new Transfer(TransferId.New(), "film.mp4", 1_000, true, DateTimeOffset.UnixEpoch, Check: check);
        _repository.Items.Add(transfer);
        var transfers = Create();

        await transfers.StartAsync();

        Assert.Equal(shown, Assert.Single(transfers.List()).State);
        Assert.Empty(_guard.Checked);
        if (check == ReceivedCheck.Blocked)
            Assert.False(_engine.SharingWhenDone[transfer.Id]);
    }

    [Fact]
    public async Task After_a_restart_a_file_that_arrived_but_was_not_checked_is_checked_once()
    {
        var transfer = new Transfer(TransferId.New(), "film.mp4", 1_000, true, DateTimeOffset.UnixEpoch, Check: ReceivedCheck.Pending);
        _repository.Items.Add(transfer);
        var transfers = Create();

        await transfers.StartAsync();
        await WaitForAsync(transfers, transfer.Id, TransferState.Done);

        Assert.Equal([Film], _guard.Checked);
        Assert.Empty(_engine.Receiving);
        Assert.Equal(ReceivedCheck.Passed, Assert.Single(_repository.Items).Check);
    }

    [Fact]
    public async Task Removing_it_while_it_is_checked_keeps_it_removed_and_the_file_stays()
    {
        var transfers = Create();
        var id = await ReceiveAsync(transfers);
        _guard.Hold = new TaskCompletionSource();
        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 1_000, true));

        Assert.True((await transfers.RemoveAsync(id)).Ok);
        _guard.Hold.SetResult();
        await Task.Delay(100);

        Assert.Empty(transfers.List());
        Assert.Empty(_repository.Items);
        Assert.DoesNotContain(id, _engine.Stopped);
    }

    [Fact]
    public async Task It_cannot_be_paused_while_it_is_checked()
    {
        var transfers = Create();
        var id = await ReceiveAsync(transfers);
        _guard.Hold = new TaskCompletionSource();
        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 1_000, true));

        var paused = await transfers.PauseAsync(id);
        _guard.Hold.SetResult();

        Assert.Equal(ErrorKind.TransferFinished, paused.Error);
        await WaitForAsync(transfers, id, TransferState.Done);
    }

    [Fact]
    public async Task A_blocked_file_is_no_longer_shared_from_this_computer()
    {
        var transfers = Create();
        var id = await ReceiveAsync(transfers);
        _guard.BlocksAll = true;

        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 1_000, true));
        await WaitForAsync(transfers, id, TransferState.Blocked);

        Assert.False(_engine.SharingWhenDone[id]);
    }

    [Fact]
    public async Task Without_a_check_a_file_that_arrives_is_received_at_once_as_before()
    {
        var transfers = new TransferUseCases(_repository, _engine);
        var id = await ReceiveAsync(transfers);

        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 1_000, true));

        var done = Assert.Single(transfers.List());
        Assert.Equal(TransferState.Done, done.State);
        Assert.Equal(ReceivedCheck.Passed, done.Transfer.Check);
    }
}
