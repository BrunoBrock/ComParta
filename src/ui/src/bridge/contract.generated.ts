// Generated from ComParta.Contracts by tools/ComParta.Contracts.TypeGen. Do not edit.

export type ShareStatusDto = "preparing" | "ready" | "unavailable";

export type TransferStateDto = "connecting" | "receiving" | "done" | "failed";

export interface ShareDto {
  id: string;
  name: string;
  sizeBytes: number;
  status: ShareStatusDto;
}

export interface IdArgs {
  id: string;
}

export interface TransferDto {
  id: string;
  name: string | null;
  sizeBytes: number | null;
  receivedBytes: number;
  state: TransferStateDto;
}

export interface ReceiveArgs {
  invite: string;
}

export const errorCodes = [
  "share.duplicate",
  "share.notFound",
  "share.notReady",
  "file.unreadable",
  "invite.invalid",
  "transfer.duplicate",
  "transfer.notFound",
  "unexpected",
] as const;

export type ErrorCode = (typeof errorCodes)[number];

export interface Commands {
  "shares.list": { args: undefined; result: ShareDto[] };
  "shares.add": { args: undefined; result: ShareDto | null };
  "shares.remove": { args: IdArgs; result: null };
  "shares.copyInvite": { args: IdArgs; result: null };
  "transfers.list": { args: undefined; result: TransferDto[] };
  "transfers.receive": { args: ReceiveArgs; result: TransferDto };
  "transfers.remove": { args: IdArgs; result: null };
  "received.openFolder": { args: undefined; result: null };
}

export interface Events {
  "shares.changed": ShareDto[];
  "transfers.changed": TransferDto[];
}

export type CommandResult<T> = { ok: true; value: T } | { ok: false; error: { code: ErrorCode } };

export type EventEnvelope = { [E in keyof Events]: { event: E; payload: Events[E] } }[keyof Events];
