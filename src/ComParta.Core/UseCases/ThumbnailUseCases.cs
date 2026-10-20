using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <summary>
/// Thumbnails for the photos and videos a person shares or has received (epic 8 story 4). The
/// path comes from the item's own record, never from the UI, and other kinds of file get none.
/// </summary>
public sealed class ThumbnailUseCases(ShareUseCases shares, ReceivedUseCases received, IThumbnailProvider provider)
{
    /// <summary>The sizes the UI may ask for; anything else is brought within them.</summary>
    public const int SmallestSize = 32;
    public const int LargestSize = 512;

    /// <summary>A bigger picture than this is not sent to the UI.</summary>
    public const int MaxBytes = 512 * 1024;

    private static readonly HashSet<string> Pictures = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".heic", ".heif", ".webp", ".bmp", ".tif", ".tiff",
        ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".m4v", ".webm", ".3gp", ".mpg", ".mpeg",
    };

    /// <summary>How many of a folder's photos and videos its card shows (UX design, Items).</summary>
    public const int FolderPictures = 4;

    /// <param name="item">For a folder or several files, which of its photos and videos, counted from 0; a file has only 0.</param>
    public async Task<Result<Thumbnail?>> ForShareAsync(ShareId id, int size, int item = 0, CancellationToken ct = default)
    {
        var view = shares.Current().FirstOrDefault(v => v.Share.Id == id);
        if (view is null)
            return Result<Thumbnail?>.Fail(ErrorKind.ShareNotFound);
        var share = view.Share;
        var path = share.Kind == ShareKind.File
            ? (item == 0 ? share.FilePath : null)
            : item is >= 0 and < FolderPictures
                ? share.Items.Where(i => Pictures.Contains(Path.GetExtension(i.RelativePath))).Skip(item).FirstOrDefault()?.ContentRef
                : null;
        return Result<Thumbnail?>.Success(path is null ? null : await DrawAsync(path, size, ct));
    }

    /// <summary>Only a file that has arrived has a picture; one still on its way gets none.</summary>
    public async Task<Result<Thumbnail?>> ForTransferAsync(TransferId id, int size, CancellationToken ct = default)
    {
        var (path, error) = received.Find(id, mustExist: true);
        if (error == ErrorKind.TransferNotFound)
            return Result<Thumbnail?>.Fail(ErrorKind.TransferNotFound);
        return Result<Thumbnail?>.Success(path is null ? null : await DrawAsync(path, size, ct));
    }

    private async Task<Thumbnail?> DrawAsync(string path, int size, CancellationToken ct)
    {
        if (!Pictures.Contains(Path.GetExtension(path)))
            return null;
        try
        {
            var thumbnail = await provider.GetAsync(path, Math.Clamp(size, SmallestSize, LargestSize), ct);
            return thumbnail is { Bytes.Length: > 0 and <= MaxBytes } && thumbnail.ContentType.StartsWith("image/", StringComparison.Ordinal)
                ? thumbnail
                : null;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // The file icon is shown instead; a picture is never worth an error message.
            return null;
        }
    }
}
