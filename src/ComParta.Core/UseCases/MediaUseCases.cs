using System.Globalization;
using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

public enum MediaSource { Share, Received }

public enum MediaKind { Picture, Song, Video }

/// <param name="Token">Names this one item in the web view's address; it opens nothing else.</param>
public sealed record MediaItem(string Token, MediaKind Kind, string Name);

/// <summary>
/// The answer to one request from the web view: a status as on the web (200, 206, 404, 416), and
/// the bytes asked for. The receiver disposes <see cref="Body"/>.
/// </summary>
/// <param name="Length">How many bytes <see cref="Body"/> holds.</param>
/// <param name="ContentRange">The part of the file sent, as the web's Content-Range header says it.</param>
public sealed record MediaReply(int Status, string? ContentType, long Length, string? ContentRange, Stream? Body);

/// <summary>
/// Pictures, songs and videos opened inside ComParta (epic 18, AD-26). The UI asks for an item by
/// id; core checks it is a shared or received file whose name and first bytes both say it is a kind
/// the system web view plays, and hands back a token that serves that file and nothing else. The
/// web view then reads it in parts, as a player seeks. Programs and anything unknown are refused.
/// </summary>
public sealed class MediaUseCases(ShareUseCases shares, ReceivedUseCases received, IFileSystem files)
{
    /// <summary>Items opened longer ago than the last this many stop being served.</summary>
    public const int KeptOpen = 16;

    private sealed record Format(string ContentType, MediaKind Kind, Func<byte[], bool> Looks);

    private static readonly Format Jpeg = new("image/jpeg", MediaKind.Picture, h => Starts(h, 0, 0xFF, 0xD8, 0xFF));
    private static readonly Format Mp4Video = new("video/mp4", MediaKind.Video, IsIsoMedia);

