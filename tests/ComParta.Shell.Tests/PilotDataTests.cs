namespace ComParta.Shell.Tests;

public sealed class PilotDataTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "comparta-pilot", Guid.NewGuid().ToString("N"));
    private string Pilot => Path.Combine(_folder, "pilot");
    private string Packaged => Path.Combine(_folder, "packaged");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static void Copy(string from, string to) => File.Copy(from, to);

    [Fact]
    public void The_pilot_database_and_engine_data_are_copied_once_and_the_pilot_is_left_alone()
    {
        Write(Pilot, "comparta.db", "database");
        Write(Pilot, Path.Combine("engine", "port.txt"), "51413");
        Write(Pilot, Path.Combine("engine", "shares", "abc", "photo.jpg"), "photo");

        Assert.True(PilotData.Import(Pilot, Packaged, Copy));

        Assert.Equal("database", File.ReadAllText(Path.Combine(Packaged, "comparta.db")));
        Assert.Equal("51413", File.ReadAllText(Path.Combine(Packaged, "engine", "port.txt")));
        Assert.Equal("photo", File.ReadAllText(Path.Combine(Packaged, "engine", "shares", "abc", "photo.jpg")));
        Assert.True(File.Exists(Path.Combine(Packaged, PilotData.MarkerName)));
        Assert.Equal("database", File.ReadAllText(Path.Combine(Pilot, "comparta.db")));

        // The pilot keeps changing, but the install now has its own data.
        Write(Pilot, Path.Combine("engine", "port.txt"), "6000");
        Assert.False(PilotData.Import(Pilot, Packaged, Copy));
        Assert.Equal("51413", File.ReadAllText(Path.Combine(Packaged, "engine", "port.txt")));
    }

    [Fact]
    public void Nothing_is_copied_without_a_pilot_database_or_when_the_install_has_one()
    {
        Write(Pilot, Path.Combine("engine", "port.txt"), "51413");
        Assert.False(PilotData.Import(Pilot, Packaged, Copy));
        Assert.False(Directory.Exists(Packaged));

        Write(Pilot, "comparta.db", "pilot");
        Write(Packaged, "comparta.db", "own");
        Assert.False(PilotData.Import(Pilot, Packaged, Copy));
        Assert.Equal("own", File.ReadAllText(Path.Combine(Packaged, "comparta.db")));
    }

    [Fact]
    public void An_import_cut_short_before_the_database_is_done_again_next_time()
    {
        Write(Pilot, "comparta.db", "database");
        Write(Pilot, Path.Combine("engine", "port.txt"), "51413");
        Assert.Throws<IOException>(() => PilotData.Import(Pilot, Packaged, (_, _) => throw new IOException("disk full")));

        Assert.True(PilotData.Import(Pilot, Packaged, Copy));
        Assert.Equal("database", File.ReadAllText(Path.Combine(Packaged, "comparta.db")));
    }
}
