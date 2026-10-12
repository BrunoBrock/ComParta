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
    public const string TransfersList = "transfers.list";
    public const string TransfersPreview = "transfers.preview";
    public const string TransfersReceive = "transfers.receive";
    public const string ClipboardFindInvite = "clipboard.findInvite";
    public const string TransfersRemove = "transfers.remove";
    public const string TransfersOpenFile = "transfers.openFile";
    public const string TransfersPause = "transfers.pause";
    public const string TransfersResume = "transfers.resume";
    public const string ReceivedOpenFolder = "received.openFolder";
    public const string SettingsGet = "settings.get";
    public const string SettingsSet = "settings.set";
    public const string ConnectionGet = "connection.get";
    public const string ConnectionOpenSettings = "connection.openSettings";
    public const string InvitesTakeOpened = "invites.takeOpened";
    public const string ActivityGet = "activity.get";
    public const string ActivitySet = "activity.set";
    public const string SharesDetails = "shares.details";
    public const string TransfersDetails = "transfers.details";
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
    public const string Unexpected = "unexpected";

    public static IReadOnlyList<string> All { get; } =
    [
        ShareDuplicate, ShareNotFound, ShareNotReady, FileUnreadable,
        InviteInvalid, TransferDuplicate, TransferNotFound, TransferFinished, DownloadInvalidFile, DiskFull, SettingsInvalid, Unexpected,
    ];
}

public enum ShareStatusDto { Preparing, Ready, Unavailable }

public enum TransferStateDto { Connecting, Receiving, Paused, Done, Failed }

public enum ConnectionHealthDto { Good, Limited, Blocked }

public enum TransferSourceDto { Invite, DownloadLink, DownloadFile }

public sealed record ShareDto(string Id, string Name, long SizeBytes, ShareStatusDto Status);

/// <param name="BytesPerSecond">Current speed while receiving, else 0.</param>
/// <param name="SecondsLeft">Roughly how long is left, or null when it cannot be told.</param>
public sealed record TransferDto(
    string Id, string? Name, long? SizeBytes, long ReceivedBytes, TransferStateDto State, long BytesPerSecond, long? SecondsLeft);

public sealed record IdArgs(string Id);

public sealed record ReceiveArgs(string Invite);

/// <param name="Message">
/// The words the UI wrote in the person's language, with <c>{link}</c> where the invite goes.
/// Without it, only the link is copied.
/// </param>
public sealed record CopyInviteArgs(string Id, string? Message);

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
public sealed record SettingsDto(string? Language, string SystemLanguage, string? View, string? Sort);

/// <summary>Every choice at once; the UI sends what it has, with the one it changed.</summary>
/// <param name="Language">A language tag, or null to follow the system again.</param>
public sealed record SetSettingsArgs(string? Language, string? View, string? Sort);

/// <param name="Health">How well others can reach this computer (AD-15).</param>
public sealed record ConnectionDto(ConnectionHealthDto Health);

/// <summary>The speed bar: total speeds and the two Stop flags (CAP-27, AD-22).</summary>
public sealed record ActivityDto(long ReceivingBytesPerSecond, long SendingBytesPerSecond, bool SendingStopped, bool ReceivingStopped);

public sealed record SetActivityArgs(bool SendingStopped, bool ReceivingStopped);

/// <summary>The details window of a shared file (CAP-26).</summary>
/// <param name="Folder">The folder the file sits in on this computer.</param>
/// <param name="AddedAt">When it was shared, as an ISO 8601 time.</param>
/// <param name="BytesSent">Sent since the app started.</param>
public sealed record ShareDetailsDto(
    string Id, string Name, long SizeBytes, ShareStatusDto Status, string Folder, string AddedAt,
    long SendingBytesPerSecond, int Connections, long BytesSent);

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
        new(Commands.SharesAdd, null, typeof(ShareDto), ResultMayBeNull: true),
        new(Commands.SharesRemove, typeof(IdArgs), null),
        new(Commands.SharesCopyInvite, typeof(CopyInviteArgs), null),
        new(Commands.TransfersList, null, typeof(TransferDto[])),
        new(Commands.TransfersPreview, typeof(ReceiveArgs), typeof(InvitePreviewDto)),
        new(Commands.TransfersReceive, typeof(ReceiveArgs), typeof(TransferDto)),
        new(Commands.ClipboardFindInvite, null, typeof(string), ResultMayBeNull: true),
        new(Commands.TransfersRemove, typeof(IdArgs), null),
        new(Commands.TransfersOpenFile, null, typeof(TransferDto), ResultMayBeNull: true),
        new(Commands.TransfersPause, typeof(IdArgs), null),
        new(Commands.TransfersResume, typeof(IdArgs), null),
        new(Commands.ReceivedOpenFolder, null, null),
        new(Commands.SettingsGet, null, typeof(SettingsDto)),
        new(Commands.SettingsSet, typeof(SetSettingsArgs), null),
        new(Commands.ConnectionGet, null, typeof(ConnectionDto)),
        new(Commands.ConnectionOpenSettings, null, null),
        new(Commands.InvitesTakeOpened, null, typeof(string), ResultMayBeNull: true),
        new(Commands.ActivityGet, null, typeof(ActivityDto)),
        new(Commands.ActivitySet, typeof(SetActivityArgs), null),
        new(Commands.SharesDetails, typeof(IdArgs), typeof(ShareDetailsDto)),
        new(Commands.TransfersDetails, typeof(IdArgs), typeof(TransferDetailsDto)),
    ];

    public static IReadOnlyList<EventSpec> EventSpecs { get; } =
    [
        new(Events.SharesChanged, typeof(ShareDto[])),
        new(Events.TransfersChanged, typeof(TransferDto[])),
        new(Events.SettingsChanged, typeof(SettingsDto)),
        new(Events.ConnectionChanged, typeof(ConnectionDto)),
        new(Events.InvitesChanged, typeof(bool)),
        new(Events.ActivityChanged, typeof(ActivityDto)),
    ];

    /// <summary>camelCase JSON with enums as camelCase strings, as the conventions require.</summary>
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
