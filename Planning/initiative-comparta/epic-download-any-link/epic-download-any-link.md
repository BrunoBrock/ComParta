---
type: epic
title: "A person downloads from any link, like qBittorrent but simpler"
parent: initiative-comparta
covers: [CAP-25]
after: []
assignee: ""
risk: medium
status: in-progress
---

# A person downloads from any link, like qBittorrent but simpler

## Description

Besides files sent by people they know, a person can paste a download link from a website or open a download file a website gave them, and ComParta downloads it into the Received folder with the same safety rules. The main screen stays simple: progress, speed, time left, pause and continue. Everything a qBittorrent user would look for beyond that lives under "More options" on each download.

## Outcome

Paulo, who used qBittorrent before, uses ComParta for everything, and Maria can download the film her grandson sent her a link to without seeing a single technical word.

## Requirements

- CAP-25 (download from any link), CAP-8 (follow a transfer), CAP-9 (safe received content).
- AD-8 (amended 2026-10-10), AD-21; AD-5 still holds: the catalog says "download link" and "download file".

## Done when

1. Pasting a public download link starts a download that finishes in the Received folder.
2. Opening a download file through the file dialog does the same; a file that is not one gets a plain message.
3. Each download shows speed and time left, and can be paused and continued, also after a restart.
4. More options lets the person choose which files of a multi-file download to get, limit speed, and stop sharing when finished.
5. Double-clicking a download file or a download link in the browser opens ComParta.

## Boundaries

Downloads started by the person. Not search, RSS or queues (spec non-goals).

## References

- spec — CAP-25
- architecture — AD-8 (amended), AD-21, AD-6, AD-13
- roadmap — decision P-10

## Notes

- Built 2026-10-10 on the plan branch: stories 1 to 3.
- Waits on epic-send-to-a-person because: it reuses the receive path.
- Note 2026-10-13: qBittorrent is the reference for this epic (roadmap, References).
- Decided 2026-10-20 (D-7): with friends (epic 13), website links and download files count as unknown sources, so while the switch is off, pasting one says so and points to the switch.
- Note 2026-10-20: with friends (epic 13), website links and download files count as unknown sources if decision D-7 is answered yes, so while the switch is off, pasting one says so and points to the switch.
- Built 2026-10-20: the speed-limit part of story 4. Settings has "Receive at most" and "Send at most" for ComParta as a whole (256 KB to 20 MB a second, or as fast as possible), stored as `receiveLimit` and `sendLimit` and handed to the engine at start and whenever they change. Choosing which files of a download to get, and whether to keep sharing a finished download, are still to do; they change how files are received, so they wait for the safe-received-content work.
- Built 2026-10-22: the rest of story 4 and story 5. The details window of a download from a link or a download file has More options: tick which files of a download of several files to get (at least one stays; the choice is kept after a restart), keep sharing it once it has arrived (on by default, until ComParta quits; turning it off, Clear or Delete stops sharing and the file stays), and the two speed limits from Settings. A file shared after it arrived follows the speed bar's sending Stop. Story 5: the MSIX package declares download links and download files, so Windows offers ComParta for them (it asks the person first, so another app is never replaced silently); the running copy opens "Get this file?" with the link or the file's name and size filled in. The unpackaged build registers neither. Waiting on a person at a Windows PC: double-click a download file and click a download link in Edge.
- Reading of "keep sharing" (2026-10-22): a download that arrived is shared until ComParta quits, not again after a restart; files from an earlier ComParta invite (`comparta:` links) are never shared back, while the current invites, which are standard links, follow the same choice as any download link.
