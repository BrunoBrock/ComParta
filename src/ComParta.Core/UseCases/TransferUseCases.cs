using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <summary>
/// Receive a file from an invite, a download link or a download file, and follow how it is going.
/// </summary>
public sealed class TransferUseCases
{
    /// <summary>Download files are small descriptions; anything larger is not one.</summary>
    public const int MaxDownloadFileBytes = 16 * 1024 * 1024;

    private readonly ITransferRepository _repository;
    private readonly ITransferEngine _engine;
    private readonly IFilePicker? _picker;
    private readonly IFileSystem? _files;
    private readonly IStorageInfo? _storage;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<TransferId, TransferView> _transfers = [];

    public TransferUseCases(
        ITransferRepository repository,
        ITransferEngine engine,
        TimeProvider? clock = null,
        IFilePicker? picker = null,
        IFileSystem? files = null,
        IStorageInfo? storage = null)
    {
        _repository = repository;
        _engine = engine;
        _picker = picker;
        _files = files;
        _storage = storage;
        _clock = clock ?? TimeProvider.System;
        _engine.ReceiveProgressed += OnReceiveProgressed;
    }

    /// <summary>Raised whenever the list or a transfer's progress changes.</summary>
    public event Action? Changed;

    /// <summary>Loads the saved transfers and continues the unfinished ones.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            foreach (var transfer in await _repository.GetAllAsync(ct))
            {
                if (transfer.Done)
                {
                    Set(new TransferView(transfer, TransferState.Done, transfer.SizeBytes ?? 0));
                }
                else if (await _engine.ResumeReceivingAsync(transfer.Id, ct))
                {
                    if (transfer.Paused)
                        await _engine.PauseReceivingAsync(transfer.Id, ct);
                    Set(new TransferView(transfer, transfer.Paused ? TransferState.Paused : TransferState.Connecting, 0));
                }
                else
                {
                    // The engine lost it; there is nothing left to continue.
                    await _repository.RemoveAsync(transfer.Id, ct);
                }
            }
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    public IReadOnlyList<TransferView> List()
    {
        lock (_transfers)
            return [.. _transfers.Values.OrderBy(v => v.Transfer.AddedAt)];
    }

    /// <summary>
    /// What an invite or download link says about the file, and the room left for it, so the person
    /// can agree before anything is received (AD-16).
    /// </summary>
    public Result<InvitePreview> Preview(string? text)
    {
        if (!TryReadInvite(text, out var invite, out var summary))
            return Result<InvitePreview>.Fail(ErrorKind.InviteInvalid);

        // The invite's own hints win; the engine fills in what a link without hints announces.
        var hints = invite.Hints with
        {
            Name = invite.Hints.Name ?? summary.Name,
            SizeBytes = invite.Hints.SizeBytes ?? summary.SizeBytes,
        };
        return Result<InvitePreview>.Success(new InvitePreview(hints, invite.IsLink, _storage?.GetReceivedFreeBytes()));
    }

    /// <summary>Starts receiving the file an invite or a download link points to.</summary>
    public async Task<Result<TransferView>> ReceiveAsync(string? text, CancellationToken ct = default)
    {
        var preview = Preview(text);
        if (!preview.Ok)
            return Result<TransferView>.Fail(preview.Error!.Value);
        if (preview.Value!.NotEnoughSpace)
            return Result<TransferView>.Fail(ErrorKind.DiskFull);

        TryReadInvite(text, out var invite, out _);
        var key = _engine.ReadInviteKey(invite.EngineText);
        if (key is null)
            return Result<TransferView>.Fail(ErrorKind.InviteInvalid);
        var source = invite.IsLink ? TransferSource.Invite : TransferSource.DownloadLink;
        return await StartAsync(key, id => _engine.ReceiveAsync(id, invite.EngineText, ct), ErrorKind.InviteInvalid, source, ct);
    }

    /// <summary>
    /// The clipboard's ComParta invite, if it holds one. The clipboard is read only when the person
    /// opens the receive dialog, and nothing is kept.
    /// </summary>
    public static string? FindInvite(string? clipboardText) => InviteCodec.FindLink(clipboardText);

    private bool TryReadInvite(string? text, out Invite invite, out InviteSummary summary)
    {
        summary = null!;
        if (!InviteCodec.TryRead(text, out invite))
            return false;
        var described = _engine.DescribeInvite(invite.EngineText);
        if (described is null)
            return false;
        summary = described;
        return true;
    }

    /// <summary>
    /// Opens the file dialog for a download file and starts receiving what it describes.
    /// A cancelled dialog is a success with no value.
    /// </summary>
    public async Task<Result<TransferView?>> OpenFileAsync(CancellationToken ct = default)
    {
        if (_picker is null || _files is null)
            return Result<TransferView?>.Fail(ErrorKind.Unexpected);
        var path = await _picker.PickDownloadFileAsync(ct);
        if (path is null)
            return Result<TransferView?>.Success(null);

        byte[] content;
        try
        {
            content = _files.ReadSmallFile(path, MaxDownloadFileBytes);
        }
        catch (Exception)
        {
            return Result<TransferView?>.Fail(ErrorKind.FileUnreadable);
        }

        var key = _engine.ReadDownloadFileKey(content);
        if (key is null)
            return Result<TransferView?>.Fail(ErrorKind.DownloadFileInvalid);
        var started = await StartAsync(
            key, id => _engine.ReceiveFromFileAsync(id, content, ct), ErrorKind.DownloadFileInvalid, TransferSource.DownloadFile, ct);
        return started.Ok ? Result<TransferView?>.Success(started.Value) : Result<TransferView?>.Fail(started.Error!.Value);
    }

