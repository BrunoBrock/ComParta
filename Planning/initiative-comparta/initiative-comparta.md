---
type: initiative
title: "ComParta: share files with family and friends, no cloud"
parent: none
covers: [CAP-1, CAP-2, CAP-3, CAP-4, CAP-5, CAP-6, CAP-7, CAP-8, CAP-9, CAP-10, CAP-11, CAP-12, CAP-13, CAP-14, CAP-15, CAP-16, CAP-17, CAP-18, CAP-19, CAP-20, CAP-21, CAP-22, CAP-23, CAP-24, CAP-25, CAP-26, CAP-27, CAP-28, CAP-29, CAP-30]
after: []
assignee: ""
risk: high
---

# ComParta: share files with family and friends, no cloud

## Description

Non-technical people send files directly to people they choose, with no account, no cloud and no torrent knowledge, in a window that looks like a simple file drive. The spec owns the capabilities, constraints and non-goals; the architecture spine owns the decisions every epic must follow; the UX design owns screens and words; the roadmap owns releases and open decisions. The first build (MVP) is a Windows desktop app where a person shares a file and sends it with an invite. Then three releases: R1 family pilot on Windows, R2 albums and Android, R3 people I share with. Since 2026-10-13 the first goal inside R1 is publishing: a file shared from a PC reaches any torrent app on Windows or Android, anywhere, with no router or port setup (CAP-28, epic 16). The references are qBittorrent, Stremio and Google Drive (spec, References).

## Outcome

For parents, grandparents, relatives and friends: two people in different homes move a large family video directly between their devices with no account and no hosted service. This is the spec's product success signal.

## Done when

1. A person who has never used a torrent program adds and removes a share on Windows without help, and the original file is untouched (spec MVP signal).
2. Two people in different homes transfer a large file directly between their devices, started from an invite sent by email or chat.
3. A network capture during normal use shows no connection to any hosted service, apart from the public lookup network's well-known entry nodes (AD-8), the public servers the person named (CAP-29) and the helper set in Settings (AD-23), and the app has no sign-in.
4. No screen or message uses a torrent term (CAP-4).
5. The same flows work on an Android phone.
6. A non-technical person installs the app from one download.
7. The app looks and works like a simple file drive and passes three usability sessions with non-technical people (epic 8).
8. The app speaks Brazilian Portuguese (epic 6).
9. A file shared from a PC is downloaded by qBittorrent on another network and by a stock torrent app on an Android phone on mobile data, with no router or port changed on either side (CAP-28, epic 16).

## Boundaries

Epics follow the spec's capabilities. Not public search, not cloud storage, not power-user torrent features; see the spec's non-goals. The helper (AD-23) is in scope since 2026-10-13. One repository, one owner; the units (core, contracts, engine adapter, storage adapter, shell, UI) are code folders, not epic boundaries.

Tracer path: a file picked in the Windows dialog appears in the list, survives a restart and is removed again (epic 1).

## References

- spec — Planning/initiative-comparta/spec-comparta/spec-comparta.md, section Capabilities
- constraint — the same spec, section Constraints
- architecture — Planning/initiative-comparta/architecture-comparta/architecture-comparta.md, AD-1 to AD-12 and Deferred
- roadmap — Planning/initiative-comparta/roadmap-comparta/roadmap-comparta.md, releases, decisions P-1 to P-18 and D-1 to D-6
- roadmap drawings — Planning/initiative-comparta/roadmap-comparta/roadmap.html
- UX design — Planning/initiative-comparta/ux-comparta/ux-comparta.md
- vision — Planning/ReadMe.md, for history only

## Notes

- Assumption: the platform baseline (scaffold, CI) is the first stories of epic 1 and not its own epic, because the product has no servers or environments to stand up.
- Plan 2026-10-10: every epic has stories except epic 13, whose stories after its spike carry `refine = true`. Six epics were added (8 to 13) and CAP-13 to CAP-24 added to the spec; AD-13 to AD-20 added to the architecture.
- OQ-1 answered for R1 by AD-8; the stronger model is epic 13. OQ-2 answered by AD-13. OQ-5 answered: epic 11.
- Open: OQ-3 license (decision D-1), OQ-4 channel (decision D-2), OQ-6 target success rate between homes (epic 9).
- Decision 2026-10-12 (maintainer): shared files are reachable from the whole internet, with the limits stated in AD-8; epic 9 builds and explains the reachability.
- Decision 2026-10-12 (maintainer): Done when 3 allows the public lookup network's well-known entry nodes (`router.bittorrent.com` is in the MonoTorrent DHT library).
- Parked: edge cases and small items set aside on 2026-10-12 are listed in `todo-later.md` in this folder.
- Decision 2026-10-13 (Bruno): published files must be consumable by any torrent app on Windows and Android with no router or port setup on either side; references are qBittorrent, Stremio and Google Drive. CAP-28 to CAP-30, AD-23 and epic 16 added; D-3 answered.
- Working rules 2026-10-13 (CLAUDE.md): check every branch against main before new work; every feature gets a test through the real wiring in Windows CI; new work is a draft PR; only Bruno merges.
