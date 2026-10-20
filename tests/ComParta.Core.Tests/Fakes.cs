using ComParta.Core.Ports;

namespace ComParta.Core.Tests;

internal sealed class FakeShareRepository : IShareRepository
{
    public List<Share> Items { get; } = [];
    public Task<IReadOnlyList<Share>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Share>>([.. Items]);
    public Task AddAsync(Share share, CancellationToken ct = default) { Items.Add(share); return Task.CompletedTask; }
    public Task RemoveAsync(ShareId id, CancellationToken ct = default) { Items.RemoveAll(s => s.Id == id); return Task.CompletedTask; }
    public Task SetPausedAsync(ShareId id, bool paused, CancellationToken ct = default)
    {
        var index = Items.FindIndex(s => s.Id == id);
        if (index >= 0)
            Items[index] = Items[index] with { Paused = paused };
        return Task.CompletedTask;
    }
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

    public event Action? ActivityChanged;

    public EngineActivity Activity { get; set; } = EngineActivity.None;
    public EngineActivity GetActivity() => Activity;
    public (bool Sending, bool Receiving) StoppedFlags { get; private set; }
    public int StopCalls { get; private set; }

    /// <summary>When set, a stop waits for it, like a real engine stopping many files.</summary>
    public Task? HoldStop { get; set; }

    public async Task SetStoppedAsync(bool sending, bool receiving, CancellationToken ct = default)
    {
        StopCalls++;
        if (HoldStop is { } hold)
            await hold;
        StoppedFlags = (sending, receiving);
    }

    /// <summary>Sets new activity and tells whoever listens, as the real engine does every second.</summary>
    public void Move(EngineActivity activity)
    {
        Activity = activity;
        ActivityChanged?.Invoke();
    }

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

    /// <summary>Runs while a share is being let go of, to see what the app shows meanwhile.</summary>
    public Action? OnStopOffering { get; set; }

    public Task StopOfferingAsync(ShareId id, CancellationToken ct = default)
    {
        OnStopOffering?.Invoke();
        Offered.Remove(id);
        return Task.CompletedTask;
    }

    public HashSet<ShareId> PausedOffers { get; } = [];
    public Task PauseOfferingAsync(ShareId id, CancellationToken ct = default) { PausedOffers.Add(id); return Task.CompletedTask; }
    public Task ContinueOfferingAsync(ShareId id, CancellationToken ct = default) { PausedOffers.Remove(id); return Task.CompletedTask; }

    /// <summary>The public servers the engine was last told to use.</summary>
    public IReadOnlyList<Uri> PublicServers { get; private set; } = [];
    public int PublicServerCalls { get; private set; }

    public Task SetPublicServersAsync(IReadOnlyList<Uri> servers, CancellationToken ct = default)
    {
        PublicServers = servers;
        PublicServerCalls++;
        return Task.CompletedTask;
    }

    public HelperComputer? Helper { get; private set; }
    public int HelperCalls { get; private set; }

    public Task SetHelperAsync(HelperComputer? helper, CancellationToken ct = default)
    {
        Helper = helper;
        HelperCalls++;
        return Task.CompletedTask;
    }

    public Task<string?> GetInviteAsync(ShareId id, CancellationToken ct = default) =>
        Task.FromResult(Offered.ContainsKey(id) ? InvitePrefix + id : null);

    /// <summary>A download file's content is its invite, so a test can tell which share it describes.</summary>
    public Task<DownloadFile?> GetDownloadFileAsync(ShareId id, CancellationToken ct = default) =>
        Task.FromResult(Offered.TryGetValue(id, out var path)
            ? new DownloadFile(Path.GetFileName(path) + ".download", System.Text.Encoding.UTF8.GetBytes(InvitePrefix + id))
            : null);

    public string? FindInvite(string text) =>
        System.Text.RegularExpressions.Regex.Match(text, InvitePrefix + @"\S+") is { Success: true } found ? found.Value : null;

    public string? ReadInviteKey(string inviteText) =>
        inviteText.StartsWith(InvitePrefix, StringComparison.Ordinal) ? inviteText[InvitePrefix.Length..] : null;

    /// <summary>What an invite announces, by content key; anything else announces nothing.</summary>
    public Dictionary<string, InviteSummary> Announced { get; } = [];

    public InviteSummary? DescribeInvite(string inviteText) =>
        ReadInviteKey(inviteText) is { } key ? Announced.GetValueOrDefault(key) ?? new InviteSummary(null, null) : null;

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

