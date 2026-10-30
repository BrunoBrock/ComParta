// Generated from ComParta.Contracts by tools/ComParta.Contracts.TypeGen. Do not edit.

export type ShareStatusDto = "preparing" | "ready" | "unavailable" | "partlyUnavailable";

export type ShareActivityDto = "idle" | "sending" | "sent";

export type ShareKindDto = "file" | "folder" | "collection";

export type TransferStateDto = "connecting" | "receiving" | "paused" | "done" | "failed" | "waiting" | "noSpace" | "blocked";

export type ConnectionHealthDto = "good" | "limited" | "blocked";

export type TransferSourceDto = "invite" | "downloadLink" | "downloadFile";

export type ThumbnailKindDto = "share" | "transfer";

export type ItemKindDto = "share" | "transfer";

export type MediaKindDto = "picture" | "song" | "video";

export interface ShareDto {
  id: string;
  name: string;
  sizeBytes: number;
  status: ShareStatusDto;
  activity: ShareActivityDto;
  receiverCount: number;
  percent: number;
  finishedCount: number;
  secondsLeft: number | null;
  paused: boolean;
  itemCount: number;
  kind: ShareKindDto;
}

export interface AddedDto {
  share: ShareDto | null;
  filesToName: number;
}

export interface AddPickedArgs {
  name: string | null;
}

export interface IdArgs {
  id: string;
}

export interface CopyInviteArgs {
  id: string;
  message: string | null;
}

export interface TransferDto {
  id: string;
  name: string | null;
  sizeBytes: number | null;
  receivedBytes: number;
  state: TransferStateDto;
  bytesPerSecond: number;
  secondsLeft: number | null;
  neededBytes: number | null;
  risky: boolean;
  folder: boolean;
}

export interface ReceiveArgs {
  invite: string;
}

export interface InvitePreviewDto {
  senderName: string | null;
  name: string | null;
  sizeBytes: number | null;
  itemCount: number | null;
  isInvite: boolean;
  freeBytes: number | null;
}

export interface TransferOptionsDto {
  files: TransferFileDto[] | null;
  keepSharing: boolean;
  sharing: boolean;
}

export interface TransferFileDto {
  index: number;
  path: string;
  sizeBytes: number;
  wanted: boolean;
}

export interface SetTransferOptionsArgs {
  id: string;
  skipped: number[] | null;
  keepSharing: boolean | null;
}

export interface OpenReceivedArgs {
  id: string;
  confirmed: boolean | null;
}

export interface ReceivedFileDto {
  relativePath: string;
  sizeBytes: number;
}

export interface SettingsDto {
  language: string | null;
  systemLanguage: string;
  view: string | null;
  sort: string | null;
  sendStepsSeen: number;
  startWithWindows: boolean;
  publicServers: string[];
  helperAddress: string | null;
  helperKey: string | null;
  yourName: string | null;
  theme: string | null;
  version: string;
  welcomeDone: boolean;
  receiveLimit: number | null;
  sendLimit: number | null;
  receivedFolder: string;
  receivedFolderChosen: boolean;
}

export interface SetSettingsArgs {
  language: string | null;
  view: string | null;
  sort: string | null;
  sendStepsSeen: number | null;
  startWithWindows: boolean | null;
  publicServers: string[] | null;
  helperAddress: string | null;
  helperKey: string | null;
  yourName: string | null;
  theme: string | null;
  welcomeDone: boolean | null;
  receiveLimit: number | null;
  sendLimit: number | null;
}

export interface ConnectionDto {
  health: ConnectionHealthDto | null;
  throughHelper: boolean;
}

export interface ActivityDto {
  receivingBytesPerSecond: number;
  sendingBytesPerSecond: number;
  sendingStopped: boolean;
  receivingStopped: boolean;
  items: ItemSpeedDto[];
}

export interface ItemSpeedDto {
  id: string;
  receivingBytesPerSecond: number;
  sendingBytesPerSecond: number;
}

export interface SetActivityArgs {
  sendingStopped: boolean;
  receivingStopped: boolean;
}

export interface ShareDetailsDto {
  id: string;
  name: string;
  sizeBytes: number;
  status: ShareStatusDto;
  folder: string;
  addedAt: string;
  sendingBytesPerSecond: number;
  connections: number;
  bytesSent: number;
  itemCount: number;
  kind: ShareKindDto;
}

export interface TransferDetailsDto {
  id: string;
  name: string | null;
  sizeBytes: number | null;
  receivedBytes: number;
  state: TransferStateDto;
  source: TransferSourceDto;
  addedAt: string;
  receivingBytesPerSecond: number;
  sendingBytesPerSecond: number;
  secondsLeft: number | null;
  connections: number;
  bytesSent: number;
}

export interface DropArgs {
  token: string;
}

export interface DropResultDto {
  added: number;
  problem: string | null;
}

