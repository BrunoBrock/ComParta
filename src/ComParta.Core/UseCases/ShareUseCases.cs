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
                var available = _files.FileExists(share.FilePath);
                Set(share, available ? ShareStatus.Preparing : ShareStatus.Unavailable);
                if (available)
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
                var exists = _files.FileExists(view.Share.FilePath);
                if (!exists && view.Status != ShareStatus.Unavailable)
                {
                    Set(view.Share, ShareStatus.Unavailable);
                    await _engine.StopOfferingAsync(view.Share.Id, ct);
                }
                else if (exists && view.Status == ShareStatus.Unavailable)
                {
                    Set(view.Share, ShareStatus.Preparing);
                    await OfferAsync(view.Share, ct);
                }
            }
            return Snapshot();
        }
        finally { _gate.Release(); }
    }

    /// <summary>Opens the file dialog and shares the chosen file. The value is null when the person cancels.</summary>
    public async Task<Result<ShareView?>> AddAsync(CancellationToken ct = default)
    {
        var path = await _picker.PickFileAsync(ct);
        if (path is null)
            return Result<ShareView?>.Success(null);
        return await ShareFileAsync(path, ct);
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
            var result = await ShareFileAsync(path, ct);
            if (result.Ok)
                added++;
            else
                problem ??= result.Error;
        }
        return (added, problem);
    }

    private async Task<Result<ShareView?>> ShareFileAsync(string path, CancellationToken ct)
    {
        ShareView view;
        await _gate.WaitAsync(ct);
        try
        {
            path = Path.GetFullPath(path);
            if (Snapshot().Any(v =>string.Equals(v.Share.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                return Result<ShareView?>.Fail(ErrorKind.ShareDuplicate);

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

            var share = new Share(ShareId.New(), path, Path.GetFileName(path), size, _clock.GetUtcNow());
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
