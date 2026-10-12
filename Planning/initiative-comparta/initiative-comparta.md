---
type: initiative
title: "ComParta: share files with family and friends, no cloud"
parent: none
covers: [CAP-1, CAP-2, CAP-3, CAP-4, CAP-5, CAP-6, CAP-7, CAP-8, CAP-9, CAP-10, CAP-11, CAP-12]
after: []
assignee: ""
risk: high
---

# ComParta: share files with family and friends, no cloud

## Description

Non-technical people send files directly to people they choose, with no account, no cloud and no torrent knowledge. The spec owns the capabilities, constraints and non-goals; the architecture spine owns the decisions every epic must follow. The first release (MVP) is a Windows desktop app where a person adds or removes a shared file.

## Outcome

For parents, grandparents, relatives and friends: two people in different homes move a large family video directly between their devices with no account and no hosted service. This is the spec's product success signal.

## Done when

1. A person who has never used a torrent program adds and removes a share on Windows without help, and the original file is untouched (spec MVP signal).
2. Two people in different homes transfer a large file directly between their devices, started from an invite sent by email or chat.
3. A network capture during normal use shows no connection to any hosted service, apart from the public lookup network's well-known entry nodes (AD-8), and the app has no sign-in.
4. No screen or message uses a torrent term (CAP-4).
5. The same flows work on an Android phone.
6. A non-technical person installs the app from one download.

## Boundaries

Epics follow the spec's capabilities. Not public search, not cloud storage or required relays, not power-user torrent features; see the spec's non-goals. One repository, one owner; the units (core, contracts, engine adapter, storage adapter, shell, UI) are code folders, not epic boundaries.

Tracer path: a file picked in the Windows dialog appears in the list, survives a restart and is removed again (epic 1).

## References

- spec — Planning/initiative-comparta/spec-comparta/spec-comparta.md, section Capabilities
- constraint — the same spec, section Constraints
- architecture — Planning/initiative-comparta/architecture-comparta/architecture-comparta.md, AD-1 to AD-12 and Deferred
- vision — Planning/ReadMe.md, for history only

## Notes

- Assumption: the platform baseline (scaffold, CI) is the first stories of epic 1 and not its own epic, because the product has no servers or environments to stand up.
- Assumption: epics after the first keep their envelope only; they are broken into stories when their open question is answered.
- Decision: OQ-1 privacy and discovery model is answered for the first release by AD-8 (2026-10-08), and on 2026-10-12 the maintainer accepted that shared files are reachable from the whole internet, with the limits stated in AD-8. Still open: a stronger model where only chosen people can download, hardening (upload limits, invites that expire), and routers with no inbound path; epic 2 plans the rest.
- Decision 2026-10-12: Done when 3 allows the public lookup network's well-known entry nodes (`router.bittorrent.com` is in the MonoTorrent DHT library); see epic-send-to-a-person Notes.
- Parked: edge cases and small items set aside on 2026-10-12 are listed in `todo-later.md` in this folder.
- Open question: OQ-2 Windows sandbox and marking of received files; epic 3 waits on it.
- Open question: OQ-3 open source license; needed before the repository is public.
- Open question: OQ-4 packaging, signing and updates; epic 5 waits on it.
- Parked: sharing a whole folder (OQ-5); not on any epic yet.
