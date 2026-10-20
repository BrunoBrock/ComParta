using ComParta.Core.Ports;

namespace ComParta.Shell;

/// <summary>Files on this computer's disks. Reading never stops others from writing or deleting a file.</summary>
public sealed class LocalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public long GetReadableFileSize(string path)
    {
        using var stream = OpenRead(path);
        return stream.Length;
    }

    public void DeleteFile(string path) => File.Delete(path);

    public void WriteNewFile(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(content);
    }

    public byte[] ReadSmallFile(string path, int maxBytes)
    {
        using var stream = OpenRead(path);
        if (stream.Length > maxBytes)
            throw new IOException("The file is larger than allowed.");
        var content = new byte[stream.Length];
        stream.ReadExactly(content);
        return content;
    }

    public Stream OpenRead(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: true);

    public bool FolderExists(string path) => Directory.Exists(path);

    public IReadOnlyList<string> ListFiles(string folder)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            // Links to elsewhere could lead out of the folder, or round in a circle.
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
        };
        return [.. Directory.EnumerateFiles(folder, "*", options)
            .Select(path => Path.GetRelativePath(folder, path))
            .Order(StringComparer.OrdinalIgnoreCase)];
    }
}
