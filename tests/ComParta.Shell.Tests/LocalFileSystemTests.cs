namespace ComParta.Shell.Tests;

public sealed class LocalFileSystemTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "comparta-files", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private void Write(string relative)
    {
        var path = Path.Combine(_folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, relative);
    }

    [Fact]
    public void A_folder_lists_every_file_inside_it_and_its_folders_in_order_but_not_hidden_ones()
    {
        Write("b.jpg");
        Write("A.jpg");
        Write(Path.Combine("day 2", "hill.jpg"));
        Write("secret.txt");
        File.SetAttributes(Path.Combine(_folder, "secret.txt"), FileAttributes.Hidden);
        Directory.CreateDirectory(Path.Combine(_folder, "empty"));
        var files = new LocalFileSystem();

        var listed = files.ListFiles(_folder);

        Assert.True(files.FolderExists(_folder));
        Assert.False(files.FolderExists(Path.Combine(_folder, "b.jpg")));
        var expected = new List<string> { "A.jpg", "b.jpg", Path.Combine("day 2", "hill.jpg") };
        // Linux has no hidden attribute; a name starting with a dot is how it hides files.
        if (!OperatingSystem.IsWindows())
            expected.Add("secret.txt");
        Assert.Equal(expected.Order(StringComparer.OrdinalIgnoreCase), listed);
    }

    [Fact]
    public void A_folder_that_is_not_there_cannot_be_listed()
    {
        Assert.Throws<DirectoryNotFoundException>(() => new LocalFileSystem().ListFiles(Path.Combine(_folder, "gone")));
    }
}
