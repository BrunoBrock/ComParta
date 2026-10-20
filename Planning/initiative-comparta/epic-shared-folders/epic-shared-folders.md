---
type: epic
title: "A person makes folders and shares them with friends, like Google Drive, and the files travel peer to peer"
parent: initiative-comparta
covers: [CAP-32]
after: []
assignee: ""
risk: high
---

# A person makes folders and shares them with friends, like Google Drive, and the files travel peer to peer

## Description

Like Google Drive or OneDrive, a person creates folders in ComParta, puts files in them, and shares a folder with chosen friends. The friends see the folder in "Shared with me", open it, and get one file or all of it; when the owner adds, renames or removes a file, the friends see the change. Unlike Drive, nothing is stored in a cloud: each file travels between the friends' devices with the same engine as every other share, and through the helper when the owner cannot be reached.

A folder in ComParta is a list the owner keeps, not a copy of the files: the originals stay where they are on disk (AD-6). The owner's device signs each new version of the list, and each file in it is its own transfer, so adding one file never re-sends the rest (AD-25).

Asked by the maintainer on 2026-10-20 (roadmap decision P-19).

## Outcome

Paulo makes a folder "Natal 2024", drops 200 photos in it and shares it with his mother and his sister. They open it in Shared with me on a PC and on a phone, see the pictures, and get them. The next day Paulo adds ten more; they appear in the folder on both devices without anyone sending anything again.

## Requirements

- CAP-32: folders like Google Drive, shared with friends. Builds on CAP-24 (friends, epic 13) and CAP-19 (several items in one share, epic 11).
- AD-25 (a shared folder is a signed list; each file is its own transfer), AD-24 (only friends' devices get it), AD-6 (originals untouched; received folders land under the Received root), AD-19 and AD-20 (items and content on phones), AD-23 (the helper when the owner cannot be reached), AD-5.
- UX design: "My folders" beside My shares, folder cards, the Share with friends sheet, and a friend's folder in Shared with me.

## Done when

1. A person creates, renames and deletes folders in ComParta and moves files into them; deleting a folder never deletes the original files.
2. Sharing a folder with chosen friends makes it appear in their Shared with me; anyone else, including someone holding a captured link, gets nothing.
3. A friend opens the folder, sees its files with pictures, and gets one file or all of them; received files land under the Received root in a folder of the same name.
4. A file the owner adds, renames or removes shows up for every friend who has the folder the next time both devices are on; a friend who chose "Keep this folder up to date" gets new files automatically.
5. When the owner's computer cannot be reached, friends still get the folder's files through the helper, and only friends can.
6. The same folder works on Windows and on Android.
7. A wiring test in Windows CI drives creating a folder, sharing it with a friend, the friend getting it, and a file added later reaching the friend, through the bridge, router, use cases, real engine and storage (CLAUDE.md testing rule).

## Boundaries

Folders, their lists and sharing them with friends. Not editing a file together, not comments, not version history of a file's content, not sharing a folder with the open internet (a single file's public link is epic 16).

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- spec — CAP-32, CAP-19, CAP-24
- architecture — AD-6, AD-19, AD-20, AD-23, AD-24, AD-25
- roadmap — R2, P-19

## Notes

- Waits on epic 13 because: a folder is shared with friends, and only friends' devices may get it.
- Waits on epic 11 because: it brings several items per share and receiving folders.
- Waits on epic 7 because: the folder must work on phones from the start, so it comes after the Android shell.
- Risk: keeping a friend's copy in step while devices come and go. The list is versioned, so a device that was off simply gets the newest version.
