using ComParta.Core.Ports;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

/// <summary>Waiting, no space and blocked (CAP-8): each says so, and the first two end by themselves.</summary>
public sealed class StuckTransferTests
{
    private readonly FakeTransferRepository _repository = new();
    private readonly FakeEngine _engine = new();
    private readonly FakeStorage _storage = new();
    private readonly ManualClock _clock = new(DateTimeOffset.UnixEpoch);
    private readonly TransferUseCases _transfers;

    public StuckTransferTests() => _transfers = new TransferUseCases(_repository, _engine, _clock, storage: _storage);

    private async Task<TransferId> ReceiveAsync() => (await _transfers.ReceiveAsync("invite:abc")).Value!.Transfer.Id;

    private TransferView Only() => Assert.Single(_transfers.List());

    private void Tick(TimeSpan after, int connections = 0, TransferId? id = null)
    {
        _clock.Now += after;
        var transfers = new Dictionary<TransferId, ItemActivity>();
        if (id is { } connected)
            transfers[connected] = new ItemActivity(0, 0, connections, 0, 0);
        _engine.Move(EngineActivity.None with { Transfers = transfers });
    }

    [Fact]
    public async Task Two_minutes_without_a_connection_is_waiting_and_a_connection_ends_it()
    {
        var id = await ReceiveAsync();

        Tick(TimeSpan.FromMinutes(1));
        Assert.Equal(TransferState.Connecting, Only().State);
        Tick(TimeSpan.FromMinutes(1));
        Assert.Equal(TransferState.Waiting, Only().State);

        Tick(TimeSpan.FromSeconds(1), connections: 1, id: id);
        Assert.Equal(TransferState.Connecting, Only().State);
    }

    [Fact]
    public async Task A_transfer_half_way_waits_when_the_other_device_goes_away_and_goes_on_when_bytes_arrive()
    {
        var id = await ReceiveAsync();
        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 500, false));

        Tick(TransferUseCases.WaitingAfter);
        // A report that moves nothing does not end the wait.
        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 500, false));
        Assert.Equal(TransferState.Waiting, Only().State);
        Assert.Equal(500, Only().ReceivedBytes);

        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 600, false));
        Assert.Equal(TransferState.Receiving, Only().State);
    }

    [Fact]
    public async Task A_full_disk_reported_by_the_engine_pauses_until_there_is_room_and_looks_every_minute()
    {
        var id = await ReceiveAsync();
        _storage.FreeBytes = 100;
        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 400, false, NoSpace: true));

        var stuck = Only();
        Assert.Equal(TransferState.NoSpace, stuck.State);
        Assert.Equal(500, stuck.NeededBytes);
        // Late reports from before do not undo it.
        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 400, false));
        Assert.Equal(TransferState.NoSpace, Only().State);

        Tick(TransferUseCases.NoSpaceRetry);
        Assert.Equal(TransferState.NoSpace, Only().State);
        Assert.Empty(_engine.Continued);

        _storage.FreeBytes = 10_000;
        Tick(TimeSpan.FromSeconds(30));
        Assert.Empty(_engine.Continued);
        Tick(TimeSpan.FromSeconds(30));
        Assert.Equal([id], _engine.Continued);
        Assert.Equal(TransferState.Receiving, Only().State);
        Assert.Null(Only().NeededBytes);
    }

    [Fact]
    public async Task Less_room_than_what_is_left_pauses_before_the_disk_fills()
    {
        var id = await ReceiveAsync();
        _storage.FreeBytes = 300;

        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 400, false));

        Assert.Equal(TransferState.NoSpace, Only().State);
        Assert.Equal(300, Only().NeededBytes);
        Assert.Contains(id, _engine.Paused);
        // The person did not pause it, so it is not stored as paused.
        Assert.False(Only().Transfer.Paused);
    }

    [Fact]
    public async Task Unknown_room_never_stops_a_transfer()
    {
        var id = await ReceiveAsync();

        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 400, false));

        Assert.Equal(TransferState.Receiving, Only().State);
    }

    [Fact]
    public async Task A_full_disk_does_not_turn_into_waiting()
    {
        var id = await ReceiveAsync();
        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 400, false, NoSpace: true));
        _storage.FreeBytes = 0;

        Tick(TimeSpan.FromMinutes(5));

        Assert.Equal(TransferState.NoSpace, Only().State);
    }

    [Fact]
    public async Task A_paused_transfer_never_reads_as_waiting()
    {
        var id = await ReceiveAsync();
        await _transfers.PauseAsync(id);

        Tick(TimeSpan.FromHours(1));

        Assert.Equal(TransferState.Paused, Only().State);
    }
}
