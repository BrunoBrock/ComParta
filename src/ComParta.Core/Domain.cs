using System.Net;
using System.Net.Sockets;

namespace ComParta.Core;

public readonly record struct ShareId(Guid Value)
{
    public static ShareId New() => new(Guid.NewGuid());
    public static bool TryParse(string? text, out ShareId id)
    {
        var ok = Guid.TryParse(text, out var value);
        id = new ShareId(value);
        return ok;
    }
    public override string ToString() => Value.ToString("D");
}

public readonly record struct TransferId(Guid Value)
{
    public static TransferId New() => new(Guid.NewGuid());
    public static bool TryParse(string? text, out TransferId id)
    {
        var ok = Guid.TryParse(text, out var value);
        id = new TransferId(value);
        return ok;
    }
    public override string ToString() => Value.ToString("D");
}

/// <summary>
/// What a person offers: one file, a folder, or several files under one name (AD-19). The files
/// stay where they are (AD-6).
/// </summary>
/// <param name="FilePath">The file, or the folder the items are in.</param>
/// <param name="SizeBytes">All the items together.</param>
/// <param name="Paused">The person paused sending this share on its own; it stays paused across restarts.</param>
/// <param name="Items">What the share holds; one item, the file itself, when left out.</param>
public sealed record Share(
    ShareId Id, string FilePath, string Name, long SizeBytes, DateTimeOffset AddedAt, bool Paused = false,
    IReadOnlyList<ShareItem>? Items = null, ShareKind Kind = ShareKind.File)
{
    public IReadOnlyList<ShareItem> Items { get; init; } = Items is { Count: > 0 } ? Items : [new ShareItem(Name, SizeBytes, FilePath)];

    // Two shares are the same when their items are, not only the same list.
    public bool Equals(Share? other) =>
        other is not null && Id == other.Id && FilePath == other.FilePath && Name == other.Name && SizeBytes == other.SizeBytes
        && AddedAt == other.AddedAt && Paused == other.Paused && Kind == other.Kind && Items.SequenceEqual(other.Items);

    public override int GetHashCode() => HashCode.Combine(Id, FilePath, Name, SizeBytes, AddedAt, Paused, Kind, Items.Count);
}

/// <summary>One file in a share (AD-19).</summary>
/// <param name="RelativePath">Where it goes under the share's name on the other computer, with / between folders.</param>
/// <param name="ContentRef">Where the file is read from; only the shell understands it (AD-20).</param>
public sealed record ShareItem(string RelativePath, long SizeBytes, string ContentRef);

public enum ShareKind
{
    File,
    Folder,
    /// <summary>Several loose files under a name the person gave.</summary>
    Collection,
}

public enum ShareStatus
{
    /// <summary>The file is being read so it can be offered.</summary>
    Preparing,
    /// <summary>The file is offered and an invite can be given out.</summary>
    Ready,
    /// <summary>The file was moved, renamed, deleted or changed outside the app; for a folder, all of its files.</summary>
    Unavailable,
    /// <summary>Some of a folder's files were moved, renamed or deleted; it is not offered until they are back.</summary>
    PartlyUnavailable,
}

public sealed record ShareView(Share Share, ShareStatus Status);

/// <summary>
/// A file on its way into the Received folder, from another person's device or from a public
/// download link or download file (AD-21).
/// </summary>
/// <param name="SkippedFiles">For a download of several files: the positions of the ones the person chose not to get; null for none.</param>
/// <param name="KeepSharing">For a public download: whether it is shared with others once it has arrived, while ComParta is open.</param>
/// <param name="Folder">
/// The Received folder it goes to, as chosen in Settings when it started, or null for the usual one
/// (<see cref="Ports.IAppPaths.ReceivedRoot"/>). Choosing another folder later never moves it (epic 12 story 2).
/// </param>
/// <param name="Check">Once it has arrived: what the system's protection said about it (AD-13).</param>
public sealed record Transfer(
    TransferId Id, string? Name, long? SizeBytes, bool Done, DateTimeOffset AddedAt, bool Paused = false,
    TransferSource Source = TransferSource.Invite, IReadOnlyList<int>? SkippedFiles = null, bool KeepSharing = true,
    string? Folder = null, ReceivedCheck Check = ReceivedCheck.Pending)
{
    /// <summary>Public downloads share back what they received, unless the person said not to (epic 14 story 4).</summary>
    public bool SharesWhenDone => Source != TransferSource.Invite && KeepSharing;
}

/// <summary>
/// What became of handing an arrived transfer's files to the system's protection (AD-13). Kept, so a
/// restart neither checks nor announces it again.
/// </summary>
public enum ReceivedCheck
{
    /// <summary>Not arrived yet, or arrived and not checked yet.</summary>
    Pending = 0,
    Passed = 1,
    /// <summary>The protection removed or refused a file; it shows as <see cref="TransferState.Blocked"/>.</summary>
    Blocked = 2,
}

