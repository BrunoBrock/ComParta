using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComParta.Contracts;

// The bridge between the UI and the app is declared here and nowhere else (AD-3).
// The TypeScript in src/ui/src/bridge/contract.generated.ts is generated from this file.

public static class Commands
{
    public const string SharesList = "shares.list";
    public const string SharesAdd = "shares.add";
    public const string SharesRemove = "shares.remove";
    public const string SharesCopyInvite = "shares.copyInvite";
    public const string SharesSaveDownloadFile = "shares.saveDownloadFile";
    public const string SharesPause = "shares.pause";
    public const string SharesResume = "shares.resume";
    public const string SharesReveal = "shares.reveal";
    public const string SharesAddFolder = "shares.addFolder";
    public const string SharesAddPicked = "shares.addPicked";
    public const string TransfersList = "transfers.list";
    public const string TransfersPreview = "transfers.preview";
    public const string TransfersReceive = "transfers.receive";
    public const string ClipboardFindInvite = "clipboard.findInvite";
    public const string TransfersRemove = "transfers.remove";
    public const string TransfersOpenFile = "transfers.openFile";
    public const string TransfersPause = "transfers.pause";
    public const string TransfersResume = "transfers.resume";
    public const string ReceivedOpenFolder = "received.openFolder";
    public const string ReceivedOpen = "received.open";
    public const string ReceivedReveal = "received.reveal";
    public const string ReceivedDelete = "received.delete";
    public const string SettingsGet = "settings.get";
    public const string SettingsSet = "settings.set";
    public const string SettingsChooseReceivedFolder = "settings.chooseReceivedFolder";
    public const string SettingsUseUsualReceivedFolder = "settings.useUsualReceivedFolder";
    public const string ConnectionGet = "connection.get";
    public const string ConnectionOpenSettings = "connection.openSettings";
    public const string InvitesTakeOpened = "invites.takeOpened";
    public const string ActivityGet = "activity.get";
    public const string ActivitySet = "activity.set";
    public const string SharesDetails = "shares.details";
    public const string TransfersDetails = "transfers.details";
    public const string ThumbnailsGet = "thumbnails.get";
    public const string SharesAddDropped = "shares.addDropped";
    public const string AppQuit = "app.quit";
    public const string MediaOpen = "media.open";
    public const string SupportReport = "support.report";
}

public static class Events
{
    public const string SharesChanged = "shares.changed";
    public const string TransfersChanged = "transfers.changed";
    public const string SettingsChanged = "settings.changed";
    public const string ConnectionChanged = "connection.changed";
    /// <summary>True when an invite opened from outside the app waits; the UI takes it with <c>invites.takeOpened</c>.</summary>
    public const string InvitesChanged = "invites.changed";
    public const string ActivityChanged = "activity.changed";
    /// <summary>The item whose details should show, after the person clicked a system notification (CAP-20).</summary>
    public const string DetailsChanged = "details.changed";
    /// <summary>Quitting from the notification-area icon found files moving; the screen asks first (AD-14).</summary>
    public const string QuitChanged = "quit.changed";
}

/// <summary>Stable codes; the words for them live in the UI's translation catalog (AD-11).</summary>
public static class ErrorCodes
{
    public const string ShareDuplicate = "share.duplicate";
    public const string ShareNotFound = "share.notFound";
    public const string ShareNotReady = "share.notReady";
    public const string FileUnreadable = "file.unreadable";
    public const string InviteInvalid = "invite.invalid";
    public const string TransferDuplicate = "transfer.duplicate";
    public const string TransferNotFound = "transfer.notFound";
    public const string TransferFinished = "transfer.finished";
    public const string DownloadInvalidFile = "download.invalidFile";
    public const string DiskFull = "disk.full";
    public const string SettingsInvalid = "settings.invalid";
    public const string ReceivedNotFound = "received.notFound";
    public const string ReceivedOpenFailed = "received.openFailed";
    public const string ReceivedConfirmRisky = "received.confirmRisky";
    public const string ShareDropFailed = "share.dropFailed";
    public const string FileSaveFailed = "file.saveFailed";
    public const string MediaNotPlayable = "media.notPlayable";
    public const string ShareFolderEmpty = "share.folderEmpty";
    public const string ShareFolderTooLarge = "share.folderTooLarge";
    public const string ShareNameInvalid = "share.nameInvalid";
    public const string ReceivedFolderUnusable = "received.folderUnusable";
    public const string ReceivedFolderMissing = "received.folderMissing";
    public const string Unexpected = "unexpected";

