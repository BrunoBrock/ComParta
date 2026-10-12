using MonoTorrent;
using MonoTorrent.PieceWriter;
using ReusableTasks;

namespace ComParta.Engine.MonoTorrent.Tests;

/// <summary>
/// The engine's own disk writer, except that writes can fail as on a full disk while <see cref="Full"/>
/// is set, and can be slowed by <see cref="Delay"/> so a transfer can be watched half way.
/// </summary>
internal sealed class TestDiskWriter(IPieceWriter inner) : IPieceWriter
{
    // ENOSPC, as .NET reports it outside Windows; the engine also knows the Windows codes.
    private const int NoSpaceLeft = 28;

    // Writes come in parallel; one at a time is what makes the delay add up.
    private readonly SemaphoreSlim _one = new(1, 1);

    public volatile bool Full;
    public TimeSpan Delay { get; init; }

    public int OpenFiles => inner.OpenFiles;
    public int MaximumOpenFiles => inner.MaximumOpenFiles;

    public async ReusableTask WriteAsync(ITorrentManagerFile file, long offset, ReadOnlyMemory<byte> buffer)
    {
        if (Full)
            throw new IOException("There is not enough space on the disk.", NoSpaceLeft);
        if (Delay <= TimeSpan.Zero)
        {
            await inner.WriteAsync(file, offset, buffer);
            return;
        }
        await _one.WaitAsync();
        try
        {
            await Task.Delay(Delay);
            await inner.WriteAsync(file, offset, buffer);
        }
        finally { _one.Release(); }
    }

    public ReusableTask<int> ReadAsync(ITorrentManagerFile file, long offset, Memory<byte> buffer) => inner.ReadAsync(file, offset, buffer);
    public ReusableTask CloseAsync(ITorrentManagerFile file) => inner.CloseAsync(file);
    public ReusableTask<bool> ExistsAsync(ITorrentManagerFile file) => inner.ExistsAsync(file);
    public ReusableTask FlushAsync(ITorrentManagerFile file) => inner.FlushAsync(file);
    public ReusableTask MoveAsync(ITorrentManagerFile file, string fullPath, bool overwrite) => inner.MoveAsync(file, fullPath, overwrite);
    public ReusableTask SetMaximumOpenFilesAsync(int maximumOpenFiles) => inner.SetMaximumOpenFilesAsync(maximumOpenFiles);
    public void Dispose() => inner.Dispose();
}
