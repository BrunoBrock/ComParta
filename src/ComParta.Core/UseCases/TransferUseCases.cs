using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <summary>
/// Receive a file from an invite, a download link or a download file, and follow how it is going.
/// </summary>
public sealed class TransferUseCases
{
    /// <summary>Download files are small descriptions; anything larger is not one.</summary>
    public const int MaxDownloadFileBytes = 16 * 1024 * 1024;
    /// <summary>How long without any connection before a transfer says it is waiting (UX States table).</summary>
    public static readonly TimeSpan WaitingAfter = TimeSpan.FromMinutes(2);
    /// <summary>How often a transfer stopped for lack of room looks again.</summary>
    public static readonly TimeSpan NoSpaceRetry = TimeSpan.FromMinutes(1);

    private readonly ITransferRepository _repository;
    private readonly ITransferEngine _engine;
    private readonly IFilePicker? _picker;
    private readonly IFileSystem? _files;
    private readonly IStorageInfo? _storage;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<TransferId, TransferView> _transfers = [];
    private readonly Dictionary<TransferId, TimeLeft> _timeLeft = [];
    // When each transfer last had a connection or received something.
    private readonly Dictionary<TransferId, DateTimeOffset> _lastContact = [];
    // When each transfer stopped for lack of room last looked for room.
    private readonly Dictionary<TransferId, DateTimeOffset> _roomCheckedAt = [];
    // The download file Windows opened ComParta with, read and waiting for "Get the file".
    private byte[]? _openedFile;

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
        _engine.ActivityChanged += OnActivityChanged;
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
                    await ApplyChoicesAsync(transfer, ct);
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
    /// The clipboard's invite, if it holds one: a ComParta link from an earlier build, or the
    /// engine's own link. The clipboard is read only when the person opens the receive dialog, and
    /// nothing is kept.
    /// </summary>
    public string? FindInvite(string? clipboardText) =>
        InviteCodec.FindLink(clipboardText) ?? (string.IsNullOrEmpty(clipboardText) ? null : _engine.FindInvite(clipboardText));

    private bool TryReadInvite(string? text, out Invite invite, out InviteSummary summary)
    {
        summary = null!;
        if (!InviteCodec.TryRead(text, out invite))
            return false;
        // The engine's own link may sit inside a whole chat message, as a ComParta link may.
        if (!invite.IsLink && _engine.FindInvite(invite.EngineText) is { } found)
            invite = invite with { EngineText = found };
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

        var started = await ReceiveFileAsync(content, ct);
        return started.Ok ? Result<TransferView?>.Success(started.Value) : Result<TransferView?>.Fail(started.Error!.Value);
    }

    /// <summary>
    /// Reads a download file Windows opened ComParta with, and says what it holds, so the person can
    /// agree before anything is received (epic 14 story 5). It waits for <see cref="ReceiveOpenedFileAsync"/>.
    /// </summary>
    public Result<InvitePreview> PreviewOpenedFile(string path)
    {
        if (_files is null)
            return Result<InvitePreview>.Fail(ErrorKind.Unexpected);
        byte[] content;
        try
        {
            content = _files.ReadSmallFile(path, MaxDownloadFileBytes);
        }
        catch (Exception)
        {
            return Result<InvitePreview>.Fail(ErrorKind.FileUnreadable);
        }
        if (_engine.DescribeDownloadFile(content) is not { } summary)
            return Result<InvitePreview>.Fail(ErrorKind.DownloadFileInvalid);
        lock (_transfers)
            _openedFile = content;
        var hints = InviteHints.None with { Name = summary.Name, SizeBytes = summary.SizeBytes };
        return Result<InvitePreview>.Success(new InvitePreview(hints, false, _storage?.GetReceivedFreeBytes()));
    }

    /// <summary>Starts receiving the download file Windows opened ComParta with, once.</summary>
    public async Task<Result<TransferView>> ReceiveOpenedFileAsync(CancellationToken ct = default)
    {
        byte[]? content;
        lock (_transfers)
        {
            content = _openedFile;
            _openedFile = null;
        }
        if (content is null)
            return Result<TransferView>.Fail(ErrorKind.DownloadFileInvalid);
        if (_engine.DescribeDownloadFile(content) is { SizeBytes: { } size }
            && _storage?.GetReceivedFreeBytes() is { } free && size > free)
            return Result<TransferView>.Fail(ErrorKind.DiskFull);
        return await ReceiveFileAsync(content, ct);
    }

    private Task<Result<TransferView>> ReceiveFileAsync(byte[] content, CancellationToken ct)
    {
        var key = _engine.ReadDownloadFileKey(content);
        if (key is null)
            return Task.FromResult(Result<TransferView>.Fail(ErrorKind.DownloadFileInvalid));
        return StartAsync(
            key, id => _engine.ReceiveFromFileAsync(id, content, ct), ErrorKind.DownloadFileInvalid, TransferSource.DownloadFile, ct);
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
            await ApplyChoicesAsync(transfer, ct);
            await _repository.AddAsync(transfer, ct);
            view = Set(new TransferView(transfer, TransferState.Connecting, 0));
        }
        finally { _gate.Release(); }

