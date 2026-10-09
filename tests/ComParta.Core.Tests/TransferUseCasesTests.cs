using ComParta.Core.Ports;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

public sealed class TransferUseCasesTests
{
    private readonly FakeTransferRepository _repository = new();
    private readonly FakeEngine _engine = new();

    private TransferUseCases Create() => new(_repository, _engine);

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
    public async Task A_transfer_the_engine_lost_is_dropped_at_start_up()
    {
        await Create().ReceiveAsync("invite:abc");
        _engine.Receiving.Clear();

        var restarted = Create();
        await restarted.StartAsync();

        Assert.Empty(restarted.List());
        Assert.Empty(_repository.Items);
    }
}
