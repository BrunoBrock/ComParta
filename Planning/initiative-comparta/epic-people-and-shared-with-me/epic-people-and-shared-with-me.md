---
type: epic
title: "A person sends to the people they know, and only they can get it"
parent: initiative-comparta
covers: [CAP-24]
after: []
assignee: ""
risk: high
---

# A person sends to the people they know, and only they can get it

## Description

Up to R2 every send is an invite copied into a chat, and anyone holding the invite can get the file; the content's fingerprint is visible on the public lookup network (AD-8's accepted trade-off). This epic adds "My people": after the first invite, two devices remember each other by keys kept on the device (AD-9), later shares can be sent to a person from inside ComParta and appear in their "Shared with me", and content is found on the lookup network without exposing its fingerprint. It is the most open design in the product, so it starts with a spike and its later stories are refined after it.

## Outcome

Maria sees "Paulo shared Natal 2024 with you" in her ComParta without opening WhatsApp, and a stranger who sees the lookup network's traffic learns nothing about what the family shares.

## Requirements

- CAP-24: people I share with.
- AD-8 (to be revised by the spike), AD-9 (identity is a key pair on the device), AD-2 (all of it behind the engine port or a new port beside it).
- UX design: "My people" and "Shared with me" in the information architecture; their screens are designed in story 1.

## Done when

1. The spike's design is accepted by the maintainer and written into the architecture as a revised AD-8 and a new decision.
2. Accepting an invite can add the sender to My people, on both devices, with no account.
3. A share sent to someone in My people appears in their Shared with me when both devices are on, and they get it with one click.
4. Only the chosen people's devices can get such a share; a captured invite alone is not enough.
5. The content's real fingerprint no longer appears on the public lookup network for shares made this way.

## Boundaries

People, devices, and private delivery. Not group chats, comments or anything social; not a contact list imported from the phone.

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- spec — CAP-24, Open Questions OQ-1
- architecture — AD-2, AD-8, AD-9, Deferred (stronger privacy model)
- roadmap — R3

## Notes

- Candidate ideas for the spike, not decisions: announce a value derived from a secret carried in the invite instead of the real fingerprint; a long-lived pairing key per device pair; delivery of "new share for you" messages over the BitTorrent extension protocol between paired devices when both are online.
- Waits on epic-send-to-a-person because: pairing rides on the invite flow.
- Waits on epic-android because: R3 starts after R2, and pairing must work between phones and PCs from the start.
