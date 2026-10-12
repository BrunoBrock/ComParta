---
type: epic
title: "A person sees and stops all traffic at once, and the details of any file"
parent: initiative-comparta
covers: [CAP-26, CAP-27]
after: []
assignee: ""
risk: medium
status: in-progress
---

# A person sees and stops all traffic at once, and the details of any file

## Description

Two things a qBittorrent user misses in ComParta, made plain enough for everyone. A bar along the top of the window shows how fast ComParta is receiving and sending in total, with a Stop button next to it: one click stops both, and its menu stops only sending or only receiving. Every shared and received file gets an information button that opens a window with everything ComParta knows about it, updated live.

## Outcome

Paulo stops ComParta before a video call with one click and continues it afterwards. When something looks slow, the relative helping Maria opens the details of the file and sees whether any computer is connected and how fast bytes are moving, without a single technical word.

## Requirements

- CAP-26 (details of any file), CAP-27 (see and stop all the traffic).
- AD-22 (activity from the engine, stop flags in core), AD-5 (plain words), AD-10 (settings store), AD-4 (the UI shows what the app reports).
- UX design: "Speed bar" and "Details window".

## Done when

1. The bar shows the total receiving and sending speeds and updates at least once a second while bytes move.
2. Stop stops both directions; the menu stops only one; Continue starts again. Nothing is cancelled or removed, and a transfer the person paused on its own stays paused after Continue.
3. The stopped state survives a restart of the app.
4. Every card and row has an information button; its window shows the rows of the UX design for shared and received files and updates while open.
5. No technical file-sharing word appears in the bar, the menu or the window, in English or Portuguese.

## Boundaries

Totals and per-file details. Not speed limits (epic 14 story 4, "More options"), not per-connection lists, not graphs.

## References

- spec — CAP-26, CAP-27
- architecture — AD-22, AD-5, AD-10
- UX design — "Speed bar", "Details window"
- roadmap — decision P-11

## Notes

- Requested by Bruno on 2026-10-12, to be built next, on its own branch for him to review.
- The details window is a window for now; epic 8 story 8 shows the same rows in the details panel.
- Waits on epic-send-to-a-person because: it needs shares and transfers that move bytes.
- Built 2026-10-12: all four stories. Receiving is stopped with the engine's full stop, not pause, because a transfer still fetching its details cannot pause; a transfer's own Pause survives Continue. Each transfer now remembers where it came from (database version 4). In the browser, `?stopped=both`, `sending` or `receiving` starts with Stop already chosen.
