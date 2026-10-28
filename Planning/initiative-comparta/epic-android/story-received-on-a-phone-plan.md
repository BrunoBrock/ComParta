---
title: "Received files on a phone"
ticket: 3
status: built
---

# Received files on a phone

## What this story adds

- **Received files go to Downloads/ComParta** on Android 11 and later (`AndroidPaths.ReceivedRoot`),
  where the Gallery and the Files app see them and where they stay if the app is removed. The engine
  already keeps unfinished data in the hidden `.in-progress` folder under the Received root and moves
  each finished item out of it, so the phone lists an item only once it is complete. Nothing is
  copied, and the engine keeps sharing the received file from where it is (AD-20, amended). Before
  Android 11 that needs a storage permission, so received files stay inside the app there.
- **Opening a received file** asks the phone for its own reference to the file (which also makes sure
  the Gallery lists it) and opens it with the app the phone uses for that kind of file. A file inside
  the app still goes through MAUI's launcher.
- **"Show in folder" and "Open my received files"** open the Files app at Downloads/ComParta, or the
  phone's list of downloads where the Files app cannot go there.

## Verify

- Automated: the Android emulator check in CI now also checks that Downloads/ComParta is made when
  the app starts; the engine, core and wiring tests for receiving are unchanged and still pass.
- Needs a person with a phone: receive a photo from a PC and find it in the Gallery; open it from
  ComParta; receive a folder and find its structure in Files under Downloads/ComParta; "Show in
  folder" opens Files there.