export interface ThumbnailArgs {
  kind: ThumbnailKindDto;
  id: string;
  size: number;
  item: number;
}

export interface QuitArgs {
  force: boolean;
}

export interface QuitAnswerDto {
  quitting: boolean;
  moving: string[];
}

export interface MediaArgs {
  kind: ItemKindDto;
  id: string;
}

export interface MediaDto {
  url: string;
  kind: MediaKindDto;
  name: string;
}

export interface DetailsTargetDto {
  kind: ItemKindDto;
  id: string;
  name: string;
}

export const errorCodes = [
  "share.duplicate",
  "share.notFound",
  "share.notReady",
  "file.unreadable",
  "invite.invalid",
  "invite.notInMessage",
  "transfer.duplicate",
  "transfer.notFound",
  "transfer.finished",
  "download.invalidFile",
  "disk.full",
  "settings.invalid",
  "received.notFound",
  "received.openFailed",
  "received.confirmRisky",
  "share.dropFailed",
  "file.saveFailed",
  "media.notPlayable",
  "share.folderEmpty",
  "share.folderTooLarge",
  "share.nameInvalid",
  "transfer.noFiles",
  "received.folderUnusable",
  "received.folderMissing",
  "unexpected",
] as const;

export type ErrorCode = (typeof errorCodes)[number];

export interface Commands {
  "shares.list": { args: undefined; result: ShareDto[] };
  "shares.add": { args: undefined; result: AddedDto };
  "shares.addPicked": { args: AddPickedArgs; result: ShareDto | null };
  "shares.addFolder": { args: undefined; result: ShareDto | null };
  "shares.remove": { args: IdArgs; result: null };
  "shares.copyInvite": { args: CopyInviteArgs; result: null };
  "shares.saveDownloadFile": { args: IdArgs; result: string };
  "shares.inviteLink": { args: IdArgs; result: string };
  "shares.pause": { args: IdArgs; result: null };
  "shares.resume": { args: IdArgs; result: null };
  "shares.reveal": { args: IdArgs; result: null };
  "transfers.list": { args: undefined; result: TransferDto[] };
  "transfers.preview": { args: ReceiveArgs; result: InvitePreviewDto };
  "transfers.receive": { args: ReceiveArgs; result: TransferDto };
  "clipboard.findInvite": { args: undefined; result: string | null };
  "transfers.remove": { args: IdArgs; result: null };
  "transfers.openFile": { args: undefined; result: TransferDto | null };
  "transfers.pause": { args: IdArgs; result: null };
  "transfers.resume": { args: IdArgs; result: null };
  "transfers.options": { args: IdArgs; result: TransferOptionsDto };
  "transfers.setOptions": { args: SetTransferOptionsArgs; result: null };
  "transfers.takeOpenedFile": { args: undefined; result: InvitePreviewDto | null };
  "transfers.receiveOpenedFile": { args: undefined; result: TransferDto };
  "received.openFolder": { args: undefined; result: null };
  "received.open": { args: OpenReceivedArgs; result: null };
  "received.reveal": { args: IdArgs; result: null };
  "received.delete": { args: IdArgs; result: null };
  "received.files": { args: IdArgs; result: ReceivedFileDto[] };
  "settings.get": { args: undefined; result: SettingsDto };
  "settings.set": { args: SetSettingsArgs; result: null };
  "settings.chooseReceivedFolder": { args: undefined; result: string | null };
  "settings.useUsualReceivedFolder": { args: undefined; result: null };
  "connection.get": { args: undefined; result: ConnectionDto };
  "connection.openSettings": { args: undefined; result: null };
  "invites.takeOpened": { args: undefined; result: string | null };
  "activity.get": { args: undefined; result: ActivityDto };
  "activity.set": { args: SetActivityArgs; result: null };
  "shares.details": { args: IdArgs; result: ShareDetailsDto };
  "transfers.details": { args: IdArgs; result: TransferDetailsDto };
  "shares.addDropped": { args: DropArgs; result: DropResultDto };
  "thumbnails.get": { args: ThumbnailArgs; result: string | null };
  "app.quit": { args: QuitArgs; result: QuitAnswerDto };
  "app.leave": { args: undefined; result: null };
  "media.open": { args: MediaArgs; result: MediaDto };
  "support.report": { args: undefined; result: string };
  "about.notices": { args: undefined; result: string };
}

export interface Events {
  "shares.changed": ShareDto[];
  "transfers.changed": TransferDto[];
  "settings.changed": SettingsDto;
  "connection.changed": ConnectionDto;
  "invites.changed": boolean;
  "activity.changed": ActivityDto;
  "details.changed": DetailsTargetDto;
  "quit.changed": QuitAnswerDto;
  "back.changed": boolean;
}

export type CommandResult<T> = { ok: true; value: T } | { ok: false; error: { code: ErrorCode } };

export type EventEnvelope = { [E in keyof Events]: { event: E; payload: Events[E] } }[keyof Events];
