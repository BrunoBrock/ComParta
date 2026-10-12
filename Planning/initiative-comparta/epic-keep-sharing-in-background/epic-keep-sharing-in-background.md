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
- Built 2026-10-12, all four stories, on `claude/keep-sharing-in-background`. Decisions taken while building:
  - The notification-area icon is plain Win32 (`Shell_NotifyIcon` on its own thread, in `ComParta.Shell`), not a NuGet package, so it builds and is compile-checked on any system. It stays for as long as the app runs, not only while the window is hidden; closing the window with nothing to do still quits.
  - `HasActiveWork` counts a paused transfer as work (it is not finished), and does not count failed or blocked ones.
  - `app.quit` returns `{ quitting, moving }` instead of an `app.busy` error code, so the names of the files that would stop can travel with the answer. Quitting from the icon raises `quit.changed`, which shows the window and the same dialog.
  - "Moving" for the quit question means a shared file someone is getting now, or a file in the Receiving state. A transfer still looking for the other computer does not ask.
  - The computer stays awake through a Windows power request, released 45 seconds after the last bytes moved.
  - Start with Windows is a per-user Run entry with `--background`; it is off until the person turns it on in Settings (epic 12's first run will ask). The MSIX startup task is left to epic 5.
  - The first hide explains itself once, ever, with a notification; the icon's menu words come from the `notify.*` catalog keys so they follow the language.
- Left for a Windows PC: the icon appears and its menu works; closing hides and reopening shows; `--background` starts hidden (there may be a short flash); `powercfg /requests` lists ComParta during a transfer; the Run entry appears and disappears with the setting.
