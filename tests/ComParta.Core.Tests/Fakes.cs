using ComParta.Core.Ports;

namespace ComParta.Core.Tests;

internal sealed class FakeShareRepository : IShareRepository
{
    public List<Share> Items { get; } = [];
    public Task<IReadOnlyList<Share>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Share>>([.. Items]);
    public Task AddAsync(Share share, CancellationToken ct = default) { Items.Add(share); return Task.CompletedTask; }
    public Task RemoveAsync(ShareId id, CancellationToken ct = default) { Items.RemoveAll(s => s.Id == id); return Task.CompletedTask; }
}

internal sealed class FakeTransferRepository : ITransferRepository
{
    public List<Transfer> Items { get; } = [];
    public Task<IReadOnlyList<Transfer>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Transfer>>([.. Items]);
    public Task AddAsync(Transfer transfer, CancellationToken ct = default) { Items.Add(transfer); return Task.CompletedTask; }
    public Task UpdateAsync(Transfer transfer, CancellationToken ct = default)
    {
        var index = Items.FindIndex(t => t.Id == transfer.Id);
        if (index >= 0)
            Items[index] = transfer;
        return Task.CompletedTask;
    }
    public Task RemoveAsync(TransferId id, CancellationToken ct = default) { Items.RemoveAll(t => t.Id == id); return Task.CompletedTask; }
}

/// <summary>An engine that offers at once and receives only when the test says so.</summary>
internal sealed class FakeEngine : ITransferEngine
{
    public const string InvitePrefix = "invite:";
    public const string DownloadFilePrefix = "download-file:";

    public event Action<ShareId, OfferState>? OfferChanged;
    public event Action<ReceiveProgress>? ReceiveProgressed;
    public event Action<ReachabilityFacts>? ReachabilityChanged;

    public SharingPolicy? Policy { get; private set; }
    public Dictionary<ShareId, string> Offered { get; } = [];
    public Dictionary<TransferId, string> Receiving { get; } = [];
    public List<TransferId> Stopped { get; } = [];
    public HashSet<TransferId> Paused { get; } = [];
    public OfferState OfferOutcome { get; set; } = OfferState.Ready;

    public Task StartAsync(SharingPolicy policy, CancellationToken ct = default) { Policy = policy; return Task.CompletedTask; }
    public Task StopAsync() => Task.CompletedTask;

    public Task OfferAsync(ShareId id, string filePath, CancellationToken ct = default)
    {
        Offered[id] = filePath;
        OfferChanged?.Invoke(id, OfferOutcome);
        return Task.CompletedTask;
    }

    public Task StopOfferingAsync(ShareId id, CancellationToken ct = default) { Offered.Remove(id); return Task.CompletedTask; }

    public Task<string?> GetInviteAsync(ShareId id, CancellationToken ct = default) =>
        Task.FromResult(Offered.ContainsKey(id) ? InvitePrefix + id : null);

    public string? ReadInviteKey(string inviteText) =>
        inviteText.StartsWith(InvitePrefix, StringComparison.Ordinal) ? inviteText[InvitePrefix.Length..] : null;

    public string? GetReceiveKey(TransferId id) => Receiving.GetValueOrDefault(id);

    public Task<bool> ReceiveAsync(TransferId id, string inviteText, CancellationToken ct = default)
    {
        var key = ReadInviteKey(inviteText);
        if (key is null)
            return Task.FromResult(false);
        Receiving[id] = key;
        return Task.FromResult(true);
    }

    public Task<bool> ResumeReceivingAsync(TransferId id, CancellationToken ct = default) => Task.FromResult(Receiving.ContainsKey(id));

    public Task StopReceivingAsync(TransferId id, CancellationToken ct = default)
    {
        Receiving.Remove(id);
        Stopped.Add(id);
        return Task.CompletedTask;
    }

    public string? ReadDownloadFileKey(byte[] content)
    {
        var text = System.Text.Encoding.UTF8.GetString(content);
        return text.StartsWith(DownloadFilePrefix, StringComparison.Ordinal) ? text[DownloadFilePrefix.Length..] : null;
    }

    public Task<bool> ReceiveFromFileAsync(TransferId id, byte[] content, CancellationToken ct = default)
    {
        var key = ReadDownloadFileKey(content);
        if (key is null)
            return Task.FromResult(false);
        Receiving[id] = key;
        return Task.FromResult(true);
    }

    public Task PauseReceivingAsync(TransferId id, CancellationToken ct = default) { Paused.Add(id); return Task.CompletedTask; }

    public Task ContinueReceivingAsync(TransferId id, CancellationToken ct = default) { Paused.Remove(id); return Task.CompletedTask; }

    public void Report(ReceiveProgress progress) => ReceiveProgressed?.Invoke(progress);

    public void Report(ReachabilityFacts facts) => ReachabilityChanged?.Invoke(facts);
}

internal sealed class FakeFilePicker : IFilePicker
{
    public string? NextPick { get; set; }
    public Task<string?> PickFileAsync(CancellationToken ct = default) => Task.FromResult(NextPick);
    public Task<string?> PickDownloadFileAsync(CancellationToken ct = default) => Task.FromResult(NextPick);
}

/// <summary>Holds sizes only; it has no way to change, move or delete a file.</summary>
internal sealed class FakeFileSystem : IFileSystem
{
    public Dictionary<string, long> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Locked { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FileExists(string path) => Files.ContainsKey(path);

    public Dictionary<string, byte[]> Contents { get; } = new(StringComparer.OrdinalIgnoreCase);

    public long GetReadableFileSize(string path) =>
        Locked.Contains(path) ? throw new UnauthorizedAccessException() : Files[path];

    public byte[] ReadSmallFile(string path, int maxBytes) =>
        Locked.Contains(path) || !Contents.TryGetValue(path, out var content) ? throw new UnauthorizedAccessException()
        : content.Length > maxBytes ? throw new IOException()
        : content;
}
