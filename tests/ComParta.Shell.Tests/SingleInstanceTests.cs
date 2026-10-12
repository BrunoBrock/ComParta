namespace ComParta.Shell.Tests;

public sealed class SingleInstanceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static string NewKey() => SingleInstance.KeyFor(Path.Combine(Path.GetTempPath(), "comparta-tests", Guid.NewGuid().ToString("N")));

    [Fact]
    public async Task A_second_launch_hands_its_link_to_the_first_and_is_told_to_exit()
    {
        var key = NewKey();
        using var first = SingleInstance.TryClaim(key);
        Assert.NotNull(first);
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.ArgumentsReceived += arguments => received.TrySetResult(arguments);

        // The second launch finds the name taken, so it forwards instead of starting.
        Assert.Null(SingleInstance.TryClaim(key));
        Assert.True(await SingleInstance.ForwardAsync(key, ["ComParta.exe", "comparta://receive/v1/abc"], Timeout));

        Assert.Equal(["ComParta.exe", "comparta://receive/v1/abc"], await received.Task.WaitAsync(Timeout));
    }

    [Fact]
    public async Task Several_launches_in_a_row_all_get_through()
    {
        var key = NewKey();
        using var first = SingleInstance.TryClaim(key)!;
        var received = new List<string>();
        var all = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.ArgumentsReceived += arguments =>
        {
            lock (received)
            {
                received.Add(arguments[0]);
                if (received.Count == 3)
                    all.TrySetResult();
            }
        };

        for (var i = 0; i < 3; i++)
            Assert.True(await SingleInstance.ForwardAsync(key, [$"link {i}"], Timeout));

        await all.Task.WaitAsync(Timeout);
        Assert.Equal(["link 0", "link 1", "link 2"], received);
    }

    [Fact]
    public async Task With_no_first_copy_forwarding_gives_up()
    {
        Assert.False(await SingleInstance.ForwardAsync(NewKey(), ["x"], TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public void The_name_is_free_again_once_the_first_copy_ends()
    {
        var key = NewKey();
        SingleInstance.TryClaim(key)!.Dispose();

        using var again = SingleInstance.TryClaim(key);

        Assert.NotNull(again);
    }

    [Fact]
    public void Two_data_folders_never_share_a_name()
    {
        var root = Path.Combine(Path.GetTempPath(), "comparta-tests");
        Assert.NotEqual(SingleInstance.KeyFor(Path.Combine(root, "deviceA")), SingleInstance.KeyFor(Path.Combine(root, "deviceB")));
        Assert.Equal(SingleInstance.KeyFor(Path.Combine(root, "deviceA")), SingleInstance.KeyFor(Path.Combine(root, "deviceA") + Path.DirectorySeparatorChar));
    }
}