    public static IReadOnlyList<string> All { get; } =
    [
        ShareDuplicate, ShareNotFound, ShareNotReady, FileUnreadable,
        InviteInvalid, TransferDuplicate, TransferNotFound, TransferFinished, DownloadInvalidFile, DiskFull, SettingsInvalid,
        ReceivedNotFound, ReceivedOpenFailed, ReceivedConfirmRisky, ShareDropFailed, FileSaveFailed, MediaNotPlayable,
        ShareFolderEmpty, ShareFolderTooLarge, ShareNameInvalid, ReceivedFolderUnusable, ReceivedFolderMissing, Unexpected,
    ];
}

public enum ShareStatusDto { Preparing, Ready, Unavailable, PartlyUnavailable }

public enum ShareKindDto { File, Folder, Collection }

public enum TransferStateDto { Connecting, Receiving, Paused, Done, Failed, Waiting, NoSpace, Blocked }

public enum ThumbnailKindDto { Share, Transfer }

public enum ItemKindDto { Share, Transfer }

public sealed record DetailsTargetDto(ItemKindDto Kind, string Id, string Name);

public enum MediaKindDto { Picture, Song, Video }

public sealed record MediaArgs(ItemKindDto Kind, string Id);

/// <param name="Url">Where the web view reads the file from; it serves this one file and nothing else (AD-26).</param>
public sealed record MediaDto(string Url, MediaKindDto Kind, string Name);

/// <summary>
/// The address the web view reads opened files from (AD-26). The shell answers requests to it with
/// <see cref="BridgeRouter.ServeMedia"/>; nothing there is ever fetched from a network.
/// </summary>
public static class MediaAddress
{
    public const string Root = "https://media.comparta.invalid/";

    public static string For(string token) => Root + token;

    /// <summary>The token in an address under <see cref="Root"/>, or null for any other address.</summary>
    public static string? TokenOf(string url)
    {
        if (!url.StartsWith(Root, StringComparison.OrdinalIgnoreCase))
            return null;
        var token = url[Root.Length..];
        var end = token.IndexOfAny(['?', '#', '/']);
        return end >= 0 ? token[..end] : token;
    }
}

/// <param name="Size">The picture's longest side in pixels; the app keeps it between 32 and 512.</param>
/// <param name="Item">For a shared folder or several files, which of its photos and videos, from 0 to 3.</param>
public sealed record ThumbnailArgs(ThumbnailKindDto Kind, string Id, int Size, int Item = 0);

/// <summary>Whether another device can reach this computer. The words for it live in the UI (AD-11).</summary>
public enum ConnectionHealthDto { Good, Limited, Blocked }

public enum TransferSourceDto { Invite, DownloadLink, DownloadFile }

/// <param name="Activity">Whether devices are getting the file now, or someone finished since the app started.</param>
/// <param name="ReceiverCount">How many devices are getting it now.</param>
/// <param name="Percent">How much of it they have, on average, 0 to 100.</param>
/// <param name="FinishedCount">How many devices got all of it since the app started.</param>
/// <param name="SecondsLeft">Roughly how long until they have all of it, or null when it cannot be told.</param>
/// <param name="Paused">The person paused sending this file on its own.</param>
public sealed record ShareDto(
    string Id, string Name, long SizeBytes, ShareStatusDto Status,
    ShareActivityDto Activity, int ReceiverCount, int Percent, int FinishedCount, long? SecondsLeft, bool Paused,
    int ItemCount, ShareKindDto Kind);

public enum ShareActivityDto { Idle, Sending, Sent }

