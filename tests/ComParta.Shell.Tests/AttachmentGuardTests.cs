using ComParta.Core.Ports;

namespace ComParta.Shell.Tests;

/// <summary>Windows' attachment services on real files; the checks run only on Windows.</summary>
public sealed class AttachmentGuardTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "comparta-guard-" + Guid.NewGuid().ToString("N"));

    public AttachmentGuardTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void A_received_file_is_marked_as_coming_from_another_computer()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var path = Path.Combine(_folder, "holiday notes.txt");
        File.WriteAllText(path, "A few lines from a friend.");

        Assert.Equal(ReceivedFileVerdict.Passed, new AttachmentGuard().Check(path));

        Assert.Contains("ZoneId=3", File.ReadAllText(path + ":Zone.Identifier"));
        Assert.Equal("A few lines from a friend.", File.ReadAllText(path));
    }

    [Fact]
    public void A_file_that_is_gone_afterwards_is_blocked()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.Equal(ReceivedFileVerdict.Blocked, new AttachmentGuard().Check(Path.Combine(_folder, "removed.txt")));
    }
}
