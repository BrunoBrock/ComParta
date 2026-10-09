---
type: epic
title: "A person sends a share to someone they choose, who receives it"
parent: initiative-comparta
covers: [CAP-6, CAP-7]
after: []
assignee: ""
risk: high
---

# A person sends a share to someone they choose, who receives it

## Description

The sender turns a share into an invite they can paste into an email or chat. The person who gets the invite opens it in ComParta and the file travels directly from the sender's device to theirs. This is the first epic where data leaves the device, so it is where the privacy and discovery model is put into practice.

## Outcome

Two people in different homes move a large family video directly between their devices with no account and no hosted service (the spec's product success signal).

## Requirements

To be completed when this epic is planned. The spec is the source: CAP-6 (invite a person), CAP-7 (receive a share).

## Done when

1. The sender creates an invite for a share and pastes it into an email or chat.
2. The receiver opens the invite and the file arrives in the Received folder, identical to the original.
3. The transfer works between two homes on ordinary home routers.
4. Someone without the invite cannot obtain the file.
5. A network capture shows no connection to a hosted service during the transfer.
6. Both people run a release build on their own Windows PCs.

## Boundaries

Invite and receive on Windows. Not transfer progress beyond done or not done (epic Follow a transfer), not operating-system sandboxing (epic Safe received content), not Android.

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- spec — Planning/initiative-comparta/spec-comparta/spec-comparta.md, CAP-6, CAP-7, Open Questions OQ-1
- architecture — Planning/initiative-comparta/architecture-comparta/architecture-comparta.md, AD-2, AD-6, AD-8, AD-9, Deferred (privacy and discovery model, engine fitness)

## Notes

- Status 2026-10-08: a first version was built together with epic-share-a-file. Done when 1, 2 and 4 are met in the app and checked on one PC with two copies running; 3, 5 and 6 still need two real PCs in two homes.
- OQ-1 was answered for this release by AD-8. What remains is in the architecture's Deferred table.
- Unknown: whether MonoTorrent connects reliably through home routers without uTP; test real two-household transfers when the model is chosen.
- Waits on epic-share-a-file because: it provides the running app, the bridge contract and the engine port.
