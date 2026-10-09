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

To be completed when this epic is planned. The spec is the source: CAP-9 (safe handling of received content).

## Done when

1. A received item with a hostile name or path (for example `..\..\Windows\x.exe`) is stored inside the Received root.
2. Nothing received is run or opened automatically.
3. The chosen Windows sandbox mechanism is in place and a test shows the app cannot write outside its allowed folders.
4. Received files carry whatever marking or scanning OQ-2 decides.
5. The protection is present in the release build people install.

## Boundaries

Containment of received content on Windows. Not the receive flow itself (epic Send to a person), not Android sandboxing (epic Android).

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- spec — Planning/initiative-comparta/spec-comparta/spec-comparta.md, CAP-9, Open Questions OQ-2
- architecture — Planning/initiative-comparta/architecture-comparta/architecture-comparta.md, AD-6, Deferred (operating-system sandbox)

## Notes

- Open question: OQ-2, which Windows sandbox mechanism (MSIX AppContainer or app-level containment only) and whether received files are scanned or marked. Every story here waits on it. The MSIX option also affects epic Install and update.
- Waits on epic-send-to-a-person because: it provides the receive path that writes into the Received root.
