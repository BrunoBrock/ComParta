// Generated from ComParta.Contracts by tools/ComParta.Contracts.TypeGen. Do not edit.

export type ShareStatusDto = "preparing" | "ready" | "unavailable";

export type TransferStateDto = "connecting" | "receiving" | "paused" | "done" | "failed";

export interface ShareDto {
  id: string;
  name: string;
  sizeBytes: number;
  status: ShareStatusDto;
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
}

export interface SetSettingsArgs {
  language: string | null;
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
  "settings.get": { args: undefined; result: SettingsDto };
  "settings.set": { args: SetSettingsArgs; result: null };
}

export interface Events {
  "shares.changed": ShareDto[];
  "transfers.changed": TransferDto[];
  "settings.changed": SettingsDto;
}

export type CommandResult<T> = { ok: true; value: T } | { ok: false; error: { code: ErrorCode } };

export type EventEnvelope = { [E in keyof Events]: { event: E; payload: Events[E] } }[keyof Events];
