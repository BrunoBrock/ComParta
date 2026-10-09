---
type: epic
title: "The app speaks Brazilian Portuguese"
parent: initiative-comparta
covers: [CAP-12]
after: []
assignee: ""
risk: low
---

# The app speaks Brazilian Portuguese

## Description

Every screen and message is available in Brazilian Portuguese, with the same plain-language rule as English.

## Outcome

A Portuguese-speaking relative uses ComParta entirely in their own language.

## Requirements

To be completed when this epic is planned. The spec is the source: CAP-12 (Brazilian Portuguese).

## Done when

1. Every UI string and error message has a pt-BR translation; an automated check fails when one is missing.
2. The person can choose the language in the app, and the choice survives a restart.
3. The pt-BR text contains no torrent term in either language.
4. Sizes and dates follow Brazilian formatting.
5. The translation is in the release build.

## Boundaries

The pt-BR catalog and language selection. Not other languages.

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- spec — Planning/initiative-comparta/spec-comparta/spec-comparta.md, CAP-12, CAP-4
- architecture — Planning/initiative-comparta/architecture-comparta/architecture-comparta.md, AD-5, AD-11, Consistency Conventions

## Notes

- Assumption: the app starts in the Windows display language when it is supported, English otherwise.
- Waits on epic-share-a-file because: it provides the translation catalog and the check that every string goes through it.