/// <param name="BytesPerSecond">Current speed while receiving, else 0.</param>
/// <param name="SecondsLeft">Roughly how long is left, or null when it cannot be told.</param>
public sealed record TransferDto(
    string Id, string? Name, long? SizeBytes, long ReceivedBytes, TransferStateDto State, long BytesPerSecond, long? SecondsLeft,
    long? NeededBytes, bool Risky = false);

public sealed record IdArgs(string Id);

/// <param name="Confirmed">True once the person confirmed opening a file that can run something (AD-13).</param>
public sealed record OpenReceivedArgs(string Id, bool? Confirmed = null);

public sealed record ReceiveArgs(string Invite);

/// <param name="Message">
/// The words the UI wrote in the person's language, with <c>{link}</c> where the invite goes.
/// Without it, only the link is copied.
/// </param>
public sealed record CopyInviteArgs(string Id, string? Message);

/// <param name="Token">The id the UI gave the drop when it handed the files to the window (AD-7).</param>
public sealed record DropArgs(string Token);

/// <summary>What Share files did: the one file shared, or how many chosen together wait for a name.</summary>
public sealed record AddedDto(ShareDto? Share, int FilesToName);

/// <summary>The name for the files chosen together; null forgets them.</summary>
public sealed record AddPickedArgs(string? Name);

/// <param name="Problem">The error code of the first file that could not be shared, or null.</param>
public sealed record DropResultDto(int Added, string? Problem);

/// <summary>
/// What an invite or download link says, before anything is received. Name, size and sender come
/// from the invite and are shown as such; they never become names on disk (AD-16).
/// </summary>
/// <param name="IsInvite">True for a ComParta invite, false for a public download link.</param>
/// <param name="FreeBytes">Room left for received files, or null when it cannot be told.</param>
public sealed record InvitePreviewDto(
    string? SenderName, string? Name, long? SizeBytes, int? ItemCount, bool IsInvite, long? FreeBytes);

/// <param name="Language">The language the person chose, or null to follow the system.</param>
/// <param name="SystemLanguage">The language the operating system shows, as a tag like "pt-BR".</param>
/// <param name="View">"grid" or "list", or null for the default.</param>
/// <param name="Sort">"added", "name" or "size", or null for the default.</param>
/// <param name="StartWithWindows">Start hidden in the notification area when the person signs in.</param>
/// <param name="PublicServers">Addresses of public servers that help others find what is shared; empty for none.</param>
/// <param name="HelperAddress">Where the helper computer takes files; null for none.</param>
/// <param name="HelperKey">The helper computer's key; null for none.</param>
/// <param name="YourName">The name others see in invites; null until the person gives one.</param>
/// <param name="Theme">"light" or "dark", or null to follow the system.</param>
/// <param name="Version">The app's version, for About.</param>
/// <param name="WelcomeDone">The first-run welcome was finished or skipped.</param>
/// <param name="ReceivedFolder">The name of the folder new received files go to, without its path (AD-7).</param>
/// <param name="ReceivedFolderChosen">The person chose that folder; false while it is the usual one.</param>
public sealed record SettingsDto(
    string? Language, string SystemLanguage, string? View, string? Sort, int SendStepsSeen, bool StartWithWindows, string[] PublicServers,
    string? HelperAddress, string? HelperKey, string? YourName, string? Theme, string Version, bool WelcomeDone,
    long? ReceiveLimit, long? SendLimit, string ReceivedFolder, bool ReceivedFolderChosen);

/// <summary>Every choice at once; the UI sends what it has, with the one it changed.</summary>
/// <param name="Language">A language tag, or null to follow the system again.</param>
/// <param name="PublicServers">Addresses of public servers, one per entry; null or empty for none.</param>
/// <param name="HelperAddress">Where the helper computer takes files; null or empty for none. Goes with <paramref name="HelperKey"/>.</param>
/// <param name="HelperKey">The helper computer's key; null or empty for none.</param>
/// <param name="ReceiveLimit">The most received each second, in bytes, for the whole app; null for no limit.</param>
/// <param name="SendLimit">The most sent each second, in bytes, for the whole app; null for no limit.</param>
public sealed record SetSettingsArgs(
    string? Language, string? View, string? Sort, int? SendStepsSeen, bool? StartWithWindows, string[]? PublicServers,
    string? HelperAddress = null, string? HelperKey = null, string? YourName = null, string? Theme = null, bool? WelcomeDone = null,
    long? ReceiveLimit = null, long? SendLimit = null);

