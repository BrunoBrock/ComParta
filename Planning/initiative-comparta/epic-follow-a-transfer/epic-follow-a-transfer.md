---
type: epic
title: "A person sees how a transfer is going"
parent: initiative-comparta
covers: [CAP-8, CAP-20]
after: []
assignee: ""
risk: medium
---

# A person sees how a transfer is going

## Description

Sender and receiver both see, in plain words, whether a file is on its way, finished or stuck, and what to do when it is stuck.

## Outcome

A person never has to wonder whether a file arrived or ask someone technical why it has not; each transfer explains itself.

## Requirements

The spec is the source; stories cite its ids.

- CAP-8: the receiver's progress and states exist since the first build; R1 adds the sender's view, time left, the 'waiting', 'noSpace' and 'blocked' states, and progress that is honest after a restart.
- CAP-20: system notifications when something arrives or someone finished getting a file.
- UX design: the States tables, "Notifications and toasts", "Stuck transfer".

## Done when

1. A running transfer shows its progress on both devices: the receiver as a percentage and time left, the sender as "Sending to {n}" with a percentage.
2. A finished transfer says so, and the receiver can open the Received folder from it.
3. When the other device is off or unreachable, the transfer says so in plain words and resumes when it returns.
4. No transfer state uses a torrent term.
5. A file arriving, or someone finishing a shared file, shows a Windows notification that opens the item.
6. Running out of disk space pauses the transfer with its own state and it continues when space is freed.
7. The feature is in the release build.

## Boundaries

Showing and explaining transfer state. Not speed limits, queues or other power-user controls (spec non-goals).

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- spec — Planning/initiative-comparta/spec-comparta/spec-comparta.md, CAP-8
- architecture — Planning/initiative-comparta/architecture-comparta/architecture-comparta.md, AD-4, AD-5, AD-11

## Notes

- OQ-1 is answered for R1 by AD-8; the stuck states are fixed in the UX design's States tables.
- Assumption: the engine can tell, per offered share, how many connected devices are receiving it and how much of it each has; story 1 checks this in MonoTorrent and falls back to bytes sent if not.
- Waits on epic-drive-like-home because: the states render in its cards and details panel.
- Waits on epic-send-to-a-person because: it provides transfers between two devices.
- Built 2026-10-12, story 1: the assumption holds. Each second the engine reads, per share, the connected devices that lack part of the file (their count and average percentage). A device counts as finished when it has the whole file and something came from this computer, or when it leaves after this computer sent it at least what it lacked, because a device that finishes often leaves before saying it has the last parts. Counts only, kept in memory: after a restart a share says "Ready to send" until someone connects again (story 4's rule for shares). `ShareDto` carries `activity` (`idle`, `sending`, `sent`), `receiverCount`, `percent`, `finishedCount` and `secondsLeft`. Decided: the chip reads "Sending to {n}" and "Sent {n} times", the sentence "Sending to 1 person: 45 %" with time left, as in the UX States table.
- Built 2026-10-12, story 2: `TimeLeft` in core smooths the speed (each second weighs 30 %) and says nothing after 10 seconds without progress. Transfers use it instead of the engine's speed; a sending share uses the bytes sent and what all its receivers still lack. The wording existed since epic 2; pt-BR now has a test for each.
- Built 2026-10-12, story 3: core decides `waiting` (no connection and nothing received for 2 minutes, with the engine's per-transfer connection count as contact) and `noSpace`, which comes either from the engine (a write failed with the disk-full error) or from core seeing less free room than what is left, in which case core pauses the transfer first so nothing half-written is lost. Every minute it looks again and continues when there is room; it is not stored as the person's pause. `blocked` exists in the contract and on screen for epic 3 to use. Found while testing: with MonoTorrent's write cache a part that failed to reach a full disk could still count as received and the finished file came out wrong, so the engine now runs without that cache and checks the data on disk again before continuing after a full disk. The sender's name in "Waiting for {sender}" is not known yet, so it reads "the other person".
