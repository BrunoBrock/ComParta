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

The spec is the source: CAP-11 (install and stay current). AD-17 answers most of OQ-4: an MSIX package through the Microsoft Store, which signs and updates it; the same build as a zip on GitHub Releases; no update check in the app; releases built only by CI from a tag. Decision D-1 (license) and D-2 (channel) are Bruno's.

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

- OQ-4 answered in part by AD-17; the channel is decision D-2 (recommendation: Microsoft Store, free for individual developers since September 2025, plus a GitHub zip). Stories 3, 4 and 6 carry it as `unknown`.
- OQ-3 is decision D-1 (recommendation: Apache-2.0); story 1 carries it as `unknown`.
- Risk: an MSIX app's writes to `%LOCALAPPDATA%` are redirected to the package's own folder, so the pilot's unpackaged data would be invisible to the Store build; story 5 imports it.
- Assumption: the Store's own listing needs a privacy statement URL; a file in the repository ("ComParta collects nothing") serves.
- Waits on epic-share-a-file because: it provides the Windows app to package.