/// <param name="Force">True once the person confirmed quitting although files are moving.</param>
public sealed record QuitArgs(bool Force);

/// <summary>Whether the app is quitting, or the names of the files that would stop moving (AD-14).</summary>
public sealed record QuitAnswerDto(bool Quitting, string[] Moving);

/// <param name="Health">Null while nothing is shared or received, or the router has not answered yet.</param>
/// <param name="ThroughHelper">Others get the files through the helper computer (AD-23).</param>
public sealed record ConnectionDto(ConnectionHealthDto? Health, bool ThroughHelper = false);

/// <summary>The speed bar: total speeds and the two Stop flags (CAP-27, AD-22).</summary>
/// <param name="Items">The speeds of each file moving now, for its card; files standing still are left out.</param>
public sealed record ActivityDto(
    long ReceivingBytesPerSecond, long SendingBytesPerSecond, bool SendingStopped, bool ReceivingStopped, ItemSpeedDto[] Items);

/// <param name="Id">A share's or a transfer's id.</param>
public sealed record ItemSpeedDto(string Id, long ReceivingBytesPerSecond, long SendingBytesPerSecond);

public sealed record SetActivityArgs(bool SendingStopped, bool ReceivingStopped);

/// <summary>The details window of a shared file (CAP-26).</summary>
/// <param name="Folder">The folder the file sits in on this computer.</param>
/// <param name="AddedAt">When it was shared, as an ISO 8601 time.</param>
/// <param name="BytesSent">Sent since the app started.</param>
public sealed record ShareDetailsDto(
    string Id, string Name, long SizeBytes, ShareStatusDto Status, string Folder, string AddedAt,
    long SendingBytesPerSecond, int Connections, long BytesSent, int ItemCount, ShareKindDto Kind);

/// <summary>The details window of a received file (CAP-26).</summary>
/// <param name="AddedAt">When receiving started, as an ISO 8601 time.</param>
/// <param name="BytesSent">Sent to others since the app started, for public downloads that share back.</param>
public sealed record TransferDetailsDto(
    string Id, string? Name, long? SizeBytes, long ReceivedBytes, TransferStateDto State, TransferSourceDto Source,
    string AddedAt, long ReceivingBytesPerSecond, long SendingBytesPerSecond, long? SecondsLeft, int Connections, long BytesSent);

public sealed record ErrorDto(string Code);

/// <summary>What every command returns: <c>{ ok, value }</c> or <c>{ ok: false, error: { code } }</c>.</summary>
public sealed record CommandResult(
    bool Ok,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ErrorDto? Error)
{
    public static CommandResult Success(object? value) => new(true, value, null);
    public static CommandResult Failure(string code) => new(false, null, new ErrorDto(code));
}

public sealed record EventEnvelope(string Event, object Payload);

/// <param name="Args">The argument DTO, or null when the command takes none.</param>
/// <param name="Result">The value type, or null when the command returns nothing.</param>
/// <param name="ResultMayBeNull">True when a successful result can be null (the person cancelled).</param>
public sealed record CommandSpec(string Name, Type? Args, Type? Result, bool ResultMayBeNull = false);

public sealed record EventSpec(string Name, Type Payload);

