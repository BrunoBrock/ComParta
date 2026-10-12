using MonoTorrent;
using MonoTorrent.PieceWriter;
using ReusableTasks;

namespace ComParta.Engine.MonoTorrent.Tests;

/// <summary>The engine's own disk writer, except that writes fail as on a full disk while <see cref="Full"/> is set.</summary>
internal sealed class FullDiskWriter(IPieceWriter inner) : IPieceWriter
{
    // ENOSPC, as .NET reports it outside Windows; the engine also knows the Windows codes.
    private const int NoSpaceLeft = 28;

    public volatile bool Full;

    public int OpenFiles => inner.OpenFiles;
    public int MaximumOpenFiles => inner.MaximumOpenFiles;

    public ReusableTask WriteAsync(ITorrentManagerFile file, long offset, ReadOnlyMemory<byte> buffer) =>
        Full ? throw new IOException("There is not enough space on the disk.", NoSpaceLeft) : inner.WriteAsync(file, offset, buffer);

    public ReusableTask<int> ReadAsync(ITorrentManagerFile file, long offset, Memory<byte> buffer) => inner.ReadAsync(file, offset, buffer);
    public ReusableTask CloseAsync(ITorrentManagerFile file) => inner.CloseAsync(file);
    public ReusableTask<bool> ExistsAsync(ITorrentManagerFile file) => inner.ExistsAsync(file);
    public ReusableTask FlushAsync(ITorrentManagerFile file) => inner.FlushAsync(file);
    public ReusableTask MoveAsync(ITorrentManagerFile file, string fullPath, bool overwrite) => inner.MoveAsync(file, fullPath, overwrite);
    public ReusableTask SetMaximumOpenFilesAsync(int maximumOpenFiles) => inner.SetMaximumOpenFilesAsync(maximumOpenFiles);
    public void Dispose() => inner.Dispose();
}
