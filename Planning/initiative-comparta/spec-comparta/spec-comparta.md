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

Capabilities marked **[MVP]** form the first release (Windows desktop: add or remove a shared file). The rest are later epics.

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
- **CAP-6 Invite a person**
  - **intent:** A person offers a share to someone they choose, through a channel they already know (email, chat).
  - **success:** The sender produces an invite they can paste into an email or chat; only its holder can obtain the share.
- **CAP-7 Receive a share**
  - **intent:** A person accepts an invite and gets the file.
  - **success:** Opening an invite downloads the content directly from the sender's device into the Received folder, with no account and no hosted service involved.
- **CAP-8 Follow a transfer**
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
- No account, no cloud, no hosted service, no telemetry (AD-9).
- The target user can use a smartphone and email but nothing more complex; any step needing networking or torrent knowledge is a defect.
- Private by default: content reaches only people the sharer chose; public discovery stays off until the privacy model is decided (AD-8).
- Sharing never copies, moves, changes or deletes the original file (AD-6).
- The MVP is Windows desktop only; nothing in it may block the Android shell (AD-1).
- Open source. qBittorrent and other clients are reference only; no code is copied from them.
- Every story is buildable and checkable on its own (AD-12).

## Non-goals

- Public search or discovery of content from strangers.
- Cloud storage, or hosted relays as a requirement.
- Power-user torrent features: opening `.torrent` files or magnet links, trackers, speed limits, queues, RSS.
- In the MVP: sending to or receiving from another person, mobile, an installer.

## Success signal

- **MVP:** a person who has never used a torrent program adds a file and removes it on Windows without help, and the original file is untouched.
- **Product:** two people in different homes move a large family video directly between their devices with no account and no hosted service.

## Assumptions

- The MVP shares single files only; folders (photo albums) come in a later story.
- In the MVP a share is registered and persisted but reaches nobody outside the local network, because AD-8 keeps the network closed. The MVP proves the UI, bridge, core, storage and engine wiring.
- CAP-5 is not in the input; it follows from sharing files in place (AD-6).
- Remove asks for confirmation and states that the original file is kept.
- Adding the same file twice is rejected with a plain message instead of creating a duplicate.

## Open Questions

- OQ-1: How do two people behind home routers find each other with no server, and who is able to download? Blocks CAP-6, CAP-7, CAP-8.
- OQ-2: Which Windows sandbox mechanism is used, and are received files scanned or marked? Blocks CAP-9.
- OQ-3: Which open source license (MIT, Apache-2.0, AGPL-3.0)? Needed before the repository goes public.
- OQ-4: How is the app packaged, signed and updated, given an update check is a network call (AD-9)? Blocks CAP-11.
- OQ-5: Is sharing a whole folder in the MVP or the first follow-up?
