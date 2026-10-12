---
id: SPEC-comparta
companions:
  - '../architecture-comparta/architecture-comparta.md'
sources:
  - '../../ReadMe.md'
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate. Source documents listed in frontmatter are for traceability — consult them only if you need narrative rationale or prose color this contract intentionally omits.

# ComParta

## Why

A vision to realize, answering a pain. Sharing large personal files (family videos, photo albums, documents) today means uploading them to a cloud provider, with storage limits, subscriptions, privacy trade-offs and dependence on a third party. Peer-to-peer transfer removes all of that, but existing torrent programs are built for power users and are confusing and unsafe for everyday people. ComParta lets parents, grandparents, relatives and friends, people who can use a smartphone and email but nothing more complex, send files directly to each other.

## Capabilities

Capabilities marked **[MVP]** form the first release (Windows desktop: add or remove a shared file, and send it to another person's computer with an invite). The rest are later epics. CAP-6, CAP-7 and a first form of CAP-8 were pulled into the first release on 2026-10-08.

- **CAP-1 [MVP] Add a share**
  - **intent:** A person picks a file on their PC and it joins the list of things they share.
  - **success:** After picking a file in the Windows file dialog it shows in the list with its name and size; the original file is byte-identical and in the same place.
- **CAP-2 [MVP] See my shares**
  - **intent:** A person sees everything they currently share.
  - **success:** The list shows each share's name and size, shows an empty-state message when there are none, and is the same after closing and reopening the app.
- **CAP-3 [MVP] Remove a share**
  - **intent:** A person stops sharing a file without losing it.
  - **success:** After confirming, the share leaves the list and stays gone after restart; the original file still exists unchanged.
- **CAP-4 [MVP] Plain-language experience**
  - **intent:** A person understands every screen and message without torrent knowledge.
  - **success:** No UI string contains seed, peer, leech, swarm, tracker, magnet, torrent, hash or DHT; every error code has a plain-language message telling the person what to do.
- **CAP-5 [MVP] Missing-file handling**
  - **intent:** A person learns when a shared file was moved, renamed or deleted outside the app.
  - **success:** Such a share is marked unavailable in the list and can still be removed.
- **CAP-6 [MVP] Invite a person**
  - **intent:** A person offers a share to someone they choose, through a channel they already know (email, chat).
  - **success:** The sender copies an invite they can paste into an email or chat, and it works from outside the sender's home network, not only on the same one. The sender is told in plain words whether the share can be reached from outside, and what to do when it cannot. Someone without the invite cannot ask for the share (see AD-8 for the limits of this).
- **CAP-7 [MVP] Receive a share**
  - **intent:** A person accepts an invite and gets the file.
  - **success:** Pasting an invite brings the content directly from the sender's device, in another home or on the same network, into the Received folder, with no account and no hosted service involved, and never overwrites a file already there.
- **CAP-8 [MVP, first form] Follow a transfer**
  - **intent:** A person sees whether a file is on its way, done or stuck.
  - **success:** Each transfer shows progress and a plain-language state, and says what to do when it cannot proceed (for example, the other person's device is off).
- **CAP-9 Safe handling of received content**
  - **intent:** Received files cannot harm the rest of the computer.
  - **success:** Received content is written only under the Received root, hostile file names cannot escape it, and nothing received is run automatically.
- **CAP-10 Use on a phone**
  - **intent:** A person does the same things on an Android phone.
  - **success:** Add, remove, invite and receive work on Android with the same words and flows as on desktop.
- **CAP-11 Install and stay current**
  - **intent:** A non-technical person installs and updates the app without technical steps.
  - **success:** Installation on Windows is one download with no extra prerequisites, and updating needs no manual uninstall.
- **CAP-12 Brazilian Portuguese**
  - **intent:** A person uses the app in Brazilian Portuguese.
  - **success:** Every UI string and error message has a pt-BR translation selectable in the app.

## Constraints

- AD-1 to AD-12 in the architecture companion are binding.
- No account, no cloud, no hosted service, no telemetry (AD-9). The sender's public address comes from the router, never from a third-party service.
- The target user can use a smartphone and email but nothing more complex; any step needing networking or torrent knowledge is a defect.
- Private by default: content reaches only people who hold its invite; nothing is listed or searchable. The invite itself is the secret: anyone holding it can download, so the app never presents it as safe to post publicly. The limits of this are stated in AD-8.
- Sharing never copies, moves, changes or deletes the original file (AD-6).
- The MVP is Windows desktop only; nothing in it may block the Android shell (AD-1).
- Open source. qBittorrent and other clients are reference only; no code is copied from them.
- Every story is buildable and checkable on its own (AD-12).

## Non-goals

- Public search or discovery of content from strangers.
- Cloud storage, or hosted relays as a requirement.
- Power-user torrent features: opening `.torrent` files, trackers, speed limits, queues, RSS. (An invite is technically a magnet link, but the app never calls it that.)
- In the MVP: mobile, an installer.

## Success signal

- **MVP:** a person who has never used a torrent program adds a file on Windows, sends its invite to someone, and that person receives the file on their own PC; the original file is untouched.
- **Product:** two people in different homes move a large family video directly between their devices with no account and no hosted service.

## Assumptions

- The MVP shares single files only; folders (photo albums) come in a later story.
- Both people keep ComParta open during a transfer. The sender's router either opens the port automatically or the person opens it by hand; a router with no inbound path (carrier-grade NAT) is not solved in this release (see the architecture's Deferred table).
- Unfinished received data is kept in a hidden folder inside the Received folder and moved to its final name when complete.
- CAP-5 is not in the input; it follows from sharing files in place (AD-6).
- Remove asks for confirmation and states that the original file is kept.
- Adding the same file twice is rejected with a plain message instead of creating a duplicate.

## Open Questions

- OQ-1: Answered for the first release by AD-8: the invite carries the sender's local and public address, the app asks the router to open the port automatically and tells the sender whether the share can be reached from outside, and devices also find each other through local discovery and the public lookup network. Still open: a stronger model where only chosen people can download; hardening (upload limits, invites that expire, an option to turn the public lookup network off); reliable connection through routers with no inbound path; and what the app tells a person whose router will not open the port by itself.
- OQ-2: Which Windows sandbox mechanism is used, and are received files scanned or marked? Blocks CAP-9.
- OQ-3: Which open source license (MIT, Apache-2.0, AGPL-3.0)? Needed before the repository goes public.
- OQ-4: How is the app packaged, signed and updated, given an update check is a network call (AD-9)? Blocks CAP-11.
- OQ-5: Is sharing a whole folder in the MVP or the first follow-up?
