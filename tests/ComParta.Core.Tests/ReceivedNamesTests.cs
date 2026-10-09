namespace ComParta.Core.Tests;

public sealed class ReceivedNamesTests
{
    [Theory]
    [InlineData("birthday.mp4", "birthday.mp4")]
    [InlineData(@"..\..\Windows\System32\evil.dll", "evil.dll")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData(@"C:\Users\me\startup.bat", "startup.bat")]
    [InlineData("a<b>c:d|e?f*.txt", "a_b_c_d_e_f_.txt")]
    [InlineData("trailing dots...", "trailing dots")]
    [InlineData("  spaced.txt  ", "spaced.txt")]
    [InlineData("CON", "_CON")]
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("..", "Received file")]
    [InlineData("", "Received file")]
    [InlineData(null, "Received file")]
    [InlineData("tab\there.txt", "tab_here.txt")]
    public void Sanitize_gives_one_safe_name(string? received, string expected)
    {
        Assert.Equal(expected, ReceivedNames.Sanitize(received));
    }

    [Fact]
    public void Sanitize_shortens_very_long_names_but_keeps_the_extension()
    {
        var name = ReceivedNames.Sanitize(new string('a', 400) + ".mp4");

        Assert.Equal(150, name.Length);
        Assert.EndsWith(".mp4", name);
    }

    [Fact]
    public void A_hostile_name_stays_inside_the_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "Received");

        var path = ReceivedNames.UniquePathUnder(root, @"..\..\escape.exe", _ => false);

        Assert.True(ReceivedNames.IsInside(root, path));
        Assert.Equal(Path.Combine(root, "escape.exe"), path);
    }

    [Fact]
    public void An_existing_name_gets_a_number_instead_of_being_overwritten()
    {
        var root = Path.Combine(Path.GetTempPath(), "Received");
        var taken = new HashSet<string> { Path.Combine(root, "photo.jpg"), Path.Combine(root, "photo (2).jpg") };

        var path = ReceivedNames.UniquePathUnder(root, "photo.jpg", taken.Contains);

        Assert.Equal(Path.Combine(root, "photo (3).jpg"), path);
    }

    [Fact]
    public void IsInside_rejects_paths_that_climb_out()
    {
        var root = Path.Combine(Path.GetTempPath(), "Received");

        Assert.False(ReceivedNames.IsInside(root, Path.Combine(root, "..", "elsewhere.txt")));
        Assert.False(ReceivedNames.IsInside(root, root + "-other" + Path.DirectorySeparatorChar + "x"));
        Assert.True(ReceivedNames.IsInside(root, Path.Combine(root, "sub", "x.txt")));
    }
}
