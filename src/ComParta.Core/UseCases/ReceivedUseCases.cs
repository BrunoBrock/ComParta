using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <summary>
/// What a person does with a file that has arrived: open it, show it in its folder, or delete it
/// (epic 8 story 5). Every action works only on files inside the Received folder the transfer went
/// to; the path comes from the transfer's own record, never from the UI.
/// </summary>
/// <summary>A file inside a received folder: its place in the folder, with / between folders, and its size.</summary>
public sealed record ReceivedFile(string RelativePath, long SizeBytes);

public sealed class ReceivedUseCases(TransferUseCases transfers, IAppPaths paths, IContentOpener opener, IFileSystem files)
{
    /// <summary>
    /// Opens a received file in the program that goes with it. A file that can run something (see
    /// <see cref="ReceivedNames.IsRisky"/>) is refused until the person confirms a second time, so
    /// nothing risky opens by accident (AD-13). Opening never runs a file by itself: it is always
    /// the person's own request.
    /// </summary>
    public async Task<Result> OpenAsync(TransferId id, bool confirmed = false, CancellationToken ct = default)
    {
        var (path, error) = Find(id, mustExist: true);
        if (error is { } kind)
            return Result.Fail(kind);
        if (!confirmed && ReceivedNames.IsRisky(Path.GetFileName(path!)))
            return Result.Fail(ErrorKind.ReceivedConfirmRisky);
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

    /// <summary>Whether a finished item is a folder that arrived, rather than one file.</summary>
    public bool IsFolder(TransferView view)
    {
        if (view.State != TransferState.Done || string.IsNullOrEmpty(view.Transfer.Name))
            return false;
        var (path, error) = Find(view.Transfer.Id, mustExist: false);
        return error is null && files.FolderExists(path!);
    }

    /// <summary>The files inside a received folder, with their place in it, or why there are none.</summary>
    public Result<IReadOnlyList<ReceivedFile>> ListFiles(TransferId id)
    {
        var (path, error) = Find(id, mustExist: false);
        if (error is { } kind)
            return Result<IReadOnlyList<ReceivedFile>>.Fail(kind);
        if (!files.FolderExists(path!))
            return Result<IReadOnlyList<ReceivedFile>>.Fail(ErrorKind.ReceivedNotFound);

        var listed = new List<ReceivedFile>();
        foreach (var relative in files.ListFiles(path!))
        {
            try
            {
                listed.Add(new ReceivedFile(relative.Replace(Path.DirectorySeparatorChar, '/'), files.GetReadableFileSize(Path.Combine(path!, relative))));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return Result<IReadOnlyList<ReceivedFile>>.Success(listed);
    }

    /// <summary>The finished file's place under its Received folder, or why there is none.</summary>
    internal (string? Path, ErrorKind? Error) Find(TransferId id, bool mustExist)
    {
        var view = transfers.List().FirstOrDefault(v => v.Transfer.Id == id);
        if (view is null)
            return (null, ErrorKind.TransferNotFound);
        if (view.State != TransferState.Done || string.IsNullOrEmpty(view.Transfer.Name))
            return (null, ErrorKind.ReceivedNotFound);

        // Each transfer is where it started, even after the person chose another folder (epic 12 story 2).
        var root = Path.GetFullPath(view.Transfer.Folder ?? paths.ReceivedRoot);
        var path = Path.GetFullPath(Path.Combine(root, view.Transfer.Name));
        if (!ReceivedNames.IsInside(root, path))
            return (null, ErrorKind.ReceivedNotFound);
        if (mustExist && !files.FileExists(path))
            return (null, ErrorKind.ReceivedNotFound);
        return (path, null);
    }
}
