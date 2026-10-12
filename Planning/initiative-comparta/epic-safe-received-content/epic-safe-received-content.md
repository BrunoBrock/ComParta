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
