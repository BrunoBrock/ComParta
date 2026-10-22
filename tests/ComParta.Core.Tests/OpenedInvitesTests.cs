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

    private static readonly string OpenedFile = Path.Combine(Path.GetTempPath(), "Downloads", "film.download");

    private static OpenedInvites WithEngine() =>
        new(text => text.StartsWith("engine:", StringComparison.Ordinal) ? text : null, [".download"]);

    [Fact]
    public void A_download_link_the_engine_reads_waits_like_an_invite()
    {
        var opened = WithEngine();

        Assert.True(opened.Open(["ComParta.exe", "engine:?film"]));

        Assert.Equal("engine:?film", opened.Take());
        Assert.Null(opened.TakeFile());
    }

    [Fact]
    public void A_download_file_waits_by_its_path_until_taken_once()
    {
        var opened = WithEngine();
        var raised = 0;
        opened.Opened += () => raised++;

        Assert.True(opened.Open(["ComParta.exe", OpenedFile]));

        Assert.Equal(1, raised);
        Assert.Null(opened.Take());
        Assert.Equal(OpenedFile, opened.TakeFile());
        Assert.Null(opened.TakeFile());
    }

    [Fact]
    public void Only_a_whole_path_with_the_right_ending_is_a_download_file()
    {
        var opened = WithEngine();

        Assert.False(opened.Open(["film.download", Path.Combine(Path.GetTempPath(), "film.mkv"), "--background"]));
        Assert.False(new OpenedInvites().Open([OpenedFile]));
    }

    [Fact]
    public void The_newest_of_a_link_and_a_file_wins()
    {
        var opened = WithEngine();
        opened.Open([OpenedFile]);

        opened.Open(["engine:?other"]);

        Assert.Null(opened.TakeFile());
        Assert.Equal("engine:?other", opened.Take());
    }
}
