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
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<ShareId, ShareView> _shares = [];

    public ShareUseCases(IShareRepository repository, ITransferEngine engine, IFilePicker picker, IFileSystem files, TimeProvider? clock = null)
    {
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
                    await _engine.OfferAsync(share.Id, share.FilePath, ct);
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
                    await _engine.OfferAsync(view.Share.Id, view.Share.FilePath, ct);
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
            await _engine.StopOfferingAsync(id, ct);
            await _repository.RemoveAsync(id, ct);
        }
        finally { _gate.Release(); }

        Changed?.Invoke();
        return Result.Success;
    }

    /// <summary>
    /// The ComParta link another person opens to receive this share (AD-16). It carries the name
    /// and size so they can see what it is before they agree.
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
        if (invite is null)
            return Result<string>.Fail(ErrorKind.ShareNotReady);
        var hints = new InviteHints(SenderName: null, view.Share.Name, view.Share.SizeBytes, ItemCount: 1);
        return Result<string>.Success(InviteCodec.Write(invite, hints));
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
