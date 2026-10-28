using System.Collections.Concurrent;
using ComParta.Core.Ports;
using MonoTorrent;
using MonoTorrent.PieceWriter;
using ReusableTasks;

namespace ComParta.Engine.MonoTorrent;

/// <summary>
/// The engine's disk, except for shared files: those are read through the shell's content source,
/// wherever and whatever they are (a path, or a phone's picked item), and never written (AD-20).
/// </summary>
/// <param name="disk">Received files, written and read as usual.</param>
/// <param name="content">Reads the shared files.</param>
/// <param name="shared">Where the engine thinks each shared file is, and what it really is.</param>
internal sealed class ContentPieceWriter(IPieceWriter disk, IContentSource content, ConcurrentDictionary<string, string> shared) : IPieceWriter
{
    private readonly ConcurrentDictionary<string, OpenContent> _open = new(StringComparer.Ordinal);

    public int OpenFiles => disk.OpenFiles + _open.Count;
    public int MaximumOpenFiles => disk.MaximumOpenFiles;

    public async ReusableTask<int> ReadAsync(ITorrentManagerFile file, long offset, Memory<byte> buffer)
    {
        if (!shared.TryGetValue(file.FullPath, out var contentRef))
            return await disk.ReadAsync(file, offset, buffer);
        var open = _open.GetOrAdd(file.FullPath, _ => new OpenContent(contentRef));
        return await open.ReadAsync(content, offset, buffer);
    }

    public async ReusableTask WriteAsync(ITorrentManagerFile file, long offset, ReadOnlyMemory<byte> buffer)
    {
        if (shared.ContainsKey(file.FullPath))
            throw new IOException("A shared file is never written.");
        await disk.WriteAsync(file, offset, buffer);
    }

    public async ReusableTask<bool> ExistsAsync(ITorrentManagerFile file)
    {
        if (!shared.TryGetValue(file.FullPath, out var contentRef))
            return await disk.ExistsAsync(file);
        try { return content.Exists(contentRef); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    public async ReusableTask CloseAsync(ITorrentManagerFile file)
    {
        if (_open.TryRemove(file.FullPath, out var open))
            open.Dispose();
        if (!shared.ContainsKey(file.FullPath))
            await disk.CloseAsync(file);
    }

    public async ReusableTask FlushAsync(ITorrentManagerFile file)
    {
        if (!shared.ContainsKey(file.FullPath))
            await disk.FlushAsync(file);
    }

    public async ReusableTask MoveAsync(ITorrentManagerFile file, string fullPath, bool overwrite)
    {
        if (shared.ContainsKey(file.FullPath))
            throw new IOException("A shared file is never moved.");
        await disk.MoveAsync(file, fullPath, overwrite);
    }

    public ReusableTask SetMaximumOpenFilesAsync(int maximumOpenFiles) => disk.SetMaximumOpenFilesAsync(maximumOpenFiles);

    public void Dispose()
    {
        foreach (var open in _open.Values)
            open.Dispose();
        _open.Clear();
        disk.Dispose();
    }

    /// <summary>One shared file kept open while the engine reads it, one read at a time.</summary>
    private sealed class OpenContent(string contentRef) : IDisposable
    {
        private readonly SemaphoreSlim _one = new(1, 1);
        private Stream? _stream;

        public async Task<int> ReadAsync(IContentSource content, long offset, Memory<byte> buffer)
        {
            await _one.WaitAsync();
            try
            {
                _stream ??= content.OpenRead(contentRef);
                _stream.Position = offset;
                var read = 0;
                while (read < buffer.Length)
                {
                    var n = await _stream.ReadAsync(buffer[read..]);
                    if (n == 0)
                        break;
                    read += n;
                }
                return read;
            }
            finally { _one.Release(); }
        }

        public void Dispose()
        {
            // Waits for a read under way, so the stream is never closed beneath it.
            _one.Wait();
            try
            {
                _stream?.Dispose();
                _stream = null;
            }
            finally { _one.Release(); }
        }
    }
}
