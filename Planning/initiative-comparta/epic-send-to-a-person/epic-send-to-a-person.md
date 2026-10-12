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

- CAP-6: The sender copies an invite to paste into an email or chat. It works from outside the sender's home network. The sender is told in plain words whether the share can be reached from outside, and what to do when it cannot. Someone without the invite cannot ask for the share. (spec, Capabilities)
- CAP-7: Pasting an invite brings the content directly from the sender's device, in another home or on the same network, into the Received folder, with no account and no hosted service, and never overwrites a file already there. (spec, Capabilities)
- AD-8 (2026-10-12): The invite carries the sender's local and public address, and the app asks the router to open the listening port. The public address comes from the router or the lookup network, never from a third-party service. The invite is a bearer key: the app never presents it as safe to post publicly. (architecture, AD-8; spec, Constraints)

## Done when

1. The sender creates an invite for a share and pastes it into an email or chat.
2. The receiver opens the invite and the file arrives in the Received folder, identical to the original.
3. The transfer works between two homes on ordinary home routers.
4. Someone without the invite cannot obtain the file.
5. A network capture shows no connection to a hosted service during the transfer, apart from the public lookup network's well-known entry nodes (AD-8).
6. Both people run a release build on their own Windows PCs.
7. The sender is told in plain words whether the share can be reached from outside, and that line matches what happens in the check between two homes.

## Boundaries

Invite and receive on Windows. Not transfer progress beyond done or not done (epic Follow a transfer), not operating-system sandboxing (epic Safe received content), not Android.

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- spec — Planning/initiative-comparta/spec-comparta/spec-comparta.md, CAP-6, CAP-7, Open Questions OQ-1
- architecture — Planning/initiative-comparta/architecture-comparta/architecture-comparta.md, AD-2, AD-6, AD-8, AD-9, Deferred (privacy and discovery model, engine fitness)
- todo — Planning/initiative-comparta/todo-later.md, the edge cases and small items set aside on 2026-10-12

## Notes

- Status 2026-10-08: a first version was built together with epic-share-a-file. Done when 1, 2 and 4 are met in the app and checked on one PC with two copies running; 3, 5 and 6 still need two real PCs in two homes.
- OQ-1 was answered for this release by AD-8. What remains is in the architecture's Deferred table.
- Status 2026-10-12: shared files are reachable from the whole internet, with the limits in AD-8 (decision of the maintainer). Built: invite, receive and transfers on one PC, with invites that carry only the sender's local addresses. Left: the public address in the invite, a reachability line for the sender, and the check between two homes (entries 1 to 5).
- Decision 2026-10-12 (maintainer): OQ-1 is answered for the first release: public address in the invite, automatic port mapping, reachability status.
- Decision 2026-10-12 (maintainer): edge cases and small items are ignored for now and listed in `todo-later.md` for a later review: routers with no inbound path, help for opening a port by hand, phones on mobile data, IPv6, the saved port changing, hardening, and the wording of Done when 4.
- Decision 2026-10-12 (maintainer): Done when 5 and the initiative's Done when 3 allow the public lookup network's well-known entry nodes. `router.bittorrent.com` is in the MonoTorrent DHT library shipped with the app, so a capture shows it.
- Decision 2026-10-12 (maintainer): the first version built on 2026-10-08 (invite and receive on one PC with two copies) is the tracer bullet. Entry 1 goes first because it is the least certain part: reading the public address.
- Decision 2026-10-12 (maintainer): entries 1 and 2 run side by side (engine adapter vs core and contracts); 3 needs both; 4 needs only 2 and runs against the fake bridge, beside 3.
- Decision 2026-10-12 (maintainer): the router's external address is read through Mono.Nat. `GetExternalIPAsync` is in the shipped Mono.Nat, while MonoTorrent's own port forwarder reports only port mappings (checked in the shipped DLLs).
- Decision 2026-10-12 (maintainer): "reachable" means the router opened the port and reports a public address. It is a best guess; only a connection from outside proves it.
- Decision 2026-10-12 (maintainer): a share that is not reachable from outside can still be copied as an invite, because it works for someone on the same network. Entry 4 shows a short message for this case.
- Unknown: whether the router-mapped path works on the maintainer's real routers without uTP hole punching; entry 5 answers it.
- Assumption: the fallback for the public address, the address the lookup network reports, is readable through MonoTorrent 3.0.2. Not verified; entry 1 checks it.
- Waits on epic-share-a-file because: it provides the running app, the bridge contract and the engine port.
