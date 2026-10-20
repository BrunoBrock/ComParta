using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <summary>Add, list and remove the files a person offers, and give out invites for them.</summary>
public sealed class ShareUseCases
{
    private readonly IShareRepository _repository;
    private readonly ITransferEngine _engine;
    private readonly IFilePicker _picker;
    private readonly IFileSystem _files;
    private readonly TimeProvider _clock;
    private readonly IAppPaths? _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<ShareId, ShareView> _shares = [];
    // Several files chosen together, waiting for the name the person gives them.
    private IReadOnlyList<string>? _picked;

    /// <summary>The most files one share holds; the other computer refuses more (AD-19).</summary>
    public const int MaxItems = 10_000;
    /// <summary>The longest name for several files shared together; it becomes a folder's name on the other computer.</summary>
    public const int MaxNameLength = 100;

    public ShareUseCases(IShareRepository repository, ITransferEngine engine, IFilePicker picker, IFileSystem files, TimeProvider? clock = null, IAppPaths? paths = null)
    {
        _paths = paths;
        _repository = repository;
        _engine = engine;
        _picker = picker;
        _files = files;
        _clock = clock ?? TimeProvider.System;
        _engine.OfferChanged += OnOfferChanged;
    }

    /// <summary>Raised whenever the list or a status changes.</summary>
    public event Action? Changed;

    /// <summary>Loads the saved shares and starts offering those whose file is still there.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            foreach (var share in await _repository.GetAllAsync(ct))
            {
                var missing = Missing(share);
                Set(share, missing ?? ShareStatus.Preparing);
                if (missing is null)
                    await OfferAsync(share, ct);
            }
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    /// <summary>The current shares, after checking that each file is still where it was.</summary>
    public async Task<IReadOnlyList<ShareView>> ListAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            foreach (var view in Snapshot())
            {
                var missing = Missing(view.Share);
                var wasMissing = view.Status is ShareStatus.Unavailable or ShareStatus.PartlyUnavailable;
                if (missing is { } status && status != view.Status)
                {
                    Set(view.Share, status);
                    if (!wasMissing)
                        await _engine.StopOfferingAsync(view.Share.Id, ct);
                }
                else if (missing is null && wasMissing)
                {
                    Set(view.Share, ShareStatus.Preparing);
                    await OfferAsync(view.Share, ct);
                }
            }
            return Snapshot();
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Opens the file dialog. One file chosen is shared at once; several wait for a name
    /// (<see cref="AddPickedAsync"/>). Nothing is shared when the person cancels.
    /// </summary>
    public async Task<Result<AddedShare>> AddAsync(CancellationToken ct = default)
    {
        var paths = await _picker.PickFilesAsync(ct);
        _picked = null;
        if (paths.Count == 0)
            return Result<AddedShare>.Success(new AddedShare(null));
        if (paths.Count == 1)
        {
            var one = await ShareFileAsync(paths[0], ct);
            return one.Ok ? Result<AddedShare>.Success(new AddedShare(one.Value)) : Result<AddedShare>.Fail(one.Error!.Value);
        }
        if (paths.Count > MaxItems)
            return Result<AddedShare>.Fail(ErrorKind.FolderTooLarge);
        _picked = paths;
        return Result<AddedShare>.Success(new AddedShare(null, paths.Count));
    }

    /// <summary>
    /// Shares the files chosen together under the name the person gave, or forgets them when the
    /// name is null (the person cancelled). The value is null when nothing was waiting.
    /// </summary>
    public async Task<Result<ShareView?>> AddPickedAsync(string? name, CancellationToken ct = default)
    {
        var picked = _picked;
        if (name is null || picked is null)
        {
            _picked = null;
            return Result<ShareView?>.Success(null);
        }
        name = name.Trim();
        if (name.Length == 0 || name.Length > MaxNameLength || name.EndsWith('.') || name.IndexOfAny(NotInNames) >= 0)
            return Result<ShareView?>.Fail(ErrorKind.ShareNameInvalid);
        _picked = null;

        // The file dialog chooses from one folder; files from elsewhere are refused as unreadable.
        var full = picked.Select(Path.GetFullPath).ToList();
        var root = Path.GetDirectoryName(full[0])!;
        return await AddItemsAsync(root, name, ShareKind.Collection, [.. full.Select(path => Path.GetRelativePath(root, path))], ct);
    }

    private static readonly char[] NotInNames = ['<', '>', ':', '"', '/', '\\', '|', '?', '*', .. Enumerable.Range(0, 32).Select(c => (char)c)];

    /// <summary>Opens the folder dialog and shares the folder chosen, with every file in it. Null when the person cancels.</summary>
    public async Task<Result<ShareView?>> AddFolderAsync(CancellationToken ct = default)
    {
        var folder = await _picker.PickFolderAsync(ct);
        return folder is null ? Result<ShareView?>.Success(null) : await ShareFolderAsync(folder, ct);
    }