public static class Contract
{
    public static IReadOnlyList<CommandSpec> CommandSpecs { get; } =
    [
        new(Commands.SharesList, null, typeof(ShareDto[])),
        // Several files chosen together come back as a count; the screen asks for their name (AD-19).
        new(Commands.SharesAdd, null, typeof(AddedDto)),
        new(Commands.SharesAddPicked, typeof(AddPickedArgs), typeof(ShareDto), ResultMayBeNull: true),
        new(Commands.SharesAddFolder, null, typeof(ShareDto), ResultMayBeNull: true),
        new(Commands.SharesRemove, typeof(IdArgs), null),
        new(Commands.SharesCopyInvite, typeof(CopyInviteArgs), null),
        // The saved file's name, shown to the person; the folder opens with it selected (AD-18: no path crosses the bridge).
        new(Commands.SharesSaveDownloadFile, typeof(IdArgs), typeof(string)),
        new(Commands.SharesPause, typeof(IdArgs), null),
        new(Commands.SharesResume, typeof(IdArgs), null),
        // Shows the shared file selected in its folder, as received.reveal does for one that arrived.
        new(Commands.SharesReveal, typeof(IdArgs), null),
        new(Commands.TransfersList, null, typeof(TransferDto[])),
        new(Commands.TransfersPreview, typeof(ReceiveArgs), typeof(InvitePreviewDto)),
        new(Commands.TransfersReceive, typeof(ReceiveArgs), typeof(TransferDto)),
        new(Commands.ClipboardFindInvite, null, typeof(string), ResultMayBeNull: true),
        new(Commands.TransfersRemove, typeof(IdArgs), null),
        new(Commands.TransfersOpenFile, null, typeof(TransferDto), ResultMayBeNull: true),
        new(Commands.TransfersPause, typeof(IdArgs), null),
        new(Commands.TransfersResume, typeof(IdArgs), null),
        new(Commands.ReceivedOpenFolder, null, null),
        new(Commands.ReceivedOpen, typeof(OpenReceivedArgs), null),
        new(Commands.ReceivedReveal, typeof(IdArgs), null),
        new(Commands.ReceivedDelete, typeof(IdArgs), null),
        new(Commands.SettingsGet, null, typeof(SettingsDto)),
        new(Commands.SettingsSet, typeof(SetSettingsArgs), null),
        // Opens Windows' folder dialog (AD-7); the name of the folder picked, or null when it was closed.
        // New transfers go there; running and finished ones stay where they are (epic 12 story 2).
        new(Commands.SettingsChooseReceivedFolder, null, typeof(string), ResultMayBeNull: true),
        new(Commands.SettingsUseUsualReceivedFolder, null, null),
        new(Commands.ConnectionGet, null, typeof(ConnectionDto)),
        new(Commands.ConnectionOpenSettings, null, null),
        new(Commands.InvitesTakeOpened, null, typeof(string), ResultMayBeNull: true),
        new(Commands.ActivityGet, null, typeof(ActivityDto)),
        new(Commands.ActivitySet, typeof(SetActivityArgs), null),
        new(Commands.SharesDetails, typeof(IdArgs), typeof(ShareDetailsDto)),
        new(Commands.TransfersDetails, typeof(IdArgs), typeof(TransferDetailsDto)),
        // A data: URL, or null when the file has no picture (AD-18: no path crosses the bridge).
        new(Commands.SharesAddDropped, typeof(DropArgs), typeof(DropResultDto)),
        new(Commands.ThumbnailsGet, typeof(ThumbnailArgs), typeof(string), ResultMayBeNull: true),
        new(Commands.AppQuit, typeof(QuitArgs), typeof(QuitAnswerDto)),
        // Fails with media.notPlayable when the file cannot be shown or played here.
        new(Commands.MediaOpen, typeof(MediaArgs), typeof(MediaDto)),
        // Puts the report for whoever helps the person on the clipboard and returns it, so the Help page shows what was copied.
        new(Commands.SupportReport, null, typeof(string)),
    ];

    public static IReadOnlyList<EventSpec> EventSpecs { get; } =
    [
        new(Events.SharesChanged, typeof(ShareDto[])),
        new(Events.TransfersChanged, typeof(TransferDto[])),
        new(Events.SettingsChanged, typeof(SettingsDto)),
        new(Events.ConnectionChanged, typeof(ConnectionDto)),
        new(Events.InvitesChanged, typeof(bool)),
        new(Events.ActivityChanged, typeof(ActivityDto)),
        new(Events.DetailsChanged, typeof(DetailsTargetDto)),
        new(Events.QuitChanged, typeof(QuitAnswerDto)),
    ];

    /// <summary>camelCase JSON with enums as camelCase strings, as the conventions require.</summary>
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
