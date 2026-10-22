using ComParta.Core.Ports;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

public sealed class SendingUseCasesTests
{
    private static readonly string Video = Path.Combine(Path.GetTempPath(), "Videos", "birthday.mp4");

    private readonly FakeEngine _engine = new();
    private readonly FakeFilePicker _picker = new() { NextPick = Video };
    private readonly FakeFileSystem _files = new();
    private readonly ManualClock _clock = new(DateTimeOffset.UnixEpoch);
    private readonly ShareUseCases _shares;
    private readonly SendingUseCases _sending;
    private int _changes;

    public SendingUseCasesTests()
    {
        _files.Files[Video] = 10_000;
        _shares = new ShareUseCases(new FakeShareRepository(), _engine, _picker, _files);
        _sending = new SendingUseCases(_engine, _shares, _clock);
        _sending.Changed += () => _changes++;
    }

    private void Move(ShareId id, ItemActivity activity) =>
        _engine.Move(EngineActivity.None with { Shares = new Dictionary<ShareId, ItemActivity> { [id] = activity } });

    private async Task<ShareId> ShareAsync() => (await _shares.AddAsync()).Value!.Share!.Share.Id;

    [Fact]
    public async Task A_share_nobody_gets_is_idle()
    {
        var id = await ShareAsync();

        Move(id, new ItemActivity(0, 0, 1, 0, 0));

        Assert.Equal(Sending.Idle, _sending.For(id));
        Assert.Equal(0, _changes);
    }

    [Fact]
    public async Task A_device_getting_the_file_shows_its_progress_and_time_left_then_it_is_sent()
    {
        var id = await ShareAsync();

        Move(id, new ItemActivity(0, 1_000, 1, 0, 1_000, Receivers: 1, ReceiversPercent: 10));
        _clock.Now += TimeSpan.FromSeconds(1);
        Move(id, new ItemActivity(0, 1_000, 1, 0, 2_000, Receivers: 1, ReceiversPercent: 20));

        Assert.Equal(new Sending(SendingActivity.Sending, 1, 20, 0, 8), _sending.For(id));

        Move(id, new ItemActivity(0, 0, 0, 0, 10_000, Finished: 1));

        Assert.Equal(new Sending(SendingActivity.Sent, 0, 0, 1, null), _sending.For(id));
        Assert.Equal(3, _changes);
    }

    [Fact]
    public async Task Two_devices_count_twice_for_what_is_left()
    {
        var id = await ShareAsync();

        Move(id, new ItemActivity(0, 2_000, 2, 0, 0, Receivers: 2, ReceiversPercent: 50));
        _clock.Now += TimeSpan.FromSeconds(1);
        Move(id, new ItemActivity(0, 2_000, 2, 0, 2_000, Receivers: 2, ReceiversPercent: 50));

        // Each has 5 000 bytes left, 10 000 in all, at 2 000 bytes a second.
        Assert.Equal(5, _sending.For(id).SecondsLeft);
    }

    [Fact]
    public async Task Nothing_is_kept_for_a_share_the_engine_no_longer_reports()
    {
        var id = await ShareAsync();
        Move(id, new ItemActivity(0, 0, 0, 0, 10_000, Finished: 2));

        _engine.Move(EngineActivity.None);

        Assert.Equal(Sending.Idle, _sending.For(id));
        Assert.Equal(Sending.Idle, _sending.For(ShareId.New()));
    }
}
