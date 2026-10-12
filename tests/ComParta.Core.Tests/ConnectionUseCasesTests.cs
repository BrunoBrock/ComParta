using ComParta.Core.UseCases;
using ComParta.Core.Ports;

namespace ComParta.Core.Tests;

public sealed class ConnectionUseCasesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly ReachabilityFacts Listening = ReachabilityFacts.Unknown with { Listening = true };

    public static TheoryData<ReachabilityFacts, bool?, ConnectionHealth> Cases() => new()
    {
        { ReachabilityFacts.Unknown, null, ConnectionHealth.Blocked },
        { ReachabilityFacts.Unknown with { PortMapped = true }, true, ConnectionHealth.Blocked },
        { Listening, false, ConnectionHealth.Blocked },
        { Listening with { PortMapped = true, MappedPort = 5000 }, false, ConnectionHealth.Blocked },
        { Listening, null, ConnectionHealth.Limited },
        { Listening, true, ConnectionHealth.Limited },
        { Listening with { GlobalIPv6 = true }, null, ConnectionHealth.Limited },
        { Listening with { LastIncomingAt = Now.AddHours(-2) }, true, ConnectionHealth.Limited },
        { Listening with { PortMapped = true, MappedPort = 5000 }, null, ConnectionHealth.Good },
        { Listening with { LastIncomingAt = Now.AddMinutes(-59) }, null, ConnectionHealth.Good },
        { Listening with { GlobalIPv6 = true, LastIncomingAt = Now }, true, ConnectionHealth.Good },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Each_combination_of_facts_has_one_health(ReachabilityFacts facts, bool? firewall, ConnectionHealth expected) =>
        Assert.Equal(expected, ConnectionUseCases.Decide(facts, firewall, Now));

    [Fact]
    public void Changed_is_raised_only_when_the_health_changes()
    {
        var engine = new FakeEngine { Reachability = Listening };
        var clock = new ManualClock(Now);
        var connection = new ConnectionUseCases(engine, clock: clock);
        var seen = new List<ConnectionHealth>();
        connection.Changed += seen.Add;

        Assert.Equal(ConnectionHealth.Limited, connection.Refresh());
        engine.Reach(Listening);
        engine.Reach(Listening with { LastIncomingAt = Now });
        engine.Reach(Listening with { LastIncomingAt = Now });
        clock.Now = Now.AddHours(2);
        engine.Reach(Listening with { LastIncomingAt = Now });

        Assert.Equal([ConnectionHealth.Good, ConnectionHealth.Limited], seen);
    }

    [Fact]
    public void A_firewall_that_cannot_be_asked_counts_as_not_decided()
    {
        var connection = new ConnectionUseCases(new FakeEngine { Reachability = Listening }, new BrokenFirewall());
        Assert.Equal(ConnectionHealth.Limited, connection.Refresh());
    }

    [Fact]
    public async Task Opening_settings_without_a_firewall_does_nothing()
    {
        await new ConnectionUseCases(new FakeEngine()).OpenSettingsAsync();
    }

    private sealed class BrokenFirewall : IFirewall
    {
        public bool? AllowsIncoming() => throw new InvalidOperationException();
        public Task OpenSettingsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