/// <summary>Where a transfer came from, for the details window (CAP-26).</summary>
public enum TransferSource
{
    /// <summary>A ComParta invite from a person.</summary>
    Invite,
    /// <summary>A public download link the person pasted.</summary>
    DownloadLink,
    /// <summary>A download file the person opened.</summary>
    DownloadFile,
}

public enum TransferState
{
    /// <summary>Looking for the other person's device.</summary>
    Connecting,
    Receiving,
    /// <summary>The person paused it; nothing moves until they continue it.</summary>
    Paused,
    Done,
    /// <summary>It cannot continue; the person removes it and asks for a new invite.</summary>
    Failed,
    /// <summary>No connection to the other device for a while; it continues by itself when one comes back.</summary>
    Waiting,
    /// <summary>The computer ran out of room for it; it continues by itself once there is room.</summary>
    NoSpace,
    /// <summary>The antivirus stopped the file (AD-13). Final, like <see cref="Failed"/>.</summary>
    Blocked,
}

/// <summary>
/// What the person sees before agreeing to receive: the hints, whether they come from a ComParta
/// invite, and the room left for received files.
/// </summary>
public sealed record InvitePreview(InviteHints Hints, bool IsInvite, long? FreeBytes)
{
    /// <summary>True when the size is known and larger than the room left.</summary>
    public bool NotEnoughSpace => Hints.SizeBytes is { } size && FreeBytes is { } free && size > free;
}

/// <summary>The choices under "More options" on a download (epic 14 story 4).</summary>
/// <param name="Files">The files it holds, or null while they are not known yet or once it has arrived.</param>
/// <param name="Sharing">It has arrived and others can get it from this computer now.</param>
public sealed record TransferOptions(IReadOnlyList<ReceiveFile>? Files, bool KeepSharing, bool Sharing);

/// <summary>One file of a download, as the person picks it (epic 14 story 4).</summary>
/// <param name="Index">Its position in the download, which the choice is kept by.</param>
/// <param name="Path">Its place inside the download, made safe to show; never a path on this computer.</param>
public sealed record ReceiveFile(int Index, string Path, long SizeBytes, bool Wanted);

/// <param name="SecondsLeft">Roughly how long is left, from a smoothed speed, or null when it cannot be told.</param>
/// <param name="NeededBytes">With <see cref="TransferState.NoSpace"/>: how much room to free, or null when it cannot be told.</param>
public sealed record TransferView(
    Transfer Transfer, TransferState State, long ReceivedBytes, long BytesPerSecond = 0, long? SecondsLeft = null,
    long? NeededBytes = null);

/// <summary>
/// The one place network behaviour is decided (AD-8). Only the engine adapter applies it.
/// A <see cref="ListenPort"/> of 0 lets the engine choose a port and keep it between runs.
/// </summary>
public sealed record SharingPolicy(
    bool AllowLocalDiscovery,
    bool AllowPublicLookup,
    bool AllowPeerExchange,
    bool AllowAnnounceServers,
    bool AllowPortForwarding,
    bool AllowAddressInInvite,
    bool RequireEncryption,
    int ListenPort,
    bool AllowServersInDownloadLinks = false)
{
    /// <summary>
    /// Devices find each other on the local network and through the public lookup network,
    /// using only the invite. The invite also carries this device's own network address so the
    /// other device can reach it straight away. A public download link or download file the
    /// person brings keeps the servers it names (AD-8, amended for CAP-25). Nothing else is
    /// switched on.
    /// </summary>
    public static SharingPolicy Default { get; } = new(
        AllowLocalDiscovery: true,
        AllowPublicLookup: true,
        AllowPeerExchange: false,
        AllowAnnounceServers: false,
        AllowPortForwarding: true,
        AllowAddressInInvite: true,
        RequireEncryption: true,
        ListenPort: 0,
        AllowServersInDownloadLinks: true);

    /// <summary>No discovery at all; used by tests that connect two devices by hand.</summary>
    public static SharingPolicy Closed { get; } = new(false, false, false, false, false, false, true, 0);
}

/// <summary>
/// How likely it is that another device can reach this computer. There is no value for "not
/// measured": while nothing is shared or received the health is simply absent (null).
/// </summary>
public enum ConnectionHealth
{
    /// <summary>Another device can reach this computer.</summary>
    Good,
    /// <summary>Only some other devices can, for example when the router shares one address among homes.</summary>
    Limited,
    /// <summary>Nothing can reach this computer from outside.</summary>
    Blocked,
}

/// <summary>What became of asking the router to let connections in.</summary>
public enum PortMappingState
{
    /// <summary>The policy does not ask the router, or nothing has been asked yet.</summary>
    NotRequested,
    /// <summary>The router has not answered yet.</summary>
    Pending,
    /// <summary>The router agreed.</summary>
    Obtained,
    /// <summary>The router said no, or nothing answered within thirty seconds.</summary>
    Refused,
}

