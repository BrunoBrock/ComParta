namespace ComParta.Core.Ports;

public interface IShareRepository
{
    Task<IReadOnlyList<Share>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Share share, CancellationToken ct = default);
    Task RemoveAsync(ShareId id, CancellationToken ct = default);
    Task SetPausedAsync(ShareId id, bool paused, CancellationToken ct = default);
}

public interface ITransferRepository
{
    Task<IReadOnlyList<Transfer>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Transfer transfer, CancellationToken ct = default);
    Task UpdateAsync(Transfer transfer, CancellationToken ct = default);
    Task RemoveAsync(TransferId id, CancellationToken ct = default);
}

/// <summary>The person's choices, kept in the product database (AD-10). Null removes a value.</summary>
public interface ISettingsRepository
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string? value, CancellationToken ct = default);
}

/// <summary>Facts about the computer the app runs on.</summary>
public interface ISystemInfo
{
    /// <summary>The language the operating system shows, as a tag like "pt-BR".</summary>
    string UiLanguage { get; }
}

public enum OfferState { Preparing, Ready, Failed }

/// <summary>What the engine can tell from invite text alone: the name and size it announces.</summary>
public sealed record InviteSummary(string? Name, long? SizeBytes);

/// <summary>What one share or transfer is doing now (AD-22).</summary>
/// <param name="Connections">How many other computers are connected for it.</param>
/// <param name="BytesReceived">Bytes received for it since the app started.</param>
/// <param name="BytesSent">Bytes sent for it since the app started.</param>
/// <param name="Receivers">For a share: how many connected devices are getting it now. Counts only, no addresses.</param>
/// <param name="ReceiversPercent">For a share: how much of it those devices have, on average, 0 to 100.</param>
/// <param name="Finished">For a share: how many devices got all of it from this one since the app started.</param>
public sealed record ItemActivity(
    long ReceivingBytesPerSecond,
    long SendingBytesPerSecond,
    int Connections,
    long BytesReceived,
    long BytesSent,
    int Receivers = 0,
    int ReceiversPercent = 0,
    int Finished = 0)
{
    public static ItemActivity None { get; } = new(0, 0, 0, 0, 0);
}

/// <summary>Everything the engine is moving now, in total and per item (AD-22).</summary>
public sealed record EngineActivity(
    long ReceivingBytesPerSecond,
    long SendingBytesPerSecond,
    IReadOnlyDictionary<ShareId, ItemActivity> Shares,
    IReadOnlyDictionary<TransferId, ItemActivity> Transfers)
{
    public static EngineActivity None { get; } = new(0, 0, new Dictionary<ShareId, ItemActivity>(), new Dictionary<TransferId, ItemActivity>());
}

/// <param name="NoSpace">Writing failed because the disk is full; the transfer is stopped until it is continued.</param>
public sealed record ReceiveProgress(
    TransferId Id, string? Name, long? SizeBytes, long ReceivedBytes, bool Done, bool Failed = false, long BytesPerSecond = 0,
    bool NoSpace = false);

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


    /// <summary>Raised about once a second while running, so speeds can be shown (AD-22).</summary>

    event Action? ActivityChanged;



    /// <summary>The speeds, connections and bytes moved now (AD-22).</summary>

    EngineActivity GetActivity();



    /// <summary>

    /// Stops or starts all sending and all receiving (AD-22). A transfer the person paused on its

    /// own stays paused when receiving starts again.

    /// </summary>

    Task SetStoppedAsync(bool sending, bool receiving, CancellationToken ct = default);


    Task StartAsync(SharingPolicy policy, CancellationToken ct = default);
    Task StopAsync();

    /// <summary>Starts offering the file in place. Returns at once; <see cref="OfferChanged"/> reports the outcome.</summary>
    Task OfferAsync(ShareId id, string filePath, CancellationToken ct = default);
    /// <summary>Stops offering and forgets the share. Never touches the file.</summary>
    Task StopOfferingAsync(ShareId id, CancellationToken ct = default);
    /// <summary>Stops sending one shared file; a global continue leaves it paused. Never touches the file.</summary>
    Task PauseOfferingAsync(ShareId id, CancellationToken ct = default);
    /// <summary>Sends a paused shared file again, unless all sending is stopped.</summary>
    Task ContinueOfferingAsync(ShareId id, CancellationToken ct = default);
    /// <summary>
    /// Public servers the person named (AD-8): every shared file is announced to them, now and from
    /// then on, and every invite carries them. An empty list stops using them.
    /// </summary>
    Task SetPublicServersAsync(IReadOnlyList<Uri> servers, CancellationToken ct = default);
    /// <summary>
    /// The invite text for a share that is ready, or null when it is not. It is a link the person
    /// can send as it is, and that other apps of the engine's kind open too (AD-16, amended).
    /// </summary>
    Task<string?> GetInviteAsync(ShareId id, CancellationToken ct = default);
    /// <summary>The first invite inside any text, such as a whole chat message, or null.</summary>
    string? FindInvite(string text);

    /// <summary>A stable key for the content an invite points to, or null when the text is not an invite.</summary>
    string? ReadInviteKey(string inviteText);
    /// <summary>The name and size the invite text announces, or null when it is not an invite.</summary>
    InviteSummary? DescribeInvite(string inviteText);
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
    /// <summary>Continues a paused transfer, or one that stopped because the disk was full.</summary>
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
    /// <summary>The text on the clipboard, or null. Read only when the person asks for it (AD-16).</summary>
    Task<string?> GetTextAsync(CancellationToken ct = default);
}

