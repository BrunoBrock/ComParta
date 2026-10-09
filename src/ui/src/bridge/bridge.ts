import type { CommandResult, Commands, EventEnvelope } from './contract.generated.ts'

type CommandName = keyof Commands
type ArgsOf<C extends CommandName> = Commands[C]['args']
type ResultOf<C extends CommandName> = Commands[C]['result']

/**
 * The only way the UI reaches the app (AD-3). Commands go in, events come out; the UI never
 * touches files, the network or anything else directly.
 */
export interface Bridge {
  invoke<C extends CommandName>(
    command: C,
    ...args: ArgsOf<C> extends undefined ? [] : [ArgsOf<C>]
  ): Promise<CommandResult<ResultOf<C>>>
  /** Returns a function that stops listening. */
  subscribe(listener: (event: EventEnvelope) => void): () => void
}

export type { CommandName, ArgsOf, ResultOf }
