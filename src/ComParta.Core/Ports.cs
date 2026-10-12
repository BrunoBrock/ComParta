namespace ComParta.Core.Ports;

public interface IShareRepository
{
    Task<IReadOnlyList<Share>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Share share, CancellationToken ct = default);
    Task RemoveAsync(ShareId id, CancellationToken ct = default);
}

public interface ITransferRepository
{
    Task<IReadOnlyList<Transfer>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Transfer transfer, CancellationToken ct = default);
    Task UpdateAsync(Transfer transfer, CancellationToken ct = default);
    Task RemoveAsync(TransferId id, CancellationToken ct = default);
}

public enum OfferState { Preparing, Ready, Failed }

public sealed record ReceiveProgress(
    TransferId Id, string? Name, long? SizeBytes, long ReceivedBytes, bool Done, bool Failed = false, long BytesPerSecond = 0);

/// <summary>
/// The only way core reaches the transfer engine (AD-2). Invites are opaque text to core.
/// </summary>
public interface ITransferEngine
{
    event Action<ShareId, OfferState>? OfferChanged;
    event Action<ReceiveProgress>? ReceiveProgressed;
    /// <summary>
    /// The engine's current observations about being reachable, raised whenever they differ from the
    /// last report. Facts only: core decides what they mean (AD-15).
    /// </summary>
    event Action<ReachabilityFacts>? ReachabilityChanged;

    Task StartAsync(SharingPolicy policy, CancellationToken ct = default);
    Task StopAsync();

    /// <summary>Starts offering the file in place. Returns at once; <see cref="OfferChanged"/> reports the outcome.</summary>
    Task OfferAsync(ShareId id, string filePath, CancellationToken ct = default);
    /// <summary>Stops offering and forgets the share. Never touches the file.</summary>
    Task StopOfferingAsync(ShareId id, CancellationToken ct = default);
    /// <summary>The invite text for a share that is ready, or null when it is not.</summary>
    Task<string?> GetInviteAsync(ShareId id, CancellationToken ct = default);

    /// <summary>A stable key for the content an invite points to, or null when the text is not an invite.</summary>
    string? ReadInviteKey(string inviteText);
    /// <summary>The content key of a transfer the engine knows, or null.</summary>
    string? GetReceiveKey(TransferId id);
    /// <summary>Starts receiving into the Received root. Returns false when the text is not an invite.</summary>
    Task<bool> ReceiveAsync(TransferId id, string inviteText, CancellationToken ct = default);
    /// <summary>Continues a transfer started in an earlier run. Returns false when the engine no longer knows it.</summary>
    Task<bool> ResumeReceivingAsync(TransferId id, CancellationToken ct = default);
    /// <summary>Stops a transfer and deletes its unfinished data. A finished file is kept.</summary>
    Task StopReceivingAsync(TransferId id, CancellationToken ct = default);

    /// <summary>A stable content key for the bytes of a download file, or null when they are not one.</summary>
    string? ReadDownloadFileKey(byte[] content);
    /// <summary>Starts receiving what a download file describes into the Received root. False when it is not one.</summary>
    Task<bool> ReceiveFromFileAsync(TransferId id, byte[] content, CancellationToken ct = default);
    /// <summary>Stops moving bytes for a transfer but keeps everything needed to continue it.</summary>
    Task PauseReceivingAsync(TransferId id, CancellationToken ct = default);
    /// <summary>Continues a paused transfer.</summary>
    Task ContinueReceivingAsync(TransferId id, CancellationToken ct = default);
}

/// <summary>Opens the native file dialog (AD-7). Returns null when the person cancels.</summary>
public interface IFilePicker
{
    Task<string?> PickFileAsync(CancellationToken ct = default);
    /// <summary>Opens the native file dialog showing only download files (AD-21).</summary>
    Task<string?> PickDownloadFileAsync(CancellationToken ct = default);
}

public interface IClipboard
{
    Task SetTextAsync(string text, CancellationToken ct = default);
}

/// <summary>Shows received content to the person with the platform's own file browser.</summary>
public interface IContentOpener
{
    Task OpenReceivedFolderAsync(CancellationToken ct = default);
}

public interface IFileSystem
{
    bool FileExists(string path);
    /// <summary>Size in bytes; throws when the file cannot be read.</summary>
    long GetReadableFileSize(string path);
    /// <summary>The whole content of a small file; throws when it cannot be read or is larger than <paramref name="maxBytes"/>.</summary>
    byte[] ReadSmallFile(string path, int maxBytes);
}

public interface IAppPaths
{
    string DatabasePath { get; }
    /// <summary>Written only by the engine adapter (AD-10).</summary>
    string EngineRoot { get; }
    /// <summary>The single place received content is written (AD-6).</summary>
    string ReceivedRoot { get; }
    string LogsRoot { get; }
}