        Changed?.Invoke();
        return Result<TransferView>.Success(view);
    }

    /// <summary>Hands the engine what the person chose under More options, for a new or continued transfer.</summary>
    private async Task ApplyChoicesAsync(Transfer transfer, CancellationToken ct)
    {
        if (transfer.SkippedFiles is { Count: > 0 } skipped)
            await _engine.SetSkippedFilesAsync(transfer.Id, skipped, ct);
        await _engine.SetSharingWhenDoneAsync(transfer.Id, transfer.SharesWhenDone, ct);
    }

    /// <summary>What More options shows for a download (epic 14 story 4).</summary>
    public Result<TransferOptions> GetOptions(TransferId id)
    {
        TransferView? view;
        lock (_transfers)
        {
            if (!_transfers.TryGetValue(id, out view))
                return Result<TransferOptions>.Fail(ErrorKind.TransferNotFound);
        }
        var files = view.State == TransferState.Done ? null : _engine.GetReceiveFiles(id);
        return Result<TransferOptions>.Success(new TransferOptions(
            files, view.Transfer.SharesWhenDone, view.State == TransferState.Done && _engine.IsSharingReceived(id)));
    }

    /// <summary>
    /// Changes the choices under More options: which files of a download to get, while it is on its
    /// way, and whether a public download is shared once it has arrived. Both are kept after a
    /// restart. A null choice stays as it was.
    /// </summary>
    public async Task<Result> SetOptionsAsync(
        TransferId id, IReadOnlyCollection<int>? skipped, bool? keepSharing, CancellationToken ct = default)
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
            var transfer = view.Transfer;
            if (skipped is not null)
            {
                if (view.State is TransferState.Done or TransferState.Failed or TransferState.Blocked)
                    return Result.Fail(ErrorKind.TransferFinished);
                var files = _engine.GetReceiveFiles(id);
                var chosen = skipped.Distinct().Where(i => i >= 0 && (files is null || i < files.Count)).Order().ToArray();
                if (files is not null && chosen.Length >= files.Count)
                    return Result.Fail(ErrorKind.NoFilesChosen);
                await _engine.SetSkippedFilesAsync(id, chosen, ct);
                transfer = transfer with { SkippedFiles = chosen.Length == 0 ? null : chosen };
            }
            if (keepSharing is { } keep && transfer.Source != TransferSource.Invite)
            {
                await _engine.SetSharingWhenDoneAsync(id, keep, ct);
                transfer = transfer with { KeepSharing = keep };
            }
            if (transfer == view.Transfer)
                return Result.Success;
            lock (_transfers)
            {
                if (_transfers.TryGetValue(id, out var current))
                    _transfers[id] = current with { Transfer = transfer };
            }
            await _repository.UpdateAsync(transfer, ct);
        }
        finally { _gate.Release(); }

        Changed?.Invoke();
        return Result.Success;
    }

    /// <summary>Stops sharing a file that arrived, for example before it is deleted. The file stays.</summary>
    public Task StopSharingAsync(TransferId id, CancellationToken ct = default) => _engine.SetSharingWhenDoneAsync(id, false, ct);

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
            if (view.State is TransferState.Done or TransferState.Failed or TransferState.Blocked)
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
                _timeLeft.Remove(id);
                _lastContact.Remove(id);
                _roomCheckedAt.Remove(id);
            }
            if (view.State != TransferState.Done)
                await _engine.StopReceivingAsync(id, ct);
            else
                await _engine.SetSharingWhenDoneAsync(id, false, ct);
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
        var outOfRoom = false;
        lock (_transfers)
        {
            if (!_transfers.TryGetValue(progress.Id, out var view) || view.State is TransferState.Done or TransferState.Blocked)
                return;
            // A paused transfer moves nothing; a late report from before the pause is not news.
            if (view.State == TransferState.Paused && !progress.Done)
                return;
            // One stopped for lack of room waits for the next look, not for late reports.
            if (view.State == TransferState.NoSpace && !progress.Done && !progress.NoSpace)
                return;

            var now = _clock.GetUtcNow();
            var transfer = view.Transfer with
            {
                Name = progress.Name ?? view.Transfer.Name,
                SizeBytes = progress.SizeBytes ?? view.Transfer.SizeBytes,
                Done = progress.Done,
            };
            var received = progress.Done ? transfer.SizeBytes ?? progress.ReceivedBytes
                : progress.Failed ? view.ReceivedBytes
                : progress.ReceivedBytes;
            if (received > view.ReceivedBytes)
                _lastContact[progress.Id] = now;
            var left = transfer.SizeBytes is { } size ? size - received : (long?)null;
            var free = _storage?.GetReceivedFreeBytes();
            outOfRoom = !progress.Done && !progress.Failed && !progress.NoSpace && left is > 0 && free is { } room && room < left;

            var state = progress.Done ? TransferState.Done
                : progress.Failed ? TransferState.Failed
                : progress.NoSpace || outOfRoom ? TransferState.NoSpace
                : transfer.SizeBytes is null ? TransferState.Connecting
                : received > 0 ? TransferState.Receiving
                : view.State == TransferState.Waiting ? TransferState.Connecting
                : view.State;
            if (state is TransferState.Connecting or TransferState.Receiving && OutOfContact(progress.Id, now))
                state = TransferState.Waiting;

            long? secondsLeft = null;
            if (state == TransferState.Receiving)
            {
                if (!_timeLeft.TryGetValue(progress.Id, out var timeLeft))
                    _timeLeft[progress.Id] = timeLeft = new TimeLeft(_clock);
                secondsLeft = timeLeft.Update(received, left ?? 0);
            }
            else
                _timeLeft.Remove(progress.Id);
            if (state == TransferState.NoSpace)
                _roomCheckedAt.TryAdd(progress.Id, now);
            long? needed = state == TransferState.NoSpace && left is { } l && free is { } f ? Math.Max(l - f, 1) : null;
            updated = new TransferView(
                transfer, state, received, state == TransferState.Receiving ? progress.BytesPerSecond : 0, secondsLeft, needed);
            if (updated == view)
                return;
            persist = transfer != view.Transfer;
            _transfers[progress.Id] = updated;
        }

        // Stopped here, before the disk fills, so nothing half-written is lost (UX States table).
        if (outOfRoom)
            _ = PauseQuietlyAsync(progress.Id);
        if (persist)
            _ = PersistAsync(updated.Transfer);
        Changed?.Invoke();
    }

    private bool OutOfContact(TransferId id, DateTimeOffset now) =>
        _lastContact.TryGetValue(id, out var last) && now - last >= WaitingAfter;

    /// <summary>
    /// Each second: a connection counts as contact, a transfer without any for a while says it is
    /// waiting, and one stopped for lack of room looks again every minute.
    /// </summary>
    private void OnActivityChanged()
    {
        EngineActivity activity;
        try { activity = _engine.GetActivity(); }
        catch (Exception) { return; }
        var now = _clock.GetUtcNow();
        var changed = false;
        var retry = new List<TransferId>();
        lock (_transfers)
        {
            foreach (var view in _transfers.Values.ToList())
            {
                var id = view.Transfer.Id;
                if (activity.Transfers.GetValueOrDefault(id) is { Connections: > 0 })
                    _lastContact[id] = now;
                var next = view.State switch
                {
                    TransferState.Connecting or TransferState.Receiving when OutOfContact(id, now) => TransferState.Waiting,
                    TransferState.Waiting when !OutOfContact(id, now) =>
                        view.ReceivedBytes > 0 && view.Transfer.SizeBytes is not null ? TransferState.Receiving : TransferState.Connecting,
                    _ => view.State,
                };
                if (view.State == TransferState.NoSpace
                    && _roomCheckedAt.TryGetValue(id, out var checkedAt) && now - checkedAt >= NoSpaceRetry)
                {
                    _roomCheckedAt[id] = now;
                    var left = (view.Transfer.SizeBytes ?? 0) - view.ReceivedBytes;
                    if (_storage?.GetReceivedFreeBytes() is not { } free || free >= left)
                    {
                        retry.Add(id);
                        _roomCheckedAt.Remove(id);
                        _lastContact[id] = now;
                        next = view.ReceivedBytes > 0 ? TransferState.Receiving : TransferState.Connecting;
                    }
                }
                if (next == view.State)
                    continue;
                _transfers[id] = view with { State = next, BytesPerSecond = 0, SecondsLeft = null, NeededBytes = null };
                _timeLeft.Remove(id);
                changed = true;
            }
        }
        foreach (var id in retry)
            _ = ContinueQuietlyAsync(id);
        if (changed)
            Changed?.Invoke();
    }

    private async Task PauseQuietlyAsync(TransferId id)
    {
        try { await _engine.PauseReceivingAsync(id); }
        catch (Exception) { /* the next look tries again */ }
    }

    private async Task ContinueQuietlyAsync(TransferId id)
    {
        try { await _engine.ContinueReceivingAsync(id); }
        catch (Exception) { /* the next report says how it is going */ }
    }

    private async Task PersistAsync(Transfer transfer)
    {
        try { await _repository.UpdateAsync(transfer); }
        catch (Exception) { /* the next progress report or restart corrects it */ }
    }

    private TransferView Set(TransferView view)
    {
        lock (_transfers)
        {
            _transfers[view.Transfer.Id] = view;
            // Time left starts again from a new speed after a pause, and so does the wait.
            _timeLeft.Remove(view.Transfer.Id);
            _roomCheckedAt.Remove(view.Transfer.Id);
            _lastContact[view.Transfer.Id] = _clock.GetUtcNow();
        }
        return view;
    }
}
