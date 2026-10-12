namespace ComParta.Shell.Tests;

public sealed class StartupEntryTests
{
    [Fact]
    public void Windows_runs_the_app_hidden_at_sign_in()
    {
        var command = StartupEntry.CommandFor(@"C:\Program Files\ComParta\ComParta.exe");

        Assert.Equal("\"C:\\Program Files\\ComParta\\ComParta.exe\" --background", command);
    }

    [Theory]
    [InlineData(new[] { "ComParta.exe", "--background" }, true)]
    [InlineData(new[] { "ComParta.exe", "--BACKGROUND" }, true)]
    [InlineData(new[] { "ComParta.exe", "comparta://receive?x" }, false)]
    [InlineData(new[] { "ComParta.exe" }, false)]
    public void Only_the_start_up_entry_starts_hidden(string[] arguments, bool hidden) =>
        Assert.Equal(hidden, StartupEntry.IsBackgroundLaunch(arguments));

    [Fact]
    public void Elsewhere_than_Windows_nothing_is_written()
    {
        if (OperatingSystem.IsWindows())
            return;
        Assert.False(StartupEntry.Set("/app/ComParta", on: true));
    }

    [Fact]
    public void The_sleep_guard_and_icon_do_nothing_harmful_elsewhere_than_Windows()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var guard = new SleepGuard();
        guard.Hold(true);
        guard.Hold(false);
        using var icon = new TrayIcon("ComParta", "ComParta.exe", () => ("Open", "Quit"), () => { }, () => { }, _ => { });
    }
}
