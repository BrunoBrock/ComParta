---
type: epic
title: "A person uses ComParta on an Android phone"
parent: initiative-comparta
covers: [CAP-10]
after: []
assignee: ""
risk: high
---

# A person uses ComParta on an Android phone

## Description

The same core and the same UI run inside an Android shell, so a person shares and receives from their phone with the words and flows they know from the desktop.

## Outcome

A relative who only owns a phone sends and receives files with family members on either phones or PCs.

## Requirements

To be completed when this epic is planned. The spec is the source: CAP-10 (use on a phone).

## Done when

1. On an Android phone a person adds and removes a share using the phone's own file picker.
2. A phone and a Windows PC exchange a file in both directions through an invite.
3. The screens fit a phone and use the same words as desktop.
4. What happens to sharing when the app is in the background or the phone sleeps is decided and explained to the person in plain words.
5. The app installs on a phone from a release build.

## Boundaries

The Android shell and phone layout. Not iOS. No change to core rules; anything Android-only is a shell adapter (AD-1).

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- spec — Planning/initiative-comparta/spec-comparta/spec-comparta.md, CAP-10
- architecture — Planning/initiative-comparta/architecture-comparta/architecture-comparta.md, AD-1, AD-7, Deferred (Android shell, background sharing on phones)

## Notes

- Open question: background sharing on phones (battery, mobile data, operating-system limits) is deferred in the architecture; settle it before planning.
- Open question: OQ-1 is answered for the first release by AD-8, but a phone on a mobile network usually has no inbound path (carrier-grade NAT) and cannot open a router port, so it can receive but cannot be reached; settle what a phone can offer before planning.
- Waits on epic-share-a-file because: it provides the core, contracts and UI the Android shell hosts.
- Waits on epic-send-to-a-person because: it provides the invite and receive flows to carry over.
