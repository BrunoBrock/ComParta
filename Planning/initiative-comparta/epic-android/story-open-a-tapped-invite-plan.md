---
title: "Open an invite tapped in a chat"
ticket: 6
status: built
---

# Open an invite tapped in a chat

## What this story adds

- **A tapped invite opens ComParta.** The app answers `comparta://` links (earlier builds) and the
  engine's own links, which invites are now (AD-16, amended), from Gmail, WhatsApp, a browser or any
  other app. The link goes where a link clicked on Windows goes (`OpenedInvites`, the same
  `invites.changed` event and `invites.takeOpened` command), so the screen shows the usual "Get this
  file?" confirmation, and nothing is received before the person taps there.
- **A message shared to ComParta** (Share in WhatsApp, then ComParta) is read whole: the invite inside
  it opens the same confirmation, found with the same rules as the paste box. A message with no
  invite is answered in plain words (`invite.notInMessage`) instead of nothing happening.
- **Cold and warm start.** A link that starts the app waits until the screen is ready; a link tapped
  while the app is open goes to the same screen and engine (one running copy, as on Windows). A
  screen Android makes again, or the app opened from the recent apps, does not bring the old link back.

## Verify

- Core: a shared message with a ComParta link, with the engine's link, with both, with none, with
  words and lines around it; the router answers a message without an invite with its own code.
- Wiring (Windows CI): a computer shares a file; its invite tapped, and a chat message holding it,
  each reach "Get this file?" through the real router and engine, and accepting receives the file
  byte for byte. A message without an invite is answered plainly and receives nothing.
- UI: a shared message with an invite opens the confirmation; one without says so.
- Android emulator (CI): `am start` with a tapped link and with a shared message each log that the
  confirmation was asked, the engine still started once; a message without an invite logs the plain
  answer; a link that starts the app from nothing asks too.
- Needs a person with a phone: tap an invite in Gmail; share a WhatsApp message holding an invite to
  ComParta.
