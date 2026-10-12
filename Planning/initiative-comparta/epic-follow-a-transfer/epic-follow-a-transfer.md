---
type: epic
title: "A person sees how a transfer is going"
parent: initiative-comparta
covers: [CAP-8]
after: []
assignee: ""
risk: medium
---

# A person sees how a transfer is going

## Description

Sender and receiver both see, in plain words, whether a file is on its way, finished or stuck, and what to do when it is stuck.

## Outcome

A person never has to wonder whether a file arrived or ask someone technical why it has not; each transfer explains itself.

## Requirements

To be completed when this epic is planned. The spec is the source: CAP-8 (follow a transfer).

## Done when

1. A running transfer shows its progress on both devices.
2. A finished transfer says so, and the receiver can open the Received folder from it.
3. When the other device is off or unreachable, the transfer says so in plain words and resumes when it returns.
4. No transfer state uses a torrent term.
5. The feature is in the release build.

## Boundaries

Showing and explaining transfer state. Not speed limits, queues or other power-user controls (spec non-goals).

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- spec — Planning/initiative-comparta/spec-comparta/spec-comparta.md, CAP-8
- architecture — Planning/initiative-comparta/architecture-comparta/architecture-comparta.md, AD-4, AD-5, AD-11

## Notes

- Decision: OQ-1 is answered for the first release by AD-8 (extended to the whole internet on 2026-10-12). The stuck states include "waiting for the other person's device" and "the sender cannot be reached from outside"; the sender's own reachability line comes from epic-send-to-a-person.
- Waits on epic-send-to-a-person because: it provides transfers between two devices.
