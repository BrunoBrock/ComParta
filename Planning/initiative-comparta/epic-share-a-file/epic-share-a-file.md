---
type: epic
title: "A person adds and removes shared files on Windows (MVP)"
parent: initiative-comparta
covers: [CAP-1, CAP-2, CAP-3, CAP-4, CAP-5]
after: []
assignee: ""
risk: high
status: in-progress
---

# A person adds and removes shared files on Windows (MVP)

## Description

The first release. On a Windows PC a person opens ComParta, picks a file to share, sees it in a list, and removes it again, in plain language and without the original file ever being touched. Nothing is sent to another person yet; this epic proves that the UI, the bridge, the core, storage and the torrent engine work together under the rules in the architecture spine.

## Outcome

A person who has never used a torrent program adds a file and removes it on Windows without help, and the original file is untouched (the spec's MVP success signal).

## Requirements

The spec is the source; stories cite its ids.

- CAP-1: Add a share through the Windows file dialog.
- CAP-2: See my shares; the list survives a restart; empty state when there are none.
- CAP-3: Remove a share after confirming; the original is kept.
- CAP-4: Plain-language experience; no torrent terms; every error code has a message.
- CAP-5: A share whose file was moved, renamed or deleted is marked unavailable and is still removable.

## Done when

1. On a Windows PC, a file picked in the file dialog appears in the list with its name and size, and its checksum is the same before and after.
2. After closing and reopening the app the list is the same.
3. Removing a share, after confirming, takes it off the list for good and the original file still exists unchanged.
4. Renaming a shared file outside the app shows that share as unavailable, and it is still removable.
5. The automated check finds no torrent term in any UI string and a message for every error code, and continuous integration builds and passes all tests on a Windows runner.
6. The app runs from a release build on a Windows PC other than the developer's.

## Boundaries

The Windows desktop app and everything inside it: core, contracts, storage adapter, engine adapter, shell and UI. Not sending to or receiving from another person, not an installer, not mobile, not folders (single files only).

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- spec — Planning/initiative-comparta/spec-comparta/spec-comparta.md, CAP-1 to CAP-5, Constraints, Assumptions
- architecture — Planning/initiative-comparta/architecture-comparta/architecture-comparta.md, AD-1 to AD-12, Consistency Conventions, Stack, Structural Seed
- constraint — the same architecture, AD-8: the network stays closed (local-network discovery only)

## Notes

- Status 2026-10-08: stories 1 and 3 to 12 are built and their automated checks pass. Story 2 (continuous integration) has a workflow file that has not run yet, because nothing has been pushed. Done when 6 (another PC) is not checked yet. The first version of epic-send-to-a-person was built at the same time, so the sentence "Nothing is sent to another person yet" in the Description no longer holds.
- `ShareDto` carries a `status` (preparing, ready, unavailable) instead of the planned `available` flag, because reading a large file before it can be sent takes time and the screen needs to say so.
- Tracer bullet: story 1. One `shares.list` call travels from the React UI through the bridge and `BridgeHost` to a core use case and back, inside the MAUI window.
- Sequencing: after the tracer, the bridge contract (3) and the core (4) open three lanes that do not touch the same code: UI (7, 8, 9), adapters (5, 6), and shell wiring (10). Story 10 is the first point where the real app adds and removes a file.
- Handoff: story 3 defines the DTOs and error codes on their own; story 4 defines the domain and its errors on its own; story 10 maps one to the other.
- Assumption: continuous integration is GitHub Actions.
- Assumption: no visual design exists; UI stories use a plain, accessible layout until `bmad-ux` runs.
- Assumption: no closing end-to-end suite; Done when is checked by hand on a real PC.
- Assumption: adding the same file twice is refused; remove asks for confirmation (spec Assumptions).
- Unknown: whether MonoTorrent 3.0.2 runs cleanly on .NET 10 with public discovery off; story 6 finds out. If it does not, the fallback is another engine behind the same port (AD-2).
- Unknown: whether MAUI `HybridWebView` on Windows is adequate; story 1 finds out. Fallback is a WPF + WebView2 shell (AD-1).
- Parked: sharing a whole folder (OQ-5). Answered 2026-10-10: it is epic 11 in R2.
- Plan 2026-10-10: CI now runs green on the `mvp-share-and-send` branch, so story 2 is built. What is left is story 13 (refactor sweep, run it before epic 8 changes the UI) and story 14 (second PC). The epic is done when both are.
