---
type: epic
title: "A person sends a share to someone they choose, who receives it"
parent: initiative-comparta
covers: [CAP-6, CAP-7, CAP-16]
after: []
assignee: ""
risk: high
status: in-progress
---

# A person sends a share to someone they choose, who receives it

## Description

The sender turns a share into an invite they can paste into an email or chat. The person who gets the invite opens it in ComParta and the file travels directly from the sender's device to theirs. This is the first epic where data leaves the device, so it is where the privacy and discovery model is put into practice.

## Outcome

Two people in different homes move a large family video directly between their devices with no account and no hosted service (the spec's product success signal).

## Requirements

The spec is the source; stories cite its ids. Story 1 is the first version already built; stories 2 to 7 are the R1 follow-ups.

- CAP-6, CAP-7: invite and receive, built in the first version.
- CAP-16: the invite explains itself: a ComParta link inside a friendly message, paste anywhere, click to open, and a confirmation before anything is received (AD-16, UX design "Receive a file").

## Done when

1. The sender creates an invite for a share and pastes it into an email or chat.
2. The receiver opens the invite and the file arrives in the Received folder, identical to the original.
3. The transfer works between two homes on ordinary home routers. Moved to epic 9 (connect between homes), which measures and improves it.
4. Someone without the invite cannot obtain the file.
5. A network capture shows no connection to a hosted service during the transfer, apart from the public lookup network's well-known entry nodes (AD-8).
6. Both people run a release build on their own Windows PCs.

## Boundaries

Invite and receive on Windows. Not transfer progress beyond done or not done (epic Follow a transfer), not operating-system sandboxing (epic Safe received content), not Android.

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- spec — Planning/initiative-comparta/spec-comparta/spec-comparta.md, CAP-6, CAP-7, Open Questions OQ-1
- architecture — Planning/initiative-comparta/architecture-comparta/architecture-comparta.md, AD-2, AD-6, AD-8, AD-9, Deferred (privacy and discovery model, engine fitness)

## Notes

- Status 2026-10-08: a first version was built together with epic-share-a-file. Done when 1, 2 and 4 are met in the app and checked on one PC with two copies running; 3, 5 and 6 still need two real PCs in two homes.
- Built 2026-10-11: stories 2 to 5 (ComParta links, friendly invite message, "Get this file?", paste anywhere). The invite message says "Someone" until epic 12 adds your name. Story 5's manual check (copy from WhatsApp Desktop, press Ctrl+V) still needs a Windows PC.
- Built 2026-10-11: story 6. One running copy per data folder (`ComParta.Shell.SingleInstance`, a local pipe only this user can open); each start writes the `comparta:` scheme under HKCU, and the MSIX manifest declares it. A clicked link reaches the running copy, which comes to the front with "Get this file?" open. Still to check by hand on Windows: clicking a link in Outlook, Gmail in Edge and WhatsApp Desktop.
- OQ-1 was answered for this release by AD-8. What remains is in the architecture's Deferred table.
- Unknown: whether MonoTorrent connects reliably through home routers without uTP. Moved to epic 9, which starts with a field test.
- Plan 2026-10-10: invites become `comparta://` links (AD-16); the receive step always confirms first; story 6 also brings the single running copy that epic 10 builds on. The epic is done when stories 2 to 7 are built and Done when 1, 2, 4, 5 and 6 are checked.
- Decision 2026-10-12 (maintainer): Done when 5 allows the public lookup network's well-known entry nodes; `router.bittorrent.com` is in the MonoTorrent DHT library, so a capture shows it.
- Decision 2026-10-12 (maintainer): shared files are reachable from the whole internet, with the limits in AD-8. Reachability is epic 9; edge cases set aside are in `todo-later.md`.
- Assumption: the friendly invite message (story 3) and the copy action say that the link is for the chosen person only, because anyone holding it can download the file (spec, Constraints, 2026-10-12).
- Waits on epic-share-a-file because: it provides the running app, the bridge contract and the engine port.
