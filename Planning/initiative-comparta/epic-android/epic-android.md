---
type: epic
title: "A person uses ComParta on an Android phone"
parent: initiative-comparta
covers: [CAP-10, CAP-15, CAP-16, CAP-17]
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

The spec is the source; stories cite its ids.

- CAP-10: the same things on Android with the same words.
- CAP-15 (Android part): ComParta in the phone's share menu.
- CAP-16 (Android part): invites open from a tapped link; an invite shown on a PC can be scanned as a QR code.
- CAP-17 (Android part): a foreground service with a progress notification while a transfer runs.
- AD-14 (background presence), AD-20 (content read through a platform port), AD-7 amended (share menu as an entry point); the UX design's phone layout.

## Done when

1. On an Android phone a person adds and removes a share using the phone's own file picker.
2. A phone and a Windows PC exchange a file in both directions through an invite.
3. The screens fit a phone and use the same words as desktop.
4. While a transfer runs, a notification shows its progress and the transfer continues with the screen off; when nothing moves the app stops running in the background, and the help page says so in plain words.
5. Sharing from the Gallery's share menu and opening an invite tapped in WhatsApp both work.
6. On mobile data, a transfer over 50 MB asks first.
7. The app installs on a phone from Google Play or a signed APK (decision D-4).

## Boundaries

The Android shell and phone layout. Not iOS. No change to core rules; anything Android-only is a shell adapter (AD-1).

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- spec — Planning/initiative-comparta/spec-comparta/spec-comparta.md, CAP-10
- architecture — Planning/initiative-comparta/architecture-comparta/architecture-comparta.md, AD-1, AD-7, Deferred (Android shell, background sharing on phones)

## Notes

- Background sharing settled 2026-10-10 by AD-14: a foreground service of type data sync only while bytes move. Android 15 limits that type to 6 hours in 24; story 7 handles the time-out by pausing with a notification and resuming when the app is opened.
- Shared content on Android is a content URI with a persisted read grant, read through `IContentSource` (AD-20); this needs a MonoTorrent piece writer backed by that port (story 2).
- Phones on mobile data are almost always behind carrier-grade NAT for IPv4; epic 9's IPv6 work matters most here.
- Minimum Android version: 8.0 (API 26), which is what .NET MAUI 10 supports; target the newest API Google Play requires at submission time.
- Waits on epic-share-a-folder because: the share model with items and `ContentRef` is final there.
- Waits on epic-keep-sharing-in-background because: `IBackgroundPresence` is defined there.
- Waits on epic-connect-between-homes because: reachability facts and IPv6 are reused.
- Waits on epic-share-a-file because: it provides the core, contracts and UI the Android shell hosts.
- Waits on epic-send-to-a-person because: it provides the invite and receive flows to carry over.
