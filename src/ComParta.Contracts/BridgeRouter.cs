using System.Text.Json;
using ComParta.Core;
using ComParta.Core.Ports;
using ComParta.Core.UseCases;

namespace ComParta.Contracts;

/// <summary>
/// Turns a bridge command into a use-case call and core changes into bridge events, mapping core
/// types to the contract's DTOs and error codes. It has no dependency on the shell, so the whole
/// contract can be exercised without a window.
/// </summary>
public sealed class BridgeRouter
{
    private readonly ShareUseCases _shares;
    private readonly TransferUseCases _transfers;
    private readonly IClipboard _clipboard;
    private readonly IContentOpener _opener;

    public BridgeRouter(ShareUseCases shares, TransferUseCases transfers, IClipboard clipboard, IContentOpener opener)
    {
        _shares = shares;
        _transfers = transfers;
        _clipboard = clipboard;
        _opener = opener;
        _shares.Changed += () => Raise(Events.SharesChanged, _shares.Current().Select(ToDto).ToArray());
        _transfers.Changed += () => Raise(Events.TransfersChanged, _transfers.List().Select(ToDto).ToArray());
    }

    /// <summary>An event envelope as JSON, ready to be sent to the UI.</summary>
    public event Action<string>? EventRaised;

    /// <summary>Runs one command and returns its result as JSON. Never throws.</summary>
    public async Task<string> InvokeAsync(string command, string? argsJson)
    {
        CommandResult result;
        try
        {
            result = await RunAsync(command, argsJson);
        }
        catch (Exception)
        {
            result = CommandResult.Failure(ErrorCodes.Unexpected);
        }
        return JsonSerializer.Serialize(result, Contract.Json);
    }

    private async Task<CommandResult> RunAsync(string command, string? argsJson)
    {
        switch (command)
        {
            case Commands.SharesList:
                return CommandResult.Success((await _shares.ListAsync()).Select(ToDto).ToArray());

            case Commands.SharesAdd:
            {
                var added = await _shares.AddAsync();
                return added.Ok ? CommandResult.Success(added.Value is null ? null : ToDto(added.Value)) : Failure(added.Error);
            }

            case Commands.SharesRemove:
                return ShareId.TryParse(Args<IdArgs>(argsJson)?.Id, out var removeId)
                    ? ToResult(await _shares.RemoveAsync(removeId))
                    : CommandResult.Failure(ErrorCodes.ShareNotFound);

            case Commands.SharesCopyInvite:
            {
                var invite = await GetInviteAsync(argsJson);
                if (!invite.Ok)
                    return Failure(invite.Error);
                await _clipboard.SetTextAsync(invite.Value!);
                return CommandResult.Success(null);
            }

            case Commands.TransfersList:
                return CommandResult.Success(_transfers.List().Select(ToDto).ToArray());

            case Commands.TransfersReceive:
            {
                var started = await _transfers.ReceiveAsync(Args<ReceiveArgs>(argsJson)?.Invite);
                return started.Ok ? CommandResult.Success(ToDto(started.Value!)) : Failure(started.Error);
            }

            case Commands.TransfersRemove:
                return TransferId.TryParse(Args<IdArgs>(argsJson)?.Id, out var transferId)
                    ? ToResult(await _transfers.RemoveAsync(transferId))
                    : CommandResult.Failure(ErrorCodes.TransferNotFound);

            case Commands.ReceivedOpenFolder:
                await _opener.OpenReceivedFolderAsync();
                return CommandResult.Success(null);

            default:
                return CommandResult.Failure(ErrorCodes.Unexpected);
        }
    }

    private async Task<Result<string>> GetInviteAsync(string? argsJson) =>
        ShareId.TryParse(Args<IdArgs>(argsJson)?.Id, out var id)
            ? await _shares.GetInviteAsync(id)
            : Result<string>.Fail(ErrorKind.ShareNotFound);

    private void Raise(string name, object payload) =>
        EventRaised?.Invoke(JsonSerializer.Serialize(new EventEnvelope(name, payload), Contract.Json));

    private static T? Args<T>(string? json) where T : class =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json, Contract.Json);

    private static CommandResult ToResult(Result result) =>
        result.Ok ? CommandResult.Success(null) : Failure(result.Error);

    private static CommandResult Failure(ErrorKind? error) => CommandResult.Failure(ToCode(error ?? ErrorKind.Unexpected));

    public static ShareDto ToDto(ShareView view) => new(
        view.Share.Id.ToString(),
        view.Share.Name,
        view.Share.SizeBytes,
        view.Status switch
        {
            ShareStatus.Ready => ShareStatusDto.Ready,
            ShareStatus.Unavailable => ShareStatusDto.Unavailable,
            _ => ShareStatusDto.Preparing,
        });

    public static TransferDto ToDto(TransferView view) => new(
        view.Transfer.Id.ToString(),
        view.Transfer.Name,
        view.Transfer.SizeBytes,
        view.ReceivedBytes,
        view.State switch
        {
            TransferState.Receiving => TransferStateDto.Receiving,
            TransferState.Done => TransferStateDto.Done,
            TransferState.Failed => TransferStateDto.Failed,
            _ => TransferStateDto.Connecting,
        });

    public static string ToCode(ErrorKind error) => error switch
    {
        ErrorKind.ShareDuplicate => ErrorCodes.ShareDuplicate,
        ErrorKind.ShareNotFound => ErrorCodes.ShareNotFound,
        ErrorKind.ShareNotReady => ErrorCodes.ShareNotReady,
        ErrorKind.FileUnreadable => ErrorCodes.FileUnreadable,
        ErrorKind.InviteInvalid => ErrorCodes.InviteInvalid,
        ErrorKind.TransferDuplicate => ErrorCodes.TransferDuplicate,
        ErrorKind.TransferNotFound => ErrorCodes.TransferNotFound,
        _ => ErrorCodes.Unexpected,
    };
}
