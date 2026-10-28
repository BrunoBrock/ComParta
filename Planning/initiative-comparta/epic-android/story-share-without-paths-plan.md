---
title: "Share from a phone without file paths"
ticket: 2
status: built
---

# Share from a phone without file paths

## What this story adds

- **`IContentSource` (AD-20)** in core's ports: is it there, its size, its name, open it for reading at
  any position. A content reference is whatever the shell hands over: a full path on Windows, or the
  `content://` reference a phone's picker gives.
- **The engine reads every shared file through it.** A piece writer (`ContentPieceWriter`) sits in
  front of the engine's disk: files that are shared are read through the content source and are
  never written or moved; received files go to the disk as before. The engine is told each shared
  file sits under a folder of its own that is never made, so it no longer moves its view of a file
  next to the original. The description of a new share is made the same way. On Windows the content
  source reads files on disk (`FileContent`), so nothing changes for the person there.
- **Core keeps picked items as they are.** `IFileSystem` gains `FullName`, `NameOf` and `Inside`, with
  the old path behaviour as defaults; core no longer takes a picked item apart as a path. Several
  picked files that are not paths go under their own names, made unique ("IMG_1 (2).jpg").
- **Android:** `AndroidPicker` opens the system picker for one or several files and for a folder,
  and keeps each item's read permission across restarts. `AndroidStorage` answers the file-system and
  content-source questions with the phone's content resolver, walks a picked folder for its files,
  and opens each item as a file the engine can read anywhere in.
- **Details:** a phone's item has no folder a person would know, so the router sends no folder and
  the details sheet leaves the "Where" line out.

## Verify

- Automated: engine tests through the port with items that have no path (one file, a folder, a
  permission gone after a restart, and files on disk unchanged); core tests with a phone-like
  storage; wiring tests `PhonePickerTests` (picked video, folder and two same-named photos reach a
  computer byte-identical; after a restart the video is shared again and the taken-back song is
  marked); the UI test for details without a folder; the Android build and emulator start in CI.
- Needs a person with a phone: pick a video and a folder, receive both on a PC byte-identical,
  restart the phone and check both are still offered. Items only in the cloud (not on the phone) are
  refused as unreadable for now.