/// <summary>How much room there is where received files are written.</summary>
public interface IStorageInfo
{
    /// <summary>Free bytes on the drive that holds the Received root, or null when it cannot be told.</summary>
    long? GetReceivedFreeBytes();
}

/// <summary>The operating system's firewall, as far as it concerns this app (AD-15).</summary>
public interface IFirewall
{
    /// <summary>True when incoming connections to the app are allowed, false when blocked, null when not decided yet.</summary>
    bool? AllowsIncoming();
    /// <summary>Opens the system screen where the person can let the app through.</summary>
    Task OpenSettingsAsync(CancellationToken ct = default);
}

/// <summary>Shows received content to the person with the platform's own file browser.</summary>
public interface IContentOpener
{
    Task OpenReceivedFolderAsync(CancellationToken ct = default);
    /// <summary>Opens one received file with the program Windows uses for it; false when that failed.</summary>
    Task<bool> OpenAsync(string path, CancellationToken ct = default);
    /// <summary>Shows the folder with this one file selected.</summary>
    Task RevealAsync(string path, CancellationToken ct = default);
}

/// <summary>Which item a system notification opens when the person clicks it.</summary>
public enum NotifiedItem { Share, Transfer }

/// <summary>A system notification (CAP-20), already in the person's language.</summary>
/// <param name="Kind">The item a click opens, or null when a click only opens the window.</param>
/// <param name="Name">The item's name, so the screen can title its details at once.</param>
public sealed record Notification(string Title, string Body, NotifiedItem? Kind = null, string Id = "", string Name = "");

/// <summary>Shows notifications the operating system's way (AD-11).</summary>
public interface INotifier
{
    void Show(Notification notification);
}

/// <summary>Whether the person is looking at the app's window.</summary>
public interface IWindowState
{
    /// <summary>True while the window is open, not minimised, and the one in front.</summary>
    bool IsInFront { get; }
}

/// <summary>
/// What the platform does so the app keeps working while its window is closed (AD-14): on Windows,
/// the notification-area icon, start with Windows and staying awake.
/// </summary>
public interface IBackgroundPresence
{
    /// <summary>Asks the system not to sleep while bytes are moving (the display may still turn off), or lets it sleep again.</summary>
    void KeepAwake(bool awake);
    /// <summary>Registers the app to start hidden when the person signs in, or removes that.</summary>
    void StartWithSystem(bool on);
    /// <summary>Ends the app, window and icon included.</summary>
    void Quit();
}

/// <summary>A small picture of a file, as the operating system draws it.</summary>
public sealed record Thumbnail(string ContentType, byte[] Bytes);

/// <summary>Thumbnails for photos and videos (AD-18). Null when the system has none for the file.</summary>
public interface IThumbnailProvider
{
    Task<Thumbnail?> GetAsync(string path, int size, CancellationToken ct = default);
}

public interface IFileSystem
{
    bool FileExists(string path);
    /// <summary>Size in bytes; throws when the file cannot be read.</summary>
    long GetReadableFileSize(string path);
    /// <summary>The whole content of a small file; throws when it cannot be read or is larger than <paramref name="maxBytes"/>.</summary>
    byte[] ReadSmallFile(string path, int maxBytes);
    /// <summary>Deletes one file; nothing happens when it is already gone.</summary>
    void DeleteFile(string path);
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
