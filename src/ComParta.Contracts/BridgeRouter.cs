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
    private readonly SettingsUseCases _settings;
    private readonly ConnectionUseCases _connection;
    private readonly OpenedInvites _opened;
    private readonly ActivityUseCases _activity;
    private readonly ReceivedUseCases _received;
    private readonly IClipboard _clipboard;
    private readonly IContentOpener _opener;
    private readonly ThumbnailUseCases _thumbnails;
    private readonly DroppedFiles _dropped;
    private readonly SendingUseCases _sending;
    private readonly BackgroundUseCases _background;
    private readonly MediaUseCases _media;
    private readonly SupportUseCases _support;
    private readonly AboutUseCases _about;

    public BridgeRouter(
        ShareUseCases shares,
        TransferUseCases transfers,
        SettingsUseCases settings,
        ConnectionUseCases connection,
        OpenedInvites opened,
        ActivityUseCases activity,
        ReceivedUseCases received,
        IClipboard clipboard,
        IContentOpener opener,
        ThumbnailUseCases thumbnails,
        DroppedFiles dropped,
        SendingUseCases sending,
        NotificationUseCases notifications,
        BackgroundUseCases background,
        MediaUseCases media,
        SupportUseCases support,
        AboutUseCases about)
    {
        _media = media;
        _support = support;
        _about = about;
        _background = background;
        _background.QuitAsked += answer => Raise(Events.QuitChanged, ToDto(answer));
        _background.BackAsked += () => Raise(Events.BackChanged, true);
        notifications.OpenRequested += (kind, id, name) => Raise(
            Events.DetailsChanged,
            new DetailsTargetDto(kind == NotifiedItem.Share ? ItemKindDto.Share : ItemKindDto.Transfer, id, name));
        _sending = sending;
        _thumbnails = thumbnails;
        _dropped = dropped;
        _shares = shares;
        _transfers = transfers;
        _settings = settings;
        _connection = connection;
        _opened = opened;
        _activity = activity;
        _received = received;
        _clipboard = clipboard;
        _opener = opener;
        _shares.Changed += RaiseShares;
        _sending.Changed += RaiseShares;
        _transfers.Changed += () => Raise(Events.TransfersChanged, _transfers.List().Select(TransferRow).ToArray());
        _settings.Changed += () => _ = RaiseSettingsAsync();
        _connection.Changed += () => Raise(Events.ConnectionChanged, ToDto(_connection.Current(), _connection.ThroughHelper()));
        _opened.Opened += () => Raise(Events.InvitesChanged, true);
        _activity.Changed += activityNow => Raise(Events.ActivityChanged, ToDto(activityNow));
    }

    /// <summary>An event envelope as JSON, ready to be sent to the UI.</summary>
    public event Action<string>? EventRaised;

    /// <summary>A command failed unexpectedly; the UI only hears "unexpected", so this is where the cause goes.</summary>
    public event Action<string, Exception>? CommandFailed;

    /// <summary>
    /// The web view's request for an opened file (AD-26): the address it asked for and its Range
    /// header, if any. Any address that is not an opened file gets 404.
    /// </summary>
    public MediaReply ServeMedia(string url, string? range) =>
        MediaAddress.TokenOf(url) is { } token ? _media.Serve(token, range) : new MediaReply(404, null, 0, null, null);

    /// <summary>Runs one command and returns its result as JSON. Never throws.</summary>
    public async Task<string> InvokeAsync(string command, string? argsJson)
    {
        CommandResult result;
        try
        {
            result = await RunAsync(command, argsJson);
        }
        catch (Exception e)
        {
            result = CommandResult.Failure(ErrorCodes.Unexpected);
            try { CommandFailed?.Invoke(command, e); }
            catch (Exception) { /* a listener's problem must not change the answer */ }
        }
        if (result.Error is { } error)
        {
            // Written down for the support report; failing to write it must not change the answer.
            try { await _support.RecordAsync(error.Code); }
            catch (Exception e)
            {
                try { CommandFailed?.Invoke(command, e); }
                catch (Exception) { /* as above */ }
            }
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
                return added.Ok
                    ? CommandResult.Success(new AddedDto(added.Value!.Share is { } one ? ToDto(one) : null, added.Value.FilesToName))
                    : Failure(added.Error);
            }

            case Commands.SharesAddPicked:
            {
                var added = await _shares.AddPickedAsync(Args<AddPickedArgs>(argsJson)?.Name);
                return added.Ok ? CommandResult.Success(added.Value is null ? null : ToDto(added.Value)) : Failure(added.Error);
            }

            case Commands.SharesAddFolder:
            {
                var added = await _shares.AddFolderAsync();
                return added.Ok ? CommandResult.Success(added.Value is null ? null : ToDto(added.Value)) : Failure(added.Error);
            }

            case Commands.SharesRemove:
                return ShareId.TryParse(Args<IdArgs>(argsJson)?.Id, out var removeId)
                    ? ToResult(await _shares.RemoveAsync(removeId))
                    : CommandResult.Failure(ErrorCodes.ShareNotFound);

            case Commands.SharesPause:
                return ShareId.TryParse(Args<IdArgs>(argsJson)?.Id, out var pauseShareId)
                    ? ToResult(await _shares.PauseAsync(pauseShareId))
                    : CommandResult.Failure(ErrorCodes.ShareNotFound);

            case Commands.SharesResume:
                return ShareId.TryParse(Args<IdArgs>(argsJson)?.Id, out var resumeShareId)
                    ? ToResult(await _shares.ContinueAsync(resumeShareId))
                    : CommandResult.Failure(ErrorCodes.ShareNotFound);

            case Commands.SharesReveal:
            {
                var shown = ShareId.TryParse(Args<IdArgs>(argsJson)?.Id, out var revealShareId)
                    ? _shares.Current().FirstOrDefault(v => v.Share.Id == revealShareId)
                    : null;
                if (shown is null)
                    return CommandResult.Failure(ErrorCodes.ShareNotFound);
                await _opener.RevealAsync(shown.Share.FilePath);
                return CommandResult.Success(null);
            }

            case Commands.SharesCopyInvite:
            {
                var args = Args<CopyInviteArgs>(argsJson);
                var invite = ShareId.TryParse(args?.Id, out var shareId)
                    ? await _shares.GetInviteAsync(shareId)
                    : Result<string>.Fail(ErrorKind.ShareNotFound);
                if (!invite.Ok)
                    return Failure(invite.Error);
                await _clipboard.SetTextAsync(ComposeMessage(args!.Message, invite.Value!));
                return CommandResult.Success(null);
            }

            case Commands.SharesSaveDownloadFile:
            {
                var saved = ShareId.TryParse(Args<IdArgs>(argsJson)?.Id, out var saveShareId)
                    ? await _shares.SaveDownloadFileAsync(saveShareId)
                    : Result<string>.Fail(ErrorKind.ShareNotFound);
                if (!saved.Ok)
                    return Failure(saved.Error);
                await _opener.RevealAsync(saved.Value!);
                return CommandResult.Success(Path.GetFileName(saved.Value!));
            }

            case Commands.TransfersPreview:
            {
                var preview = _transfers.Preview(Args<ReceiveArgs>(argsJson)?.Invite);
                return preview.Ok ? CommandResult.Success(ToDto(preview.Value!)) : Failure(preview.Error);
            }

            case Commands.ClipboardFindInvite:
                return CommandResult.Success(_transfers.FindInvite(await _clipboard.GetTextAsync()));

            case Commands.TransfersList:
                return CommandResult.Success(_transfers.List().Select(TransferRow).ToArray());

            case Commands.TransfersReceive:
            {
                var started = await _transfers.ReceiveAsync(Args<ReceiveArgs>(argsJson)?.Invite);
                return started.Ok ? CommandResult.Success(ToDto(started.Value!)) : Failure(started.Error);
            }

            case Commands.TransfersRemove:
                return TransferId.TryParse(Args<IdArgs>(argsJson)?.Id, out var transferId)
                    ? ToResult(await _transfers.RemoveAsync(transferId))
                    : CommandResult.Failure(ErrorCodes.TransferNotFound);

            case Commands.TransfersOpenFile:
            {
                var opened = await _transfers.OpenFileAsync();
                return opened.Ok ? CommandResult.Success(opened.Value is null ? null : ToDto(opened.Value)) : Failure(opened.Error);
            }

            case Commands.TransfersPause:
                return TransferId.TryParse(Args<IdArgs>(argsJson)?.Id, out var pauseId)
                    ? ToResult(await _transfers.PauseAsync(pauseId))
                    : CommandResult.Failure(ErrorCodes.TransferNotFound);

            case Commands.TransfersResume:
                return TransferId.TryParse(Args<IdArgs>(argsJson)?.Id, out var resumeId)
                    ? ToResult(await _transfers.ContinueAsync(resumeId))
                    : CommandResult.Failure(ErrorCodes.TransferNotFound);

            case Commands.TransfersOptions:
            {
                if (!TransferId.TryParse(Args<IdArgs>(argsJson)?.Id, out var optionsId))
                    return CommandResult.Failure(ErrorCodes.TransferNotFound);
                var options = _transfers.GetOptions(optionsId);
                return options.Ok ? CommandResult.Success(ToDto(options.Value!)) : Failure(options.Error);
            }

            case Commands.TransfersSetOptions:
            {
                var args = Args<SetTransferOptionsArgs>(argsJson);
                return TransferId.TryParse(args?.Id, out var setId)
                    ? ToResult(await _transfers.SetOptionsAsync(setId, args!.Skipped, args.KeepSharing))
                    : CommandResult.Failure(ErrorCodes.TransferNotFound);
            }

            case Commands.TransfersTakeOpenedFile:
            {
                if (_opened.TakeFile() is not { } openedPath)
                    return CommandResult.Success(null);
                var preview = _transfers.PreviewOpenedFile(openedPath);
                return preview.Ok ? CommandResult.Success(ToDto(preview.Value!)) : Failure(preview.Error);
            }

            case Commands.TransfersReceiveOpenedFile:
            {
                var started = await _transfers.ReceiveOpenedFileAsync();
                return started.Ok ? CommandResult.Success(ToDto(started.Value!)) : Failure(started.Error);
            }

            case Commands.ReceivedOpenFolder:
                // The folder new files go to; files received before a change stay in their own.
                await _opener.OpenReceivedFolderAsync(_settings.ChosenReceivedFolder);
                return CommandResult.Success(null);

            case Commands.ReceivedOpen:
            {
                var open = Args<OpenReceivedArgs>(argsJson);
                return TransferId.TryParse(open?.Id, out var openId)
                    ? ToResult(await _received.OpenAsync(openId, open?.Confirmed ?? false))
                    : CommandResult.Failure(ErrorCodes.TransferNotFound);
            }

            case Commands.ReceivedFiles:
            {
                var listed = TransferId.TryParse(Args<IdArgs>(argsJson)?.Id, out var folderId)
                    ? _received.ListFiles(folderId)
                    : Result<IReadOnlyList<ReceivedFile>>.Fail(ErrorKind.TransferNotFound);
                return listed.Ok
                    ? CommandResult.Success(listed.Value!.Select(f => new ReceivedFileDto(f.RelativePath, f.SizeBytes)).ToArray())
                    : Failure(listed.Error);
            }

            case Commands.ReceivedReveal:
                return TransferId.TryParse(Args<IdArgs>(argsJson)?.Id, out var revealId)
                    ? ToResult(await _received.RevealAsync(revealId))
                    : CommandResult.Failure(ErrorCodes.TransferNotFound);

            case Commands.ReceivedDelete:
                return TransferId.TryParse(Args<IdArgs>(argsJson)?.Id, out var deleteId)
                    ? ToResult(await _received.DeleteAsync(deleteId))
                    : CommandResult.Failure(ErrorCodes.TransferNotFound);

            case Commands.AppQuit:
                return CommandResult.Success(ToDto(_background.Quit(Args<QuitArgs>(argsJson)?.Force ?? false)));

            case Commands.SupportReport:
            {
                var report = await _support.ReportAsync();
                await _clipboard.SetTextAsync(report);
                return CommandResult.Success(report);
            }

            case Commands.AboutNotices:
                return await _about.NoticesAsync() is { } notices
                    ? CommandResult.Success(notices)
                    : CommandResult.Failure(ErrorCodes.Unexpected);

            case Commands.AppLeave:
                _background.Leave();
                return CommandResult.Success(null);

            case Commands.SettingsGet:
                return CommandResult.Success(ToDto(await _settings.GetAsync()));

            case Commands.SettingsSet:
            {
                var args = Args<SetSettingsArgs>(argsJson);
                return ToResult(await _settings.SetAsync(args?.Language, args?.View, args?.Sort, args?.SendStepsSeen ?? 0, args?.StartWithWindows ?? false,
                    args?.PublicServers, args?.HelperAddress, args?.HelperKey, args?.YourName, args?.Theme, args?.WelcomeDone ?? false,
                    args?.ReceiveLimit, args?.SendLimit));
            }

            case Commands.SettingsChooseReceivedFolder:
            {
                var chosen = await _settings.ChooseReceivedFolderAsync();
                return chosen.Ok ? CommandResult.Success(chosen.Value) : Failure(chosen.Error);
            }

            case Commands.SettingsUseUsualReceivedFolder:
                return ToResult(await _settings.UseUsualReceivedFolderAsync());

            case Commands.ConnectionGet:
                return CommandResult.Success(ToDto(_connection.Refresh(), _connection.ThroughHelper()));

            case Commands.ConnectionOpenSettings:
                await _connection.OpenSettingsAsync();
                return CommandResult.Success(null);

            case Commands.InvitesTakeOpened:
                // A message shared from another app with no invite in it is said plainly (epic 7 story 6).
                return _opened.TakeNoInvite()
                    ? Failure(ErrorKind.InviteNotInMessage)
                    : CommandResult.Success(_opened.Take());

            case Commands.ActivityGet:
                return CommandResult.Success(ToDto(_activity.Current()));

            case Commands.ActivitySet:
            {
                var args = Args<SetActivityArgs>(argsJson);
                await _activity.SetStoppedAsync(args?.SendingStopped ?? false, args?.ReceivingStopped ?? false);
                return CommandResult.Success(null);
            }

            case Commands.SharesDetails:
            {
                var share = ShareId.TryParse(Args<IdArgs>(argsJson)?.Id, out var detailsId)
                    ? _shares.Current().FirstOrDefault(v => v.Share.Id == detailsId)
                    : null;
                return share is null
                    ? CommandResult.Failure(ErrorCodes.ShareNotFound)
                    : CommandResult.Success(ToDetails(share, _activity.For(share.Share.Id)));
            }

            case Commands.TransfersDetails:
            {
                var transfer = TransferId.TryParse(Args<IdArgs>(argsJson)?.Id, out var detailsId)
                    ? _transfers.List().FirstOrDefault(v => v.Transfer.Id == detailsId)
                    : null;
                return transfer is null
                    ? CommandResult.Failure(ErrorCodes.TransferNotFound)
                    : CommandResult.Success(ToDetails(transfer, _activity.For(transfer.Transfer.Id)));
            }

            case Commands.SharesAddDropped:
            {
                if (!Guid.TryParse(Args<DropArgs>(argsJson)?.Token, out var token))
                    return CommandResult.Failure(ErrorCodes.ShareDropFailed);
                var paths = await _dropped.TakeAsync(token, DroppedFiles.Wait);
                if (paths is null)
                    return CommandResult.Failure(ErrorCodes.ShareDropFailed);
                var (added, problem) = await _shares.AddDroppedAsync(paths);
                return CommandResult.Success(new DropResultDto(added, problem is { } kind ? ToCode(kind) : null));
            }

            case Commands.MediaOpen:
            {
                var args = Args<MediaArgs>(argsJson);
                var opened = _media.Open(args?.Kind == ItemKindDto.Share ? MediaSource.Share : MediaSource.Received, args?.Id);
                if (!opened.Ok)
                    return Failure(opened.Error);
                var item = opened.Value!;
                var kind = item.Kind switch
                {
                    MediaKind.Picture => MediaKindDto.Picture,
                    MediaKind.Song => MediaKindDto.Song,
                    _ => MediaKindDto.Video,
                };
                return CommandResult.Success(new MediaDto(MediaAddress.For(item.Token), kind, item.Name));
            }

            case Commands.ThumbnailsGet:
            {
                var args = Args<ThumbnailArgs>(argsJson);
                Result<Thumbnail?> found;
                if (args?.Kind == ThumbnailKindDto.Share)
                {
                    if (!ShareId.TryParse(args.Id, out var pictureShareId))
                        return CommandResult.Failure(ErrorCodes.ShareNotFound);
                    found = await _thumbnails.ForShareAsync(pictureShareId, args.Size, args.Item);
                }
                else
                {
                    if (!TransferId.TryParse(args?.Id, out var pictureTransferId))
                        return CommandResult.Failure(ErrorCodes.TransferNotFound);
                    found = await _thumbnails.ForTransferAsync(pictureTransferId, args!.Size);
                }
                if (!found.Ok)
                    return Failure(found.Error);
                return CommandResult.Success(found.Value is { } picture
                    ? $"data:{picture.ContentType};base64,{Convert.ToBase64String(picture.Bytes)}"
                    : null);
            }

            default:
                return CommandResult.Failure(ErrorCodes.Unexpected);
        }
    }

    /// <summary>The UI's message with the link put in, or the link alone.</summary>
    private static string ComposeMessage(string? message, string link)
    {
        const string placeholder = "{link}";
        if (string.IsNullOrWhiteSpace(message))
            return link;
        return message.Contains(placeholder, StringComparison.Ordinal)
            ? message.Replace(placeholder, link, StringComparison.Ordinal)
            : message.TrimEnd() + Environment.NewLine + link;
    }

    private async Task RaiseSettingsAsync()
    {
        try { Raise(Events.SettingsChanged, ToDto(await _settings.GetAsync())); }
        catch (Exception) { /* the UI reads settings again on its next start */ }
    }

    private void Raise(string name, object payload) =>
        EventRaised?.Invoke(JsonSerializer.Serialize(new EventEnvelope(name, payload), Contract.Json));

    private static T? Args<T>(string? json) where T : class =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json, Contract.Json);

    /// <summary>A transfer as the screens show it, saying whether it is a folder that arrived.</summary>
    private TransferDto TransferRow(TransferView view) => ToDto(view) with { Folder = _received.IsFolder(view) };

    private static CommandResult ToResult(Result result) =>
        result.Ok ? CommandResult.Success(null) : Failure(result.Error);

    private static CommandResult Failure(ErrorKind? error) => CommandResult.Failure(ToCode(error ?? ErrorKind.Unexpected));

    private void RaiseShares() => Raise(Events.SharesChanged, _shares.Current().Select(ToDto).ToArray());

    private ShareDto ToDto(ShareView view) => ToDto(view, _sending.For(view.Share.Id));

    public static ShareDto ToDto(ShareView view, Sending sending) => new(
        view.Share.Id.ToString(),
        view.Share.Name,
        view.Share.SizeBytes,
        view.Status switch
        {
            ShareStatus.Ready => ShareStatusDto.Ready,
            ShareStatus.Unavailable => ShareStatusDto.Unavailable,
            ShareStatus.PartlyUnavailable => ShareStatusDto.PartlyUnavailable,
            _ => ShareStatusDto.Preparing,
        },
        sending.Activity switch
        {
            SendingActivity.Sending => ShareActivityDto.Sending,
            SendingActivity.Sent => ShareActivityDto.Sent,
            _ => ShareActivityDto.Idle,
        },
        sending.ReceiverCount,
        sending.Percent,
        sending.FinishedCount,
        sending.SecondsLeft,
        view.Share.Paused,
        view.Share.Items.Count,
        view.Share.Kind switch
        {
            ShareKind.Folder => ShareKindDto.Folder,
            ShareKind.Collection => ShareKindDto.Collection,
            _ => ShareKindDto.File,
        });

    public static TransferDto ToDto(TransferView view) => new(
        view.Transfer.Id.ToString(),
        view.Transfer.Name,
        view.Transfer.SizeBytes,
        view.ReceivedBytes,
        view.State switch
        {
            TransferState.Receiving => TransferStateDto.Receiving,
            TransferState.Paused => TransferStateDto.Paused,
            TransferState.Done => TransferStateDto.Done,
            TransferState.Failed => TransferStateDto.Failed,
            TransferState.Waiting => TransferStateDto.Waiting,
            TransferState.NoSpace => TransferStateDto.NoSpace,
            TransferState.Blocked => TransferStateDto.Blocked,
            _ => TransferStateDto.Connecting,
        },
        view.BytesPerSecond,
        view.SecondsLeft,
        view.NeededBytes,
        view.Transfer.Name is { } name && ReceivedNames.IsRisky(name));

    public static SettingsDto ToDto(Settings settings) =>
        new(settings.Language, settings.SystemLanguage, settings.View, settings.Sort, settings.SendStepsSeen, settings.StartWithWindows,
            [.. settings.PublicServers], settings.HelperAddress, settings.HelperKey, settings.YourName, settings.Theme, settings.Version, settings.WelcomeDone,
            settings.ReceiveLimit, settings.SendLimit, settings.ReceivedFolderName ?? "", settings.ReceivedFolderChosen);

    public static QuitAnswerDto ToDto(QuitAnswer answer) => new(answer.Quitting, [.. answer.Moving]);

    public static ActivityDto ToDto(Activity activity) => new(
        activity.ReceivingBytesPerSecond, activity.SendingBytesPerSecond, activity.SendingStopped, activity.ReceivingStopped,
        [.. activity.Items.Select(i => new ItemSpeedDto(i.Id, i.ReceivingBytesPerSecond, i.SendingBytesPerSecond))]);

    public static ShareDetailsDto ToDetails(ShareView view, ItemActivity activity)
    {
        var dto = ToDto(view, Sending.Idle);
        return new ShareDetailsDto(
            dto.Id, dto.Name, dto.SizeBytes, dto.Status,
            // A phone's picked item has no folder a person would recognise (AD-20); nothing is shown.
            Path.IsPathFullyQualified(view.Share.FilePath) ? Path.GetDirectoryName(view.Share.FilePath) ?? "" : "",
            view.Share.AddedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            activity.SendingBytesPerSecond, activity.Connections, activity.BytesSent, dto.ItemCount, dto.Kind);
    }

    public static TransferDetailsDto ToDetails(TransferView view, ItemActivity activity)
    {
        var dto = ToDto(view);
        return new TransferDetailsDto(
            dto.Id, dto.Name, dto.SizeBytes, dto.ReceivedBytes, dto.State,
            view.Transfer.Source switch
            {
                TransferSource.DownloadLink => TransferSourceDto.DownloadLink,
                TransferSource.DownloadFile => TransferSourceDto.DownloadFile,
                _ => TransferSourceDto.Invite,
            },
            view.Transfer.AddedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            dto.BytesPerSecond, activity.SendingBytesPerSecond, dto.SecondsLeft, activity.Connections, activity.BytesSent);
    }

    public static TransferOptionsDto ToDto(TransferOptions options) => new(
        options.Files?.Select(f => new TransferFileDto(f.Index, f.Path, f.SizeBytes, f.Wanted)).ToArray(),
        options.KeepSharing,
        options.Sharing);

    public static InvitePreviewDto ToDto(InvitePreview preview) => new(
        preview.Hints.SenderName,
        preview.Hints.Name,
        preview.Hints.SizeBytes,
        preview.Hints.ItemCount,
        preview.IsInvite,
        preview.FreeBytes);

    public static ConnectionDto ToDto(ConnectionHealth? health, bool throughHelper = false) => new(health switch
    {
        ConnectionHealth.Good => ConnectionHealthDto.Good,
        ConnectionHealth.Limited => ConnectionHealthDto.Limited,
        ConnectionHealth.Blocked => ConnectionHealthDto.Blocked,
        _ => null,
    }, throughHelper);

    public static string ToCode(ErrorKind error) => error switch
    {
        ErrorKind.ShareDuplicate => ErrorCodes.ShareDuplicate,
        ErrorKind.ShareNotFound => ErrorCodes.ShareNotFound,
        ErrorKind.ShareNotReady => ErrorCodes.ShareNotReady,
        ErrorKind.FileUnreadable => ErrorCodes.FileUnreadable,
        ErrorKind.InviteInvalid => ErrorCodes.InviteInvalid,
        ErrorKind.InviteNotInMessage => ErrorCodes.InviteNotInMessage,
        ErrorKind.TransferDuplicate => ErrorCodes.TransferDuplicate,
        ErrorKind.TransferNotFound => ErrorCodes.TransferNotFound,
        ErrorKind.TransferFinished => ErrorCodes.TransferFinished,
        ErrorKind.DownloadFileInvalid => ErrorCodes.DownloadInvalidFile,
        ErrorKind.DiskFull => ErrorCodes.DiskFull,
        ErrorKind.SettingsInvalid => ErrorCodes.SettingsInvalid,
        ErrorKind.ReceivedNotFound => ErrorCodes.ReceivedNotFound,
        ErrorKind.ReceivedOpenFailed => ErrorCodes.ReceivedOpenFailed,
        ErrorKind.ReceivedConfirmRisky => ErrorCodes.ReceivedConfirmRisky,
        ErrorKind.ShareDropFailed => ErrorCodes.ShareDropFailed,
        ErrorKind.SaveFailed => ErrorCodes.FileSaveFailed,
        ErrorKind.NotPlayable => ErrorCodes.MediaNotPlayable,
        ErrorKind.FolderEmpty => ErrorCodes.ShareFolderEmpty,
        ErrorKind.FolderTooLarge => ErrorCodes.ShareFolderTooLarge,
        ErrorKind.ShareNameInvalid => ErrorCodes.ShareNameInvalid,
        ErrorKind.NoFilesChosen => ErrorCodes.TransferNoFiles,
        ErrorKind.ReceivedFolderUnusable => ErrorCodes.ReceivedFolderUnusable,
        ErrorKind.ReceivedFolderMissing => ErrorCodes.ReceivedFolderMissing,
        _ => ErrorCodes.Unexpected,
    };
}
