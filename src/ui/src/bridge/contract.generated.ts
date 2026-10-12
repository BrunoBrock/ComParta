// Generated from ComParta.Contracts by tools/ComParta.Contracts.TypeGen. Do not edit.

export type ShareStatusDto = "preparing" | "ready" | "unavailable";

export type ShareActivityDto = "idle" | "sending" | "sent";

export type TransferStateDto = "connecting" | "receiving" | "paused" | "done" | "failed" | "waiting" | "noSpace" | "blocked";

export type ConnectionHealthDto = "good" | "limited" | "blocked";

export type TransferSourceDto = "invite" | "downloadLink" | "downloadFile";

export type ThumbnailKindDto = "share" | "transfer";

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

export interface SettingsDto {
  language: string | null;
  systemLanguage: string;
  view: string | null;
  sort: string | null;
  sendStepsSeen: number;
}

export interface SetSettingsArgs {
  language: string | null;
  view: string | null;
  sort: string | null;
  sendStepsSeen: number | null;
}

export interface ConnectionDto {
  health: ConnectionHealthDto;
}

export interface ActivityDto {
  receivingBytesPerSecond: number;
  sendingBytesPerSecond: number;
  sendingStopped: boolean;
  receivingStopped: boolean;
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
}

export const errorCodes = [
  "share.duplicate",
  "share.notFound",
  "share.notReady",
  "file.unreadable",
  "invite.invalid",
  "transfer.duplicate",
  "transfer.notFound",
  "transfer.finished",
  "download.invalidFile",
  "disk.full",
  "settings.invalid",
  "received.notFound",
  "received.openFailed",
  "share.dropFailed",
  "unexpected",
] as const;

export type ErrorCode = (typeof errorCodes)[number];

export interface Commands {
  "shares.list": { args: undefined; result: ShareDto[] };
  "shares.add": { args: undefined; result: ShareDto | null };
  "shares.remove": { args: IdArgs; result: null };
  "shares.copyInvite": { args: CopyInviteArgs; result: null };
  "transfers.list": { args: undefined; result: TransferDto[] };
  "transfers.preview": { args: ReceiveArgs; result: InvitePreviewDto };
  "transfers.receive": { args: ReceiveArgs; result: TransferDto };
  "clipboard.findInvite": { args: undefined; result: string | null };
  "transfers.remove": { args: IdArgs; result: null };
  "transfers.openFile": { args: undefined; result: TransferDto | null };
  "transfers.pause": { args: IdArgs; result: null };
  "transfers.resume": { args: IdArgs; result: null };
  "received.openFolder": { args: undefined; result: null };
  "received.open": { args: IdArgs; result: null };
  "received.reveal": { args: IdArgs; result: null };
  "received.delete": { args: IdArgs; result: null };
  "settings.get": { args: undefined; result: SettingsDto };
  "settings.set": { args: SetSettingsArgs; result: null };
  "connection.get": { args: undefined; result: ConnectionDto };
  "connection.openSettings": { args: undefined; result: null };
  "invites.takeOpened": { args: undefined; result: string | null };
  "activity.get": { args: undefined; result: ActivityDto };
  "activity.set": { args: SetActivityArgs; result: null };
  "shares.details": { args: IdArgs; result: ShareDetailsDto };
  "transfers.details": { args: IdArgs; result: TransferDetailsDto };
  "shares.addDropped": { args: DropArgs; result: DropResultDto };
  "thumbnails.get": { args: ThumbnailArgs; result: string | null };
}

export interface Events {
  "shares.changed": ShareDto[];
  "transfers.changed": TransferDto[];
  "settings.changed": SettingsDto;
  "connection.changed": ConnectionDto;
  "invites.changed": boolean;
  "activity.changed": ActivityDto;
}

export type CommandResult<T> = { ok: true; value: T } | { ok: false; error: { code: ErrorCode } };

export type EventEnvelope = { [E in keyof Events]: { event: E; payload: Events[E] } }[keyof Events];
