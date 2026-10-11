using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

public sealed class OpenedInvitesTests
{
    private static readonly string Link = InviteCodec.Write("engine-text", new InviteHints(null, "video.mp4", 10, 1));

    [Fact]
    public void A_link_among_the_arguments_waits_until_taken_once()
    {
        var opened = new OpenedInvites();
        var raised = 0;
        opened.Opened += () => raised++;

        Assert.True(opened.Open([@"C:\Program Files\ComParta\ComParta.exe", Link]));

        Assert.Equal(1, raised);
        Assert.Equal(Link, opened.Take());
        Assert.Null(opened.Take());
    }

    [Fact]
    public void Arguments_without_a_link_are_ignored()
    {
        var opened = new OpenedInvites();
        var raised = 0;
        opened.Opened += () => raised++;

        Assert.False(opened.Open(["ComParta.exe", "--something", "https://example.com/file.zip"]));
        Assert.False(opened.Open([]));

        Assert.Equal(0, raised);
        Assert.Null(opened.Take());
    }

    [Fact]
    public void The_newest_link_wins_and_a_trailing_slash_is_dropped()
    {
        var opened = new OpenedInvites();
        var older = InviteCodec.Write("older", InviteHints.None);

        opened.Open([older]);
        opened.Open([Link + "/"]);

        Assert.Equal(Link, opened.Take());
    }
}
