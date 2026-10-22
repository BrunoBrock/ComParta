---
title: "Android shell walking skeleton"
ticket: 1
status: built
---

# Android shell walking skeleton

## What this story adds

- **An Android target in `ComParta.App`** (`net10.0-android`, Android 8 and later), built on any
  machine that is not Windows or with `-p:BuildAndroid=true`. The same `HybridWebView`, `BridgeHost`,
  router, use cases, SQLite storage and MonoTorrent engine run on the phone; the in-memory stub the
  ticket mentions was not needed, because the real engine started on the emulator straight away.
- **Adapters split by platform.** `AppServices` is now partial: the shared start-up stays in
  `AppServices.cs`, the Windows adapters (notification-area icon, firewall, Explorer, thumbnails,
  link registration, pilot import) move to `Platforms/Windows`, and `Platforms/Android` holds the
  Android ones: paths in the app's own storage, an opener through the system, back leaving the app
  or moving it out of sight (story 4's `back.changed`). Thumbnails, notifications and the background
  service do as little as is safe until stories 3, 6 and 7.
- **One engine per app.** Android can make the screen again while the app runs; the engine and
  storage already running are kept rather than started a second time.
- **The `android` job in CI** (Linux): builds and uploads the signed APK (`ComParta-apk`), installs
  it on an Android 14 emulator, starts it, and fails unless the engine starts exactly once, the screen
  sends commands that the router answers, and the app is still running afterwards. It leaves a
  screenshot and the app's log (`android-check`).

## Verify

- Automated: the `android` job above; Windows build, wiring and UI tests unchanged.
- Needs a person with a real phone: install the APK from the `ComParta-apk` artifact, open it, see
  the shares list, share nothing yet (picking files is story 2), and check that back leaves the app.