    private async Task<Result<ShareView?>> ShareFolderAsync(string folder, CancellationToken ct)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        IReadOnlyList<string> files;
        try { files = _files.ListFiles(folder); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Result<ShareView?>.Fail(ErrorKind.FileUnreadable);
        }
        if (files.Count == 0)
            return Result<ShareView?>.Fail(ErrorKind.FolderEmpty);
        if (files.Count > MaxItems)
            return Result<ShareView?>.Fail(ErrorKind.FolderTooLarge);
        var name = Path.GetFileName(folder);
        return await AddItemsAsync(folder, string.IsNullOrEmpty(name) ? folder : name, ShareKind.Folder, files, ct);
    }

    /// <summary>
    /// Shares each file dropped onto the window, as if picked one by one (epic 8 story 6). Returns
    /// how many were shared and why the first one that could not be was refused.
    /// </summary>
    public async Task<(int Added, ErrorKind? Problem)> AddDroppedAsync(IEnumerable<string> paths, CancellationToken ct = default)
    {
        var added = 0;
        ErrorKind? problem = null;
        foreach (var path in paths)
        {
            // A dropped folder is shared whole, as if chosen in the folder dialog.
            var result = _files.FolderExists(path) ? await ShareFolderAsync(path, ct) : await ShareFileAsync(path, ct);
            if (result.Ok)
                added++;
            else
                problem ??= result.Error;
        }
        return (added, problem);
    }

    private Task<Result<ShareView?>> ShareFileAsync(string path, CancellationToken ct)
    {
        path = Path.GetFullPath(path);
        return AddItemsAsync(path, Path.GetFileName(path), ShareKind.File, [Path.GetFileName(path)], ct);
    }

    /// <summary>
    /// Shares a set of files as one (AD-19): each relative path is read from under
    /// <paramref name="root"/>, the file itself for a single file. Every file must be readable and
    /// not empty, and no path may leave the root.
    /// </summary>
    public async Task<Result<ShareView?>> AddItemsAsync(
        string root, string name, ShareKind kind, IReadOnlyList<string> relativePaths, CancellationToken ct = default)
    {
        ShareView view;
        await _gate.WaitAsync(ct);
        try
        {
            root = Path.GetFullPath(root);
            if (relativePaths.Count == 0 || string.IsNullOrWhiteSpace(name))
                return Result<ShareView?>.Fail(ErrorKind.FileUnreadable);
            // The same file or folder shared twice; several files from one folder may be shared in many ways.
            if (kind != ShareKind.Collection && Snapshot().Any(v =>
                    v.Share.Kind == kind && string.Equals(v.Share.FilePath, root, StringComparison.OrdinalIgnoreCase)))
                return Result<ShareView?>.Fail(ErrorKind.ShareDuplicate);

            var items = new List<ShareItem>();
            foreach (var relative in relativePaths)
            {
                var path = kind == ShareKind.File ? root : Path.GetFullPath(Path.Combine(root, relative));
                if (kind != ShareKind.File && !path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return Result<ShareView?>.Fail(ErrorKind.FileUnreadable);
                long size;
                try
                {
                    if (!_files.FileExists(path))
                        return Result<ShareView?>.Fail(ErrorKind.FileUnreadable);
                    size = _files.GetReadableFileSize(path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    return Result<ShareView?>.Fail(ErrorKind.FileUnreadable);
                }
                if (size == 0)
                    return Result<ShareView?>.Fail(ErrorKind.FileUnreadable);
                items.Add(new ShareItem(relative.Replace('\\', '/'), size, path));
            }

            var share = new Share(ShareId.New(), root, name, items.Sum(i => i.SizeBytes), _clock.GetUtcNow(), Items: items, Kind: kind);
            await _repository.AddAsync(share, ct);
            view = Set(share, ShareStatus.Preparing);
            await _engine.OfferAsync(share.Id, share.FilePath, ct);
        }
        finally { _gate.Release(); }

        Changed?.Invoke();
        return Result<ShareView?>.Success(view);
    }

    /// <summary>Stops offering and forgets the share. The original file is never touched (AD-6).</summary>
    public async Task<Result> RemoveAsync(ShareId id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            lock (_shares)
            {
                if (!_shares.Remove(id))
                    return Result.Fail(ErrorKind.ShareNotFound);
            }
            // Gone from the screen at once; the engine lets go of the file behind it.
            Changed?.Invoke();
            await _repository.RemoveAsync(id, ct);
            await _engine.StopOfferingAsync(id, ct);
        }
        finally { _gate.Release(); }
        return Result.Success;
    }

    /// <summary>Stops sending one file, also after a restart, until it is continued (epic 15).</summary>
    public Task<Result> PauseAsync(ShareId id, CancellationToken ct = default) => SetPausedAsync(id, true, ct);

    /// <summary>Sends a paused file again.</summary>
    public Task<Result> ContinueAsync(ShareId id, CancellationToken ct = default) => SetPausedAsync(id, false, ct);

    private async Task<Result> SetPausedAsync(ShareId id, bool paused, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ShareView? view;
            lock (_shares)
            {
                if (!_shares.TryGetValue(id, out view))
                    return Result.Fail(ErrorKind.ShareNotFound);
                if (view.Share.Paused == paused)
                    return Result.Success;
                // Shown at once; the engine catches up behind it.
                _shares[id] = view with { Share = view.Share with { Paused = paused } };
            }
            await _repository.SetPausedAsync(id, paused, ct);
            Changed?.Invoke();
            if (paused)
                await _engine.PauseOfferingAsync(id, ct);
            else
                await _engine.ContinueOfferingAsync(id, ct);
        }
        finally { _gate.Release(); }
        return Result.Success;
    }

    /// <summary>Offers a share, still paused when the person paused it.</summary>
    private async Task OfferAsync(Share share, CancellationToken ct)
    {
        await _engine.OfferAsync(share.Id, share.FilePath, ct);
        if (share.Paused)
            await _engine.PauseOfferingAsync(share.Id, ct);
    }

    /// <summary>
    /// The link another person opens to receive this share: the engine's own link, as it is, so
    /// that other apps of its kind open it too (AD-16, amended). The engine puts the name and size
    /// in it so the person can see what it is before they agree.
    /// </summary>
    public async Task<Result<string>> GetInviteAsync(ShareId id, CancellationToken ct = default)
    {
        ShareView? view;
        lock (_shares)
            _shares.TryGetValue(id, out view);
        if (view is null)
            return Result<string>.Fail(ErrorKind.ShareNotFound);
        if (view.Status != ShareStatus.Ready)
            return Result<string>.Fail(ErrorKind.ShareNotReady);

        var invite = await _engine.GetInviteAsync(id, ct);
        return invite is null ? Result<string>.Fail(ErrorKind.ShareNotReady) : Result<string>.Success(invite);
    }

    /// <summary>
    /// Saves a ready share's download file in the person's Downloads folder, for apps that take
    /// a file instead of a link, and returns the full path it was saved at. A file already there with
    /// that name is never replaced: the new one gets a number.
    /// </summary>
    public async Task<Result<string>> SaveDownloadFileAsync(ShareId id, CancellationToken ct = default)
    {
        ShareView? view;
        lock (_shares)
            _shares.TryGetValue(id, out view);
        if (view is null)
            return Result<string>.Fail(ErrorKind.ShareNotFound);
        if (view.Status != ShareStatus.Ready)
            return Result<string>.Fail(ErrorKind.ShareNotReady);
        if (_paths is null)
            return Result<string>.Fail(ErrorKind.SaveFailed);

        var file = await _engine.GetDownloadFileAsync(id, ct);
        if (file is null)
            return Result<string>.Fail(ErrorKind.ShareNotReady);

        var name = Path.GetFileNameWithoutExtension(file.Name);
        var extension = Path.GetExtension(file.Name);
        for (var copy = 1; copy <= 100; copy++)
        {
            var path = Path.Combine(_paths.SavedRoot, copy == 1 ? file.Name : $"{name} ({copy}){extension}");
            if (_files.FileExists(path))
                continue;
            try
            {
                _files.WriteNewFile(path, file.Content);
                return Result<string>.Success(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Taken a moment ago by something else: the next number. Anything else fails below.
                if (!_files.FileExists(path))
                    return Result<string>.Fail(ErrorKind.SaveFailed);
            }
        }
        return Result<string>.Fail(ErrorKind.SaveFailed);
    }

    private void OnOfferChanged(ShareId id, OfferState state)
    {
        lock (_shares)
        {
            if (!_shares.TryGetValue(id, out var view))
                return;
            var status = state switch
            {
                OfferState.Ready => ShareStatus.Ready,
                OfferState.Failed => ShareStatus.Unavailable,
                _ => ShareStatus.Preparing,
            };
            if (view.Status == status)
                return;
            _shares[id] = view with { Status = status };
        }
        Changed?.Invoke();
    }

    /// <summary>Null when every file is there; else whether some or all of them are gone.</summary>
    private ShareStatus? Missing(Share share)
    {
        var gone = share.Items.Count(item => !_files.FileExists(item.ContentRef));
        return gone == 0 ? null : gone == share.Items.Count ? ShareStatus.Unavailable : ShareStatus.PartlyUnavailable;
    }

    private ShareView Set(Share share, ShareStatus status)
    {
        var view = new ShareView(share, status);
        lock (_shares)
            _shares[share.Id] = view;
        return view;
    }

    /// <summary>The shares as last known, without checking the files again.</summary>
    public IReadOnlyList<ShareView> Current() => Snapshot();

    private List<ShareView> Snapshot()
    {
        lock (_shares)
            return [.. _shares.Values.OrderBy(v => v.Share.AddedAt)];
    }
}
