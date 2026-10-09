---
type: epic
title: "A non-technical person installs and updates the app"
parent: initiative-comparta
covers: [CAP-11]
after: []
assignee: ""
risk: medium
---

# A non-technical person installs and updates the app

## Description

ComParta reaches people outside the developer's machine: one download installs it on Windows with nothing else to set up, and a newer version replaces the old one without manual steps.

## Outcome

A grandparent installs ComParta from a link a relative sent, without help.

## Requirements

To be completed when this epic is planned. The spec is the source: CAP-11 (install and stay current).

## Done when

1. On a clean Windows PC, one downloaded file installs the app with no extra prerequisite.
2. Windows does not show an unknown-publisher warning, or the chosen answer to OQ-4 says why that is accepted.
3. Installing a newer version keeps the person's shares and needs no manual uninstall.
4. Whatever tells the person about a new version respects AD-9 (no hosted service of ours, nothing sent).
5. Each release is built and published by an automated, repeatable process.

## Boundaries

Windows packaging, signing and updates. Not Android distribution (epic Android).

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- spec — Planning/initiative-comparta/spec-comparta/spec-comparta.md, CAP-11, Open Questions OQ-3, OQ-4
- architecture — Planning/initiative-comparta/architecture-comparta/architecture-comparta.md, AD-9, Deferred (packaging, code signing and updates)

## Notes

- Open question: OQ-4, MSIX or installer, code signing, and how an update check fits AD-9. Every story here waits on it.
- Open question: OQ-3, the open source license must be chosen before a public release.
- Waits on epic-share-a-file because: it provides the Windows app to package.
