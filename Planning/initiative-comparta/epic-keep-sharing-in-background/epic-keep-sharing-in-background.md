---
type: epic
title: "Closing the window does not stop a transfer"
parent: initiative-comparta
covers: [CAP-17]
after: []
assignee: ""
risk: medium
---

# Closing the window does not stop a transfer

## Description

Today a transfer runs only while the window is open, and the README has to tell people to keep it open. Non-technical people close windows. This epic keeps ComParta running in the notification area while it has work, lets it start with Windows, stops the computer sleeping mid-transfer, and asks before quitting would cut someone off.

## Outcome

Paulo shares a video, closes the window and goes to dinner; Maria gets the whole file.

## Requirements

- CAP-17: keep sharing when the window is closed.
- AD-14: one shell port, `IBackgroundPresence`; the single running copy comes from epic 2 story 6.
- UX design: First run card 3 explains it; the quit confirmation is a dialog.

## Done when

1. With something shared or moving, closing the window leaves an icon in the notification area and the first time says why; with nothing to do, closing quits.
2. The icon's menu opens the window or quits; quitting while someone is receiving a file asks first.
3. With the setting on, ComParta starts with Windows, hidden in the notification area.
4. The PC does not go to sleep while bytes are moving, and may sleep when nothing moves.

## Boundaries

Windows behaviour. The Android equivalent (foreground service) is epic 7, behind the same port.

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- spec — CAP-17
- architecture — AD-14
- UX design — "First run", "Notifications and toasts"

## Notes

- Waits on epic-send-to-a-person because: story 6 there makes the app a single running copy, which the notification-area icon relies on.
- Assumption: start with Windows is on when the person agrees at first run (epic 12 story 3); until then it is off.
