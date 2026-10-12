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
    public const string TransfersReceive = "transfers.receive";
    public const string TransfersRemove = "transfers.remove";
    public const string TransfersOpenFile = "transfers.openFile";
    public const string TransfersPause = "transfers.pause";
    public const string TransfersResume = "transfers.resume";
    public const string ReceivedOpenFolder = "received.openFolder";
    public const string ConnectionGet = "connection.get";
}

public static class Events
{
    public const string SharesChanged = "shares.changed";
    public const string TransfersChanged = "transfers.changed";
    public const string ConnectionChanged = "connection.changed";
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
    public const string Unexpected = "unexpected";

    public static IReadOnlyList<string> All { get; } =
    [
        ShareDuplicate, ShareNotFound, ShareNotReady, FileUnreadable,
        InviteInvalid, TransferDuplicate, TransferNotFound, TransferFinished, DownloadInvalidFile, Unexpected,
    ];
}

public enum ShareStatusDto { Preparing, Ready, Unavailable }

public enum TransferStateDto { Connecting, Receiving, Paused, Done, Failed }

/// <summary>Whether another device can reach this computer. The words for it live in the UI (AD-11).</summary>
public enum ConnectionHealthDto { Good, Limited, Blocked }

/// <param name="Health">Null while nothing is shared or received, or the router has not answered yet.</param>
public sealed record ConnectionDto(ConnectionHealthDto? Health);

public sealed record ShareDto(string Id, string Name, long SizeBytes, ShareStatusDto Status);

/// <param name="BytesPerSecond">Current speed while receiving, else 0.</param>
/// <param name="SecondsLeft">Roughly how long is left, or null when it cannot be told.</param>
public sealed record TransferDto(
    string Id, string? Name, long? SizeBytes, long ReceivedBytes, TransferStateDto State, long BytesPerSecond, long? SecondsLeft);

public sealed record IdArgs(string Id);

public sealed record ReceiveArgs(string Invite);

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
        new(Commands.SharesCopyInvite, typeof(IdArgs), null),
        new(Commands.TransfersList, null, typeof(TransferDto[])),
        new(Commands.TransfersReceive, typeof(ReceiveArgs), typeof(TransferDto)),
        new(Commands.TransfersRemove, typeof(IdArgs), null),
        new(Commands.TransfersOpenFile, null, typeof(TransferDto), ResultMayBeNull: true),
        new(Commands.TransfersPause, typeof(IdArgs), null),
        new(Commands.TransfersResume, typeof(IdArgs), null),
        new(Commands.ReceivedOpenFolder, null, null),
        new(Commands.ConnectionGet, null, typeof(ConnectionDto)),
    ];

    public static IReadOnlyList<EventSpec> EventSpecs { get; } =
    [
        new(Events.SharesChanged, typeof(ShareDto[])),
        new(Events.TransfersChanged, typeof(TransferDto[])),
        new(Events.ConnectionChanged, typeof(ConnectionDto)),
    ];

    /// <summary>camelCase JSON with enums as camelCase strings, as the conventions require.</summary>
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
