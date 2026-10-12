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
    [InlineData("photo\u202Egpj.exe", "photogpj.exe")]
    [InlineData("\u2067report\u2069.pdf\u200F", "report.pdf")]
    [InlineData("holiday.jpg                                   .exe", "holiday.jpg .exe")]
    [InlineData("film.mp4:hidden.exe", "film.mp4_hidden.exe")]
    [InlineData("film.mp4::$DATA", "film.mp4__$DATA")]
    [InlineData("CON .txt", "_CON .txt")]
    [InlineData("com1.tar.gz", "_com1.tar.gz")]
    [InlineData("LPT9", "_LPT9")]
    [InlineData("COM¹.txt", "_COM¹.txt")]
    [InlineData("CONOUT$", "_CONOUT$")]
    [InlineData("CONSOLE.txt", "CONSOLE.txt")]
    [InlineData("name. . .", "name")]
    [InlineData("bell\u0007.txt", "bell_.txt")]
    [InlineData("\u202E", "Received file")]
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
    public void A_very_long_name_hiding_a_program_still_ends_with_its_real_extension()
    {
        var name = ReceivedNames.Sanitize("photo" + new string(' ', 10) + new string('a', 400) + ".jpg\u202E.exe");

        Assert.EndsWith("a.exe", name);
        Assert.False(name.Contains('\u202E'));
        Assert.True(name.Length <= 150);
    }

    [Theory]
    [InlineData("setup.exe", true)]
    [InlineData("SETUP.EXE", true)]
    [InlineData("run.bat", true)]
    [InlineData("script.ps1", true)]
    [InlineData("shortcut.lnk", true)]
    [InlineData("disk.iso", true)]
    [InlineData("budget.xlsm", true)]
    [InlineData("page.html", true)]
    [InlineData("photo\u202Egpj.exe", true)]
    [InlineData("holiday.exe.", true)]
    [InlineData("film.mp4", false)]
    [InlineData("photo.jpg", false)]
    [InlineData("budget.xlsx", false)]
    [InlineData("letter.pdf", false)]
    [InlineData("archive.zip", false)]
    [InlineData("README", false)]
    [InlineData(null, false)]
    public void Programs_scripts_and_macro_documents_are_risky(string? name, bool risky)
    {
        Assert.Equal(risky, ReceivedNames.IsRisky(name));
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
