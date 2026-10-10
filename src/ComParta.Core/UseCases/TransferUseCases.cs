using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <summary>Receive a file from an invite and follow how it is going.</summary>
public sealed class TransferUseCases
{
    private readonly ITransferRepository _repository;
    private readonly ITransferEngine _engine;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<TransferId, TransferView> _transfers = [];

    public TransferUseCases(ITransferRepository repository, ITransferEngine engine, TimeProvider? clock = null)
    {
        _repository = repository;
        _engine = engine;
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
                    Set(new TransferView(transfer, TransferState.Connecting, 0));
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

    /// <summary>Starts receiving the file an invite points to.</summary>
    public async Task<Result<TransferView>> ReceiveAsync(string? inviteText, CancellationToken ct = default)
    {
        inviteText = inviteText?.Trim() ?? "";
        var key = _engine.ReadInviteKey(inviteText);
        if (key is null)
            return Result<TransferView>.Fail(ErrorKind.InviteInvalid);

        TransferView view;
        await _gate.WaitAsync(ct);
        try
        {
            var running = List().Where(v => v.State != TransferState.Done).Select(v => v.Transfer.Id);
            if (running.Any(id => _engine.GetReceiveKey(id) == key))
                return Result<TransferView>.Fail(ErrorKind.TransferDuplicate);

            var transfer = new Transfer(TransferId.New(), null, null, false, _clock.GetUtcNow());
            if (!await _engine.ReceiveAsync(transfer.Id, inviteText, ct))
                return Result<TransferView>.Fail(ErrorKind.InviteInvalid);
            await _repository.AddAsync(transfer, ct);
            view = Set(new TransferView(transfer, TransferState.Connecting, 0));
        }
        finally { _gate.Release(); }

        Changed?.Invoke();
        return Result<TransferView>.Success(view);
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
            updated = new TransferView(transfer, state, received);
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
