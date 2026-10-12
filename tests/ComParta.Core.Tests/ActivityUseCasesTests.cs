using ComParta.Core.Ports;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

public sealed class ActivityUseCasesTests
{
    private readonly FakeEngine _engine = new();
    private readonly FakeSettingsRepository _settings = new();
    private readonly ActivityUseCases _activity;
    private readonly List<Activity> _raised = [];

    public ActivityUseCasesTests()
    {
        _activity = new ActivityUseCases(_engine, _settings);
        _activity.Changed += _raised.Add;
    }

    private static EngineActivity Totals(long receiving, long sending) =>
        EngineActivity.None with { ReceivingBytesPerSecond = receiving, SendingBytesPerSecond = sending };

    [Fact]
    public void Totals_are_reported_only_when_they_change()
    {
        _engine.Move(Totals(1_000, 200));
        _engine.Move(Totals(1_000, 200));
        _engine.Move(Totals(0, 0));

        Assert.Equal([new Activity(1_000, 200, false, false), new Activity(0, 0, false, false)], _raised);
        Assert.Equal(new Activity(0, 0, false, false), _activity.Current());
    }

    [Fact]
    public async Task Stopping_is_applied_stored_and_reported()
    {
        await _activity.SetStoppedAsync(sending: true, receiving: true);

        Assert.Equal((true, true), _engine.StoppedFlags);
        Assert.Equal("true", _settings.Values["activity.sendingStopped"]);
        Assert.Equal("true", _settings.Values["activity.receivingStopped"]);
        Assert.Equal(new Activity(0, 0, true, true), Assert.Single(_raised));

        await _activity.SetStoppedAsync(sending: false, receiving: true);

        Assert.Equal((false, true), _engine.StoppedFlags);
        Assert.False(_settings.Values.ContainsKey("activity.sendingStopped"));
        Assert.Equal(new Activity(0, 0, false, true), _raised[^1]);
    }

    [Fact]
    public async Task A_stop_chosen_before_a_restart_is_applied_at_start()
    {
        _settings.Values["activity.receivingStopped"] = "true";

        await _activity.StartAsync();

        Assert.Equal((false, true), _engine.StoppedFlags);
        Assert.True(_activity.Current().ReceivingStopped);
        Assert.False(_activity.Current().SendingStopped);
    }

    [Fact]
    public async Task Nothing_stopped_before_means_nothing_stopped_at_start()
    {
        await _activity.StartAsync();

        Assert.Equal(1, _engine.StopCalls);
        Assert.Equal((false, false), _engine.StoppedFlags);
    }

    [Fact]
    public void Each_item_reports_its_own_activity_or_none()
    {
        var share = ShareId.New();
        var transfer = TransferId.New();
        _engine.Activity = new EngineActivity(
            10, 20,
            new Dictionary<ShareId, ItemActivity> { [share] = new(0, 20, 1, 0, 5_000) },
            new Dictionary<TransferId, ItemActivity> { [transfer] = new(10, 0, 2, 7_000, 0) });

        Assert.Equal(5_000, _activity.For(share).BytesSent);
        Assert.Equal(2, _activity.For(transfer).Connections);
        Assert.Same(ItemActivity.None, _activity.For(ShareId.New()));
        Assert.Same(ItemActivity.None, _activity.For(TransferId.New()));
    }
}