/// <summary>
/// What the engine observes about being reachable, with no judgement attached (AD-15). Core turns
/// these into a <see cref="ConnectionHealth"/>; the engine never decides what they mean.
/// </summary>
/// <param name="Listening">The engine accepts connections on its port.</param>
/// <param name="ListenPortBusy">Something else already uses the port, so nothing can connect.</param>
/// <param name="PortMapping">What became of asking the router.</param>
/// <param name="ExternalPort">The port the router opened to the outside, when it agreed.</param>
/// <param name="ExternalAddress">The router's address as seen from the internet; null when not read yet.</param>
/// <param name="GlobalIPv6Present">This computer has an internet-wide IPv6 address.</param>
/// <param name="ListeningOnIPv6">The engine accepts connections on IPv6 too.</param>
/// <param name="IncomingConnectionSeenRecently">Another device from outside the local network connected within the last hour.</param>
/// <param name="ReachableThroughHelper">
/// A helper computer the person named answers and holds every file that is ready (AD-23), so others
/// get them from it whether or not they can reach this computer.
/// </param>
public sealed record ReachabilityFacts(
    bool Listening,
    bool ListenPortBusy,
    PortMappingState PortMapping,
    int? ExternalPort,
    IPAddress? ExternalAddress,
    bool GlobalIPv6Present,
    bool ListeningOnIPv6,
    bool IncomingConnectionSeenRecently,
    bool ReachableThroughHelper = false);

/// <summary>
/// A shared file's download file (AD-21), for apps that do not open links. <see cref="Name"/>
/// is a plain file name, extension included, that names the share.
/// </summary>
public sealed record DownloadFile(string Name, byte[] Content);

/// <summary>
/// A helper computer the person named (AD-23): an always-reachable computer that keeps a copy of
/// each shared file and passes it on to anyone with the invite.
/// </summary>
/// <param name="Address">Where the helper takes files, such as https://helper.example.org:6970.</param>
/// <param name="Key">The secret the helper asks for before it takes a file.</param>
public sealed record HelperComputer(Uri Address, string Key);

/// <summary>Which addresses lead outside the local network.</summary>
public static class NetworkAddresses
{
    /// <summary>
    /// True for an address that anyone on the internet can use to reach a device. False for this
    /// computer itself, home and company networks (10/8, 172.16/12, 192.168/16), the range
    /// internet providers share among homes (100.64/10), self-assigned addresses and addresses
    /// that are not for one device. IPv6 counts when it is an internet-wide unicast address.
    /// </summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] switch
            {
                0 or 10 or 127 => false,
                >= 224 => false,
                100 => bytes[1] is < 64 or > 127,
                169 => bytes[1] != 254,
                172 => bytes[1] is < 16 or > 31,
                192 => bytes[1] != 168,
                _ => true,
            };
        }

        // Internet-wide IPv6 addresses are handed out from 2000::/3.
        return address.AddressFamily == AddressFamily.InterNetworkV6 && (bytes[0] & 0xE0) == 0x20;
    }
}

public enum ErrorKind
{
    ShareDuplicate,
    ShareNotFound,
    ShareNotReady,
    FileUnreadable,
    InviteInvalid,
    TransferDuplicate,
    TransferNotFound,
    DownloadFileInvalid,
    TransferFinished,
    DiskFull,
    SettingsInvalid,
    ReceivedNotFound,
    ReceivedOpenFailed,
    /// <summary>The received file can run something; it opens only once the person confirms again.</summary>
    ReceivedConfirmRisky,
    ShareDropFailed,
    /// <summary>A file the person asked to save could not be written.</summary>
    SaveFailed,
    /// <summary>The file cannot be shown or played inside ComParta; the person may open its folder instead.</summary>
    NotPlayable,
    /// <summary>A folder with no files in it, once hidden ones are left out.</summary>
    FolderEmpty,
    /// <summary>A folder with more files than one share can hold.</summary>
    FolderTooLarge,
    /// <summary>The name given to several files shared together is empty, too long or has characters a folder cannot have.</summary>
    ShareNameInvalid,
    /// <summary>Every file of a download was left out; at least one must stay.</summary>
    NoFilesChosen,
    /// <summary>The folder the person picked for received files cannot hold them: ComParta cannot write there, or it is ComParta's own.</summary>
    ReceivedFolderUnusable,
    /// <summary>The folder chosen for received files is not there now, for example on a drive that was unplugged.</summary>
    ReceivedFolderMissing,
    Unexpected,
}

/// <summary>What the file dialog gave: one file shared at once, or several that wait for a name.</summary>
/// <param name="FilesToName">How many files wait for a name; 0 when nothing waits.</param>
public sealed record AddedShare(ShareView? Share, int FilesToName = 0);

public readonly record struct Result(ErrorKind? Error)
{
    public bool Ok => Error is null;
    public static Result Success { get; } = new((ErrorKind?)null);
    public static Result Fail(ErrorKind error) => new(error);
}

public readonly record struct Result<T>(T? Value, ErrorKind? Error)
{
    public bool Ok => Error is null;
    public static Result<T> Success(T value) => new(value, null);
    public static Result<T> Fail(ErrorKind error) => new(default, error);
}
