---
type: epic
title: "A person adds friends, sees what they share, and gets files only from friends unless they choose otherwise"
parent: initiative-comparta
covers: [CAP-24, CAP-31]
after: []
assignee: ""
risk: high
---

# A person adds friends, sees what they share, and gets files only from friends unless they choose otherwise

## Description

Until now every send is an invite copied into a chat, anyone holding the invite can get the file, and a ComParta accepts an invite from anyone. This epic adds **friends**. Two devices become friends by keys kept on each device, with no account (AD-9, AD-24): accepting an invite from someone new asks to add them as a friend first, and "Add a friend" works without a file too. A person then sees everything a friend shares with them in "Shared with me" and gets it with one click, and sends to friends from inside ComParta without copying anything.

A new install accepts files, and requests to connect, only from friends (CAP-31). The first Settings screen has one switch, "Accept files from people who are not my friends", off by default. It is the most open design in the product, so it starts with a spike and its later stories are refined after it.

Asked by the maintainer on 2026-10-20 (roadmap decision P-19). Moved from R3 into R1, so the first release people install already accepts files only from friends.

## Outcome

Maria adds Paulo as a friend from his first invite. From then on she sees "Paulo shared Natal 2024 with you" in her ComParta without opening WhatsApp, and a stranger who gets hold of a link, or tries to send her a file, gets nothing through unless she turns the switch on herself.

## Requirements

- CAP-24: friends and their shared files. CAP-31: only friends by default, with a switch on the first Settings screen.
- AD-24 (friends are device keys; core owns the friend list and the known-sources rule), AD-9 (identity is a key pair on the device, no account), AD-8 (revised by the spike), AD-2 (all network work behind the engine port), AD-5 (the person sees "friend", never a technical word).
- UX design: "Friends" and "Shared with me" in the information architecture; their screens and the friend request are designed in story 1.

## Done when

1. The spike's design is accepted by the maintainer and written into the architecture (AD-24 settled, AD-8 revised).
2. Accepting an invite from someone new asks to add them as a friend, on both devices, with no account; "Add a friend" works without a file.
3. Everything a friend shares with this person appears in their Shared with me when both devices are on, and they get any of it with one click.
4. On a new install, an invite or a request to connect from someone who is not a friend is refused with a plain message, and no byte of their file is written; a friend's invite still works.
5. The first Settings screen shows the unknown-sources switch, off by default; turning it on asks once, in plain words, and invites from anyone work again.
6. Only the chosen friends' devices can get a share sent this way; a captured link alone is not enough, and its real fingerprint is not on the public lookup network.
7. A wiring test in Windows CI drives adding a friend, a friend's share arriving, a stranger's invite refused, and the switch, from the bridge command through the router, use cases, real engine and storage (CLAUDE.md testing rule).

## Boundaries

Friends, devices, private delivery and the known-sources rule. Not group chats, comments or anything social; not a contact list imported from the phone. Folders shared with friends are epic 17.

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- spec — CAP-24, CAP-31, Open Questions OQ-1, OQ-8
- architecture — AD-2, AD-8, AD-9, AD-24, Deferred (stronger privacy model)
- roadmap — R1, P-19, D-7

## Notes

- Candidate ideas for the spike, not decisions: the invite carries the sender's device key, so accepting it is the friend request; a long-lived pairing key per pair of devices; "new share for you" and "what you share with me" messages over the BitTorrent extension protocol between friends' devices when both are online; announcing a value derived from a secret instead of the real fingerprint.
- Default taken 2026-10-20 (coordinator, where the ask was open): "unknown sources" covers both files and requests to connect from people who are not friends, and the switch lives on the first Settings screen.
- Default taken 2026-10-20, to confirm (D-7): download links and download files from websites (epic 14) also count as unknown sources, so with the switch off ComParta says so and points to the switch instead of downloading.
- Sharing to the open internet (epic 16, any torrent app) is untouched: the rule is about what this ComParta accepts, not about what it offers.
- Changed 2026-10-20: no longer waits on epic 7; the spike designs friends for phones and PCs from the start.