    private static readonly Dictionary<string, Format> Formats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = Jpeg,
        [".jpeg"] = Jpeg,
        [".png"] = new("image/png", MediaKind.Picture, h => Starts(h, 0, 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A)),
        [".gif"] = new("image/gif", MediaKind.Picture, h => Starts(h, 0, "GIF8"u8)),
        [".webp"] = new("image/webp", MediaKind.Picture, h => Starts(h, 0, "RIFF"u8) && Starts(h, 8, "WEBP"u8)),
        [".mp3"] = new("audio/mpeg", MediaKind.Song, h => HasTags(h) || IsMpegAudio(h)),
        [".aac"] = new("audio/aac", MediaKind.Song, h => HasTags(h) || (h.Length > 1 && h[0] == 0xFF && (h[1] & 0xF6) == 0xF0)),
        [".m4a"] = new("audio/mp4", MediaKind.Song, IsIsoMedia),
        [".wav"] = new("audio/wav", MediaKind.Song, h => Starts(h, 0, "RIFF"u8) && Starts(h, 8, "WAVE"u8)),
        [".flac"] = new("audio/flac", MediaKind.Song, h => HasTags(h) || Starts(h, 0, "fLaC"u8)),
        [".ogg"] = new("audio/ogg", MediaKind.Song, h => Starts(h, 0, "OggS"u8)),
        [".oga"] = new("audio/ogg", MediaKind.Song, h => Starts(h, 0, "OggS"u8)),
        [".mp4"] = Mp4Video,
        [".m4v"] = Mp4Video,
        [".webm"] = new("video/webm", MediaKind.Video, h => Starts(h, 0, 0x1A, 0x45, 0xDF, 0xA3)),
    };

    private const int HeadBytes = 16;

    private sealed record Opened(string Token, string Path, string ContentType);

    private readonly LinkedList<Opened> _open = new();

    /// <summary>A token for the item when it can be shown or played here; <see cref="ErrorKind.NotPlayable"/> otherwise.</summary>
    public Result<MediaItem> Open(MediaSource source, string? id)
    {
        string path, name;
        if (source == MediaSource.Share)
        {
            var view = ShareId.TryParse(id, out var shareId) ? shares.Current().FirstOrDefault(v => v.Share.Id == shareId) : null;
            if (view is null)
                return Result<MediaItem>.Fail(ErrorKind.ShareNotFound);
            (path, name) = (view.Share.FilePath, view.Share.Name);
        }
        else
        {
            if (!TransferId.TryParse(id, out var transferId))
                return Result<MediaItem>.Fail(ErrorKind.TransferNotFound);
            var (found, error) = received.Find(transferId, mustExist: true);
            if (error is { } kind)
                return Result<MediaItem>.Fail(kind);
            (path, name) = (found!, Path.GetFileName(found!));
        }

        if (!Formats.TryGetValue(Path.GetExtension(KindName(path)), out var format) || !files.FileExists(path) || !format.Looks(ReadHead(path)))
            return Result<MediaItem>.Fail(ErrorKind.NotPlayable);

        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        lock (_open)
        {
            _open.AddFirst(new Opened(token, path, format.ContentType));
            while (_open.Count > KeptOpen)
                _open.RemoveLast();
        }
        return Result<MediaItem>.Success(new MediaItem(token, format.Kind, name));
    }

    /// <summary>
    /// The bytes of an opened item, whole or the part a web Range header asks for
    /// (<c>bytes=start-end</c>, <c>bytes=start-</c> or <c>bytes=-last</c>). A token that was never
    /// given out, or a file that has gone, gets 404.
    /// </summary>
    public MediaReply Serve(string? token, string? range)
    {
        Opened? opened;
        lock (_open)
            opened = _open.FirstOrDefault(item => item.Token == token);
        if (opened is null)
            return NotFound;

        Stream stream;
        try { stream = files.OpenRead(opened.Path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return NotFound; }

        var size = stream.Length;
        var part = ReadRange(range, size);
        if (part is null)
            return new MediaReply(200, opened.ContentType, size, null, stream);
        var (start, end) = part.Value;
        if (start > end)
        {
            stream.Dispose();
            return new MediaReply(416, null, 0, $"bytes */{size}", null);
        }
        var length = end - start + 1;
        return new MediaReply(206, opened.ContentType, length, $"bytes {start}-{end}/{size}", new SliceStream(stream, start, length));
    }

    private static readonly MediaReply NotFound = new(404, null, 0, null, null);

    /// <summary>Null for the whole file; an empty range (start after end) when nothing asked for is in it.</summary>
    private static (long Start, long End)? ReadRange(string? range, long size)
    {
        // Anything other than one range in bytes is answered with the whole file, as the web allows.
        if (string.IsNullOrWhiteSpace(range) || !range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || range.Contains(','))
            return null;
        var spec = range["bytes=".Length..].Trim();
        var dash = spec.IndexOf('-');
        if (dash < 0)
            return null;
        var first = spec[..dash].Trim();
        var last = spec[(dash + 1)..].Trim();
        const NumberStyles digits = NumberStyles.None;

        if (first.Length == 0)
        {
            if (!long.TryParse(last, digits, CultureInfo.InvariantCulture, out var suffix))
                return null;
            return suffix == 0 || size == 0 ? (1, 0) : (Math.Max(0, size - suffix), size - 1);
        }
        if (!long.TryParse(first, digits, CultureInfo.InvariantCulture, out var start))
            return null;
        var end = size - 1;
        if (last.Length > 0)
        {
            if (!long.TryParse(last, digits, CultureInfo.InvariantCulture, out var asked) || asked < start)
                return null;
            end = Math.Min(asked, size - 1);
        }
        return start >= size ? (1, 0) : (start, end);
    }

    /// <summary>
    /// The name that tells the file's kind: a path ends in it, but an item picked on a phone has no
    /// path (AD-20), so the shell is asked the name it knows the item by. Empty when it cannot tell.
    /// </summary>
    private string KindName(string path)
    {
        try
        {
            return files.NameOf(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private byte[] ReadHead(string path)
    {
        try
        {
            using var stream = files.OpenRead(path);
            var head = new byte[HeadBytes];
            var read = stream.ReadAtLeast(head, HeadBytes, throwOnEndOfStream: false);
            return head[..read];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool Starts(byte[] head, int at, params ReadOnlySpan<byte> expected) =>
        head.Length >= at + expected.Length && head.AsSpan(at, expected.Length).SequenceEqual(expected);

    /// <summary>MP4 and M4A open with a box naming their brand.</summary>
    private static bool IsIsoMedia(byte[] head) => Starts(head, 4, "ftyp"u8);

    /// <summary>Songs often start with their title and artist before the sound itself.</summary>
    private static bool HasTags(byte[] head) => Starts(head, 0, "ID3"u8);

    private static bool IsMpegAudio(byte[] head) => head.Length > 1 && head[0] == 0xFF && (head[1] & 0xE0) == 0xE0;

    /// <summary>One part of a file, which a player may move around in.</summary>
    private sealed class SliceStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _start;
        private readonly long _length;
        private long _position;

        public SliceStream(Stream inner, long start, long length)
        {
            _inner = inner;
            _start = start;
            _length = length;
            _inner.Position = start;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var left = _length - _position;
            if (left <= 0)
                return 0;
            var read = _inner.Read(buffer[..(int)Math.Min(buffer.Length, left)]);
            _position += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var left = _length - _position;
            if (left <= 0)
                return 0;
            var read = await _inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, left)], cancellationToken);
            _position += read;
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override long Seek(long offset, SeekOrigin origin)
        {
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                _ => _length + offset,
            };
            if (target < 0)
                throw new IOException("Cannot move before the start.");
            _position = target;
            _inner.Position = _start + Math.Min(target, _length);
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
