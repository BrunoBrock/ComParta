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
