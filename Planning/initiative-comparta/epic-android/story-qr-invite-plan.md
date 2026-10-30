---
title: "Pass an invite with a QR code"
ticket: 9
status: built
---

# Pass an invite with a QR code

## What this story adds

- **Show as QR code** in the details window of a shared file that is ready, on Windows and on the
  phone. The code holds the invite link alone, the same one "Copy link for other apps" copies, so
  ComParta on another phone reads it and so does the phone's own camera app (which then opens
  ComParta through the links of story 6). The code is drawn dark on white in both themes, with the
  border a camera needs, and a link too long for any QR code is said plainly instead of drawn broken.
  The link comes from core through a new command, `shares.inviteLink`, which copies nothing.
- **Scan a QR code** in Receive, on the phone only. The camera is asked for only when the person taps
  Scan; Android asks them about it then, the first time, and never at start-up. The back camera is
  read a few times a second and turned off as soon as a code is read or the person taps Back. What it
  read goes through the same preview as a pasted invite, so the usual "Get this file?" opens, and a
  code that holds no invite gets the same plain words. A refused camera says how to allow it, and
  paste still works.
- **Android:** the manifest may ask for the camera (not required, so phones without one still
  install), and a web view hook (`CameraForScreen`) gives the camera, and only the camera, to the
  app's own page once the person allowed it.
- Third-party notices list the two small parts the screen now carries: `qrcode-generator` (MIT,
  draws the code) and `jsqr` (Apache-2.0, reads it). The notices tool no longer mistakes the Apache
  license's own words ("copyright notice that is included…") for a package's copyright line.

## Verify

- Wiring (Windows CI): a shared file's `shares.inviteLink` through the real router is the link Copy
  link copies; a second real engine given that link gets the file byte for byte, and the preview
  names it. A file no longer shared answers `share.notFound`.
- UI (vitest): the drawn code, read square by square with jsQR, is the copied link; focus goes back
  to the button; English and Portuguese; not offered while the file is not ready; axe passes. Receive
  offers Scan only on a phone, asks for the camera only on the tap, turns it off on Back, and says
  plainly when the camera is refused.
- Real browser (Playwright, `npm run test:phone`): the browser's stand-in camera plays a picture of an
  invite's QR code, and tapping Scan opens "Get this file?" for that file and receives it. The code a
  share shows, as the browser drew it, reads back as that file's invite and opens the confirmation.
- Android emulator (CI): the app can ask for the camera, and starting it does not take it.

## Needs a phone

- A phone scans the QR code shown on a PC (and on another phone) and "Get this file?" opens.
- The first Scan asks about the camera once; refusing it shows the plain message.
- The phone's own camera app reads the code and offers to open ComParta.