    private async Task<Result<TransferView>> StartAsync(
        string key, Func<TransferId, Task<bool>> receive, ErrorKind refused, TransferSource source, CancellationToken ct)
    {
        TransferView view;
        await _gate.WaitAsync(ct);
        try
        {
            var running = List().Where(v => v.State != TransferState.Done).Select(v => v.Transfer.Id);
            if (running.Any(id => _engine.GetReceiveKey(id) == key))
                return Result<TransferView>.Fail(ErrorKind.TransferDuplicate);

            var transfer = new Transfer(TransferId.New(), null, null, false, _clock.GetUtcNow(), Source: source);
            if (!await receive(transfer.Id))
                return Result<TransferView>.Fail(refused);
            await _repository.AddAsync(transfer, ct);
            view = Set(new TransferView(transfer, TransferState.Connecting, 0));
        }
        finally { _gate.Release(); }

        Changed?.Invoke();
        return Result<TransferView>.Success(view);
    }

    /// <summary>Stops moving bytes for a running transfer; it stays paused after a restart.</summary>
    public Task<Result> PauseAsync(TransferId id, CancellationToken ct = default) => SetPausedAsync(id, true, ct);

    /// <summary>Continues a paused transfer.</summary>
    public Task<Result> ContinueAsync(TransferId id, CancellationToken ct = default) => SetPausedAsync(id, false, ct);

    private async Task<Result> SetPausedAsync(TransferId id, bool paused, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            TransferView? view;
            lock (_transfers)
            {
                if (!_transfers.TryGetValue(id, out view))
                    return Result.Fail(ErrorKind.TransferNotFound);
            }
            if (view.State is TransferState.Done or TransferState.Failed)
                return Result.Fail(ErrorKind.TransferFinished);
            if (view.Transfer.Paused == paused)
                return Result.Success;

            if (paused)
                await _engine.PauseReceivingAsync(id, ct);
            else
                await _engine.ContinueReceivingAsync(id, ct);

            var transfer = view.Transfer with { Paused = paused };
            var state = paused ? TransferState.Paused
                : transfer.SizeBytes is null || view.ReceivedBytes == 0 ? TransferState.Connecting
                : TransferState.Receiving;
            Set(new TransferView(transfer, state, view.ReceivedBytes));
            await _repository.UpdateAsync(transfer, ct);
        }
        finally { _gate.Release(); }

        Changed?.Invoke();
        return Result.Success;
    }

    /// <summary>
    /// Takes a transfer off the list. An unfinished one is stopped and its partial data deleted;
    /// a finished file stays in the Received folder.
    /// </summary>
    public async Task<Result> RemoveAsync(TransferId id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            TransferView? view;
            lock (_transfers)
            {
                if (!_transfers.Remove(id, out view))
                    return Result.Fail(ErrorKind.TransferNotFound);
            }
            if (view.State != TransferState.Done)
                await _engine.StopReceivingAsync(id, ct);
            await _repository.RemoveAsync(id, ct);
        }
        finally { _gate.Release(); }

        Changed?.Invoke();
        return Result.Success;
    }

    private void OnReceiveProgressed(ReceiveProgress progress)
    {
        TransferView updated;
        bool persist;
        lock (_transfers)
        {
            if (!_transfers.TryGetValue(progress.Id, out var view) || view.State == TransferState.Done)
                return;
            // A paused transfer moves nothing; a late report from before the pause is not news.
            if (view.State == TransferState.Paused && !progress.Done)
                return;

            var transfer = view.Transfer with
            {
                Name = progress.Name ?? view.Transfer.Name,
                SizeBytes = progress.SizeBytes ?? view.Transfer.SizeBytes,
                Done = progress.Done,
            };
            var state = progress.Done ? TransferState.Done
                : progress.Failed ? TransferState.Failed
                : transfer.SizeBytes is null ? TransferState.Connecting
                : progress.ReceivedBytes > 0 ? TransferState.Receiving
                : view.State;
            var received = progress.Done ? transfer.SizeBytes ?? progress.ReceivedBytes
                : progress.Failed ? view.ReceivedBytes
                : progress.ReceivedBytes;
            updated = new TransferView(transfer, state, received, state == TransferState.Receiving ? progress.BytesPerSecond : 0);
            if (updated == view)
                return;
            persist = transfer != view.Transfer;
            _transfers[progress.Id] = updated;
        }

        if (persist)
            _ = PersistAsync(updated.Transfer);
        Changed?.Invoke();
    }

    private async Task PersistAsync(Transfer transfer)
    {
        try { await _repository.UpdateAsync(transfer); }
        catch (Exception) { /* the next progress report or restart corrects it */ }
    }

    private TransferView Set(TransferView view)
    {
        lock (_transfers)
            _transfers[view.Transfer.Id] = view;
        return view;
    }
}
