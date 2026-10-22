---
type: epic
title: "Received files cannot harm the computer"
parent: initiative-comparta
covers: [CAP-9]
after: []
assignee: ""
risk: high
---

# Received files cannot harm the computer

## Description

Whatever arrives from another person stays contained. The receive path already writes only under the Received root with sanitised names (AD-6); this epic adds the operating-system level protection and the handling of received files that the vision calls "safe by design".

## Outcome

A non-technical person can accept a file without risk to the rest of their computer; a hostile file from a sender cannot write outside the Received folder or run by itself.

## Requirements

The spec is the source: CAP-9 (safe handling of received content). AD-13 answers OQ-2: no AppContainer; received files get the Windows internet mark and the installed antivirus's check through the attachment services API; programs and scripts are flagged and open only after a second confirmation; nothing opens by itself.

## Done when

1. A received item with a hostile name or path (for example `..\..\Windows\x.exe`) is stored inside the Received root.
2. Nothing received is run or opened automatically.
3. Every finished received file carries the Windows "came from the internet" mark, and a file the antivirus blocks is shown as blocked and cannot be opened from ComParta.
4. A received program or script shows a warning, and opening it asks a second time.
5. A test receives hostile metadata (path traversal, reserved device names, alternate data streams, right-to-left override characters, thousands of files) and nothing lands outside the Received root or under a disguised name.
6. The protection is present in the release build people install.

## Boundaries

Containment of received content on Windows. Not the receive flow itself (epic Send to a person), not Android sandboxing (epic Android).

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- spec — Planning/initiative-comparta/spec-comparta/spec-comparta.md, CAP-9, Open Questions OQ-2
- architecture — Planning/initiative-comparta/architecture-comparta/architecture-comparta.md, AD-6, Deferred (operating-system sandbox)

## Notes

- OQ-2 answered 2026-10-10 by AD-13. Why not AppContainer: sharing in place (AD-6) needs read access to files wherever the person keeps them, which an AppContainer denies without broad file-system access that removes most of its benefit.
- Waits on epic-drive-like-home because: the Open action this epic guards is built there (story 5).
- Waits on epic-follow-a-transfer because: the `blocked` state is added there (story 3).
- Waits on epic-send-to-a-person because: it provides the receive path that writes into the Received root.
- Paused 2026-10-13: story 1 was started (safer received names, risky extensions, `MaxFiles`) on `claude/safe-received-content`, draft PR #20. Its hostile-metadata engine test was written but never run, and nothing yet tests it through the real wiring, so it is not ready. It waits behind epic 16.
- Built 2026-10-22, stories 1, 2, 4 and 5 (branch `claude/project-thread-9pxli5`, draft PR #37). Story 1: safer received names, including the risky-extension list and the device-name and invisible-character rules. Story 2: the engine refuses over-limit metadata (`MaxFiles`), with the hostile metadata tests passing. Story 4: a received file that can run a program or change the computer carries `risky` in the list and on screen; the first open answers `received.confirmRisky`, the screen asks again naming the file, and only the second yes calls `received.open` with `confirmed`. Story 5: two architecture guards in `GuardTests`. One fails when a process starts outside the Windows adapter and the firewall page; the other fails when anything but the explicit open, reveal and open-folder commands names or calls the opener. Notification clicks only bring the window forward (`AppServices`).
- Not built yet: story 3 (the Windows mark and the antivirus check). Its dependency, epic-follow-a-transfer story 3, is built. What holds it: the spec names `IAttachmentExecute`, a COM interface that cannot be checked on a machine without Windows, and a fake guard would make the protection look present when it is not. Needs a Windows PC to confirm the mark, the antivirus block and the Properties text. The epic stays open until it is built, so its roadmap entry does not move yet.
