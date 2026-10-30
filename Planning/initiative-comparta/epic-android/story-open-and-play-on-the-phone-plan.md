---
title: "Open and play on the phone"
ticket: 12
status: built
---

# Open and play on the phone

## What this story adds

- **The phone's web view reads opened files** (`Platforms/Android/MediaStream.cs`): every request for
  the media address goes through `BridgeRouter.ServeMedia`, as on Windows, with parts of a file
  (206, 416) so a video can be seeked. It uses MAUI's own `HybridWebView.WebResourceRequested`, so
  neither `MainActivity.cs` nor the manifest changes. A song or video starts when the viewer opens,
  as on Windows.
- **A shared item with no path opens** (AD-20): an item picked on a phone is a `content://`
  reference with no file name in it, so core now asks the shell the name it knows the item by
  (`IFileSystem.NameOf`) to tell its kind, and reads its first bytes and its parts through the same
  port. Before this, every picked picture, song and video was refused. Windows is unchanged: the
  name there is the end of the path, as before.
- **The fallback** was already there: a file the phone cannot play (an MKV or HEVC video, a
  document) says so, and "Open the folder" opens the Files app at Downloads/ComParta.
- **On a phone** the viewer fills the screen, its Close is 48 px, and the back button closes it (the
  video stops) without leaving the app.

## Verify

- Core: a picked item with no path opens by the name the phone gives it, whole and in parts, and 416
  past the end; MKV, PDF and no-extension items and a taken-back permission give the fallback.
- Wiring (`PhonePickerTests`, Windows CI): a photo and a video picked the phone's way go `media.open`
  as JSON through the real router and are read whole and with a seek; a video the phone received
  from a computer over the real engine does the same; a picked MKV ends in `media.notPlayable` and
  "Open the folder" reaches the reveal port.
- Screen: the back button closes the viewer and the cannot-play message; the message in pt-BR; at
  360 px in a real browser a shared video plays and a received photo shows, full screen, and Close
  goes back.
- Android emulator in CI (`check-android.sh`, `drive-phone.mjs`): another device on the runner
  (`tools/ComParta.CheckSender`, the real engine) shares a photo, an H.264 video and an MKV; the app
  receives them through its own bridge, the photo decodes and the video plays and seeks in the
  phone's web view, the log shows the media answered 200 and 206, and the MKV opens the Files app.
  The check reaches the screen over the web view's debugging socket, which the app opens only when
  the check puts a marker in its own folder (`ScreenCheck.cs`, Android 11 and later).
- Needs a real phone: a received phone video plays and seeks; a song keeps playing while the viewer is
  open; an MKV or HEVC video falls back and the Files app opens at it; a picked photo from the
  Gallery opens.
