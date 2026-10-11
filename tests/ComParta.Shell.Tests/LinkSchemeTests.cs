namespace ComParta.Shell.Tests;

public sealed class LinkSchemeTests
{
    [Fact]
    public void A_clicked_link_starts_the_app_with_the_link_as_its_one_argument() =>
        Assert.Equal("\"C:\\Apps\\ComParta\\ComParta.exe\" \"%1\"", LinkScheme.CommandFor(@"C:\Apps\ComParta\ComParta.exe"));

    [Fact]
    public void Off_Windows_nothing_is_registered()
    {
        if (!OperatingSystem.IsWindows())
            Assert.False(LinkScheme.Register("/tmp/ComParta"));
    }
}
