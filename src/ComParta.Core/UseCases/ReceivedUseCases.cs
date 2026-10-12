using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <summary>
/// What a person does with a file that has arrived: open it, show it in its folder, or delete it
/// (epic 8 story 5). Every action works only on files inside the Received folder; the path comes
/// from the transfer's own record, never from the UI.
/// </summary>
public sealed class ReceivedUseCases(TransferUseCases transfers, IAppPaths paths, IContentOpener opener, IFileSystem files)
{
    // Opening these would run a program. ComParta never does that; the person can still find the
    // file with Show in folder, where Windows asks before running anything (AD-6).
    private static readonly HashSet<string> Programs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".msix", ".appx", ".bat", ".cmd", ".com", ".scr", ".pif", ".cpl", ".ps1", ".psm1",
        ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".lnk", ".url", ".reg", ".jar", ".dll", ".sys",
    };

    public async Task<Result> OpenAsync(TransferId id, CancellationToken ct = default)
    {
        var (path, error) = Find(id, mustExist: true);
        if (error is { } kind)
            return Result.Fail(kind);
        if (Programs.Contains(Path.GetExtension(path!)))
            return Result.Fail(ErrorKind.ReceivedOpenFailed);
        return await opener.OpenAsync(path!, ct) ? Result.Success : Result.Fail(ErrorKind.ReceivedOpenFailed);
    }

    public async Task<Result> RevealAsync(TransferId id, CancellationToken ct = default)
    {
        var (path, error) = Find(id, mustExist: true);
        if (error is { } kind)
            return Result.Fail(kind);
        await opener.RevealAsync(path!, ct);
        return Result.Success;
    }

    /// <summary>Deletes the file and takes it off the list. A file already gone is simply taken off.</summary>
    public async Task<Result> DeleteAsync(TransferId id, CancellationToken ct = default)
    {
        var (path, error) = Find(id, mustExist: false);
        if (error is { } kind)
            return Result.Fail(kind);
        try { files.DeleteFile(path!); }
        catch (IOException) { return Result.Fail(ErrorKind.FileUnreadable); }
        catch (UnauthorizedAccessException) { return Result.Fail(ErrorKind.FileUnreadable); }
        return await transfers.RemoveAsync(id, ct);
    }

    /// <summary>The finished file's place under the Received folder, or why there is none.</summary>
    internal (string? Path, ErrorKind? Error) Find(TransferId id, bool mustExist)
    {
        var view = transfers.List().FirstOrDefault(v => v.Transfer.Id == id);
        if (view is null)
            return (null, ErrorKind.TransferNotFound);
        if (view.State != TransferState.Done || string.IsNullOrEmpty(view.Transfer.Name))
            return (null, ErrorKind.ReceivedNotFound);

        var root = Path.GetFullPath(paths.ReceivedRoot);
        var path = Path.GetFullPath(Path.Combine(root, view.Transfer.Name));
        var inside = path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!inside)
            return (null, ErrorKind.ReceivedNotFound);
        if (mustExist && !files.FileExists(path))
            return (null, ErrorKind.ReceivedNotFound);
        return (path, null);
    }
}