    public List<TransferId> Continued { get; } = [];

    public Task ContinueReceivingAsync(TransferId id, CancellationToken ct = default)
    {
        Paused.Remove(id);
        Continued.Add(id);
        return Task.CompletedTask;
    }

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

    public List<string> Deleted { get; } = [];

    public void DeleteFile(string path)
    {
        if (Locked.Contains(path))
            throw new IOException("in use");
        Files.Remove(path);
        Deleted.Add(path);
    }

    public void WriteNewFile(string path, byte[] content)
    {
        if (Locked.Contains(path) || Files.ContainsKey(path))
            throw new IOException("cannot write");
        Files[path] = content.Length;
        Contents[path] = content;
    }

    public byte[] ReadSmallFile(string path, int maxBytes) =>
        Locked.Contains(path) || !Contents.TryGetValue(path, out var content) ? throw new UnauthorizedAccessException()
        : content.Length > maxBytes ? throw new IOException()
        : content;

    public Stream OpenRead(string path) =>
        Locked.Contains(path) || !Contents.TryGetValue(path, out var content) ? throw new UnauthorizedAccessException()
        : new MemoryStream(content, writable: false);
}

internal sealed class FakePaths(string root) : IAppPaths
{
    public string DatabasePath => Path.Combine(root, "data", "comparta.db");
    public string EngineRoot => Path.Combine(root, "engine");
    public string ReceivedRoot => Path.Combine(root, "Received");
    public string LogsRoot => Path.Combine(root, "logs");
    public string SavedRoot => Path.Combine(root, "Downloads");
}

internal sealed class FakeOpener : IContentOpener
{
    public int FolderOpened { get; private set; }
    public List<string> Opened { get; } = [];
    public List<string> Revealed { get; } = [];
    public bool OpenWorks { get; set; } = true;

    public Task OpenReceivedFolderAsync(CancellationToken ct = default) { FolderOpened++; return Task.CompletedTask; }
    public Task<bool> OpenAsync(string path, CancellationToken ct = default) { Opened.Add(path); return Task.FromResult(OpenWorks); }
    public Task RevealAsync(string path, CancellationToken ct = default) { Revealed.Add(path); return Task.CompletedTask; }
}

internal sealed class FakeThumbnails : IThumbnailProvider
{
    public List<(string Path, int Size)> Asked { get; } = [];
    public Thumbnail? Next { get; set; } = new("image/png", [1, 2, 3]);
    public bool Throws { get; set; }

    public Task<Thumbnail?> GetAsync(string path, int size, CancellationToken ct = default)
    {
        Asked.Add((path, size));
        if (Throws)
            throw new IOException("The system could not draw it.");
        return Task.FromResult(Next);
    }
}

internal sealed class FakeStorage : IStorageInfo
{
    public long? FreeBytes { get; set; }
    public long? GetReceivedFreeBytes() => FreeBytes;
}

internal sealed class FakeSettingsRepository : ISettingsRepository
{
    public Dictionary<string, string> Values { get; } = [];
    public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(Values.GetValueOrDefault(key));
    public Task SetAsync(string key, string? value, CancellationToken ct = default)
    {
        if (value is null) Values.Remove(key);
        else Values[key] = value;
        return Task.CompletedTask;
    }
}

internal sealed class FakeSystem(string uiLanguage) : ISystemInfo
{
    public string UiLanguage { get; } = uiLanguage;
}

internal sealed class FakeFirewall : IFirewall
{
    public bool? Allows { get; set; }
    public int SettingsOpened { get; private set; }
    public bool? AllowsIncoming() => Allows;
    public Task OpenSettingsAsync(CancellationToken ct = default) { SettingsOpened++; return Task.CompletedTask; }
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FakeNotifier : INotifier
{
    public List<Notification> Shown { get; } = [];
    public void Show(Notification notification) => Shown.Add(notification);
}

internal sealed class FakeWindow : IWindowState
{
    public bool IsInFront { get; set; }
}

internal sealed class FakePresence : IBackgroundPresence
{
    public List<bool> Awake { get; } = [];
    public List<bool> StartWithSystem { get; } = [];
    public int Quits { get; private set; }
    public void KeepAwake(bool awake) => Awake.Add(awake);
    void IBackgroundPresence.StartWithSystem(bool on) => StartWithSystem.Add(on);
    public void Quit() => Quits++;
}
