---
title: "Keep going with the screen off"
ticket: 7
status: built
---

# Keep going with the screen off

## What this story adds

- **A foreground service while bytes move** (`MovingService`, data sync kind, AD-14). Core decides
  when it runs (`BackgroundUseCases`): it starts when bytes start moving and stops a minute after
  they stop; Android only shows it. Windows does nothing new: its speed bar and tray icon already
  do this job.
- **A notification in plain words** with a progress bar: "Receiving “Natal.mp4”: 45 %", "Sending 2
  files: 10 %", "Sending and receiving 3 files: 40 %", in English and pt-BR. Its **Stop** button runs
  the same stop as the speed bar's Stop, kept after a restart, and the screen shows it stopped.
  Tapping the notification opens ComParta.
- **The notification permission** (Android 13 and later) is asked the first time something moves,
  after one sentence saying why. Without it the service still runs; only the notification is hidden.
- **Android 15's six-hour limit:** when the phone says the time is up, everything pauses (not kept as
  the person's own stop), the notification says "Paused to save battery. Open ComParta to continue.",
  and opening the app continues. A debug build can shorten the limit:
  `adb shell am start -n org.comparta.app/org.comparta.app.MainActivity --ei limitSeconds 60`.

## Verify

- Core: the notification appears on the first bytes, names the file with its percent, counts several
  files, goes a minute after the last bytes; Stop stops sending and receiving and is remembered; the
  time-out pauses, says so, and opening the app continues without undoing a stop the person chose.
- Wiring (Windows CI): a real file from a second engine arrives through the real router; the presence
  port shows its name and progress and goes a minute after; Stop ends with `activity.changed` showing
  both stopped and nothing more arriving; the time-out pauses and opening the app finishes the file.
- Android emulator (CI): a computer-side sender (`tests/ComParta.PhoneCheck`) shares 300 MB at
  1 MB/s; the phone opens its invite, taps "Get the file", the service starts with its notification,
  the screen is turned off and the bytes keep arriving for 40 seconds; when the sender stops, the
  service is gone a minute later.
- Needs a person with a phone: a 1 GB transfer finishes with the screen off; the notification shows
  progress and Stop works; the service is gone a minute after; the permission question reads well.
