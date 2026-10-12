namespace ComParta.Core.Tests;

public sealed class TimeLeftTests
{
    private readonly ManualClock _clock = new(DateTimeOffset.UnixEpoch);

    [Fact]
    public void Steady_progress_gives_a_stable_estimate()
    {
        var timeLeft = new TimeLeft(_clock);
        Assert.Null(timeLeft.Update(0, 6_000));

        var estimates = new List<long?>();
        for (var second = 1; second <= 30; second++)
        {
            _clock.Now += TimeSpan.FromSeconds(1);
            estimates.Add(timeLeft.Update(second * 100, 6_000 - second * 100));
        }

        Assert.Equal(Enumerable.Range(1, 30).Select(second => (long?)(60 - second)), estimates);
    }

    [Fact]
    public void A_short_burst_moves_the_estimate_only_part_way()
    {
        var timeLeft = new TimeLeft(_clock);
        timeLeft.Update(0, 10_000);
        _clock.Now += TimeSpan.FromSeconds(1);
        Assert.Equal(100, timeLeft.Update(100, 10_000));

        _clock.Now += TimeSpan.FromSeconds(1);
        var afterBurst = timeLeft.Update(1_100, 10_000);

        Assert.InRange(afterBurst!.Value, 11, 99);
    }

    [Fact]
    public void A_stall_makes_it_unknown_until_bytes_move_again()
    {
        var timeLeft = new TimeLeft(_clock);
        timeLeft.Update(0, 1_000);
        _clock.Now += TimeSpan.FromSeconds(1);
        Assert.NotNull(timeLeft.Update(100, 900));

        for (var second = 0; second < TimeLeft.StallAfter.TotalSeconds; second++)
        {
            _clock.Now += TimeSpan.FromSeconds(1);
            timeLeft.Update(100, 900);
        }
        Assert.Null(timeLeft.Update(100, 900));

        _clock.Now += TimeSpan.FromSeconds(1);
        Assert.Equal(8, timeLeft.Update(200, 800));
    }

    [Fact]
    public void Nothing_left_or_a_count_that_starts_again_is_unknown()
    {
        var timeLeft = new TimeLeft(_clock);
        timeLeft.Update(0, 100);
        _clock.Now += TimeSpan.FromSeconds(1);
        Assert.Null(timeLeft.Update(100, 0));

        _clock.Now += TimeSpan.FromSeconds(1);
        Assert.Null(timeLeft.Update(10, 90));
    }
}
