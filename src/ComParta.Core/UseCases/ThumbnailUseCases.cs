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

    public async Task<Result<Thumbnail?>> ForShareAsync(ShareId id, int size, CancellationToken ct = default)
    {
        var view = shares.Current().FirstOrDefault(v => v.Share.Id == id);
        if (view is null)
            return Result<Thumbnail?>.Fail(ErrorKind.ShareNotFound);
        return Result<Thumbnail?>.Success(await DrawAsync(view.Share.FilePath, size, ct));
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
