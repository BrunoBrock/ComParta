namespace ComParta.Core.Tests;

public sealed class InviteCodecTests
{
    private static readonly InviteHints Natal = new("Paulo", "Natal 2024.mp4", 4_509_715_660, 1);

    [Fact]
    public void A_link_reads_back_the_engine_text_and_the_hints()
    {
        var link = InviteCodec.Write("engine text &x=1", Natal);

        Assert.StartsWith(InviteCodec.Prefix, link);
        Assert.True(InviteCodec.TryRead(link, out var invite));
        Assert.Equal("engine text &x=1", invite.EngineText);
        Assert.Equal(Natal, invite.Hints);
        Assert.True(invite.IsLink);
    }

    [Fact]
    public void The_link_carries_no_readable_engine_text()
    {
        var link = InviteCodec.Write("engine-specific words", Natal);

        Assert.DoesNotContain("engine", link);
        Assert.DoesNotContain(' ', link);
    }

    [Fact]
    public void A_link_inside_a_chat_message_is_found()
    {
        var link = InviteCodec.Write("engine text", Natal);
        var message = $"Oi vó! 🎄😀\r\nPaulo quer te mandar um vídeo.\n1. Baixe o ComParta\n2. Abra: {link}\n\nBeijos ❤️";

        Assert.Equal(link, InviteCodec.FindLink(message));
        Assert.True(InviteCodec.TryRead(message, out var invite));
        Assert.Equal("engine text", invite.EngineText);
    }

    [Fact]
    public void Text_without_a_link_is_passed_on_whole_for_the_engine_to_judge()
    {
        Assert.True(InviteCodec.TryRead("  first-build invite text \n", out var invite));

        Assert.Equal("first-build invite text", invite.EngineText);
        Assert.Equal(InviteHints.None, invite.Hints);
        Assert.False(invite.IsLink);
        Assert.Null(InviteCodec.FindLink("first-build invite text"));
    }

    [Theory]
    [InlineData("comparta://receive/v1/")]
    [InlineData("comparta://receive/v1/not-base64-json")]
    [InlineData("see comparta://receive/v1/e30 there")] // {} has no engine text
    [InlineData("comparta://receive/v1/A")]
    public void A_link_with_a_broken_payload_is_refused(string text)
    {
        Assert.False(InviteCodec.TryRead(text, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_text_is_refused(string? text)
    {
        Assert.False(InviteCodec.TryRead(text, out _));
    }

    [Fact]
    public void Hints_are_display_text_only()
    {
        var link = InviteCodec.Write("engine", new InviteHints("Pa\u0007ulo", "..\\..\\Windows\r\nevil.exe", -5, 0));

        Assert.True(InviteCodec.TryRead(link, out var invite));
        Assert.Equal("Paulo", invite.Hints.SenderName);
        Assert.Equal("..\\..\\Windowsevil.exe", invite.Hints.Name);
        Assert.Null(invite.Hints.SizeBytes);
        Assert.Null(invite.Hints.ItemCount);
    }

    [Fact]
    public void A_very_long_hint_is_cut_short()
    {
        var link = InviteCodec.Write("engine", new InviteHints(new string('a', 5_000), null, null, null));

        Assert.True(InviteCodec.TryRead(link, out var invite));
        Assert.Equal(200, invite.Hints.SenderName!.Length);
    }
}
