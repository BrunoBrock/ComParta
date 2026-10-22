---
type: epic
title: "A person sends a whole folder or several files as one share"
parent: initiative-comparta
covers: [CAP-19]
after: []
assignee: ""
risk: high
---

# A person sends a whole folder or several files as one share

## Description

Families send albums, not single files. This epic changes a share from one file to a set of items with relative paths (AD-19), lets a person pick or drop a folder or several files as one share with one invite, and recreates the folder on the receiving side under the Received root. It comes before Android so the phone builds on the final share model.

## Outcome

Paulo sends the 200 photos of the birthday as one folder with one invite, and Maria finds them in one folder named after the album.

## Requirements

- CAP-19: share a folder or several files.
- AD-19: a share is a set of items; AD-6: every received path segment is sanitised; AD-7 (amended): folders enter through the native folder picker or a drop.
- UX design: folder card with up to four thumbnails and "· 200 files".

## Done when

1. A folder picked or dropped becomes one share with one invite; several files picked together become one share named by the person (default "{n} files").
2. The receiver gets the same folder structure under `Received/<share name>/`, byte-identical.
3. Existing single-file shares and transfers keep working after the upgrade.
4. A file in a shared folder that changes or disappears marks the share "partly unavailable" with a plain explanation; files added later are not part of the share and the details panel says so.
5. Hostile metadata limits (item count, depth, name length) hold.

## Boundaries

Shares made of several items, on Windows. Not syncing a folder as it changes (spec non-goal), not Android pickers (epic 7 reuses this model).

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- spec — CAP-19, Open Questions OQ-5
- architecture — AD-6, AD-7, AD-19
- UX design — "Items", "Details panel"

## Notes

- Assumption: MonoTorrent can create multi-file content from files that do not share one parent folder, by giving each file a source path and a destination path; story 3 checks it. If not, several unrelated files become a share only when they share a parent folder, and the picker says so.
- Waits on epic-drive-like-home because: the folder card and the drop target are built there.
- Waits on epic-safe-received-content because: per-segment sanitising and the metadata limits extend its tests.
- Built 2026-10-22, story 4 (branch `claude/project-thread-9pxli5`): a received folder is one item marked `folder`; every name inside it is sanitised deepest first, so a reserved or disguised name nested in a folder is kept only under its safe name; 200 files arrive with their tree. `received.files` lists the files inside a received folder with their places and sizes, and the screen shows them in a folder view with Show in folder. Not yet: opening one file from the folder view, and the folder's count on its card (story 5).
