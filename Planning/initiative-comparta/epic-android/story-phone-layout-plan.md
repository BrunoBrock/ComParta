---
title: "Phone layout"
ticket: 4
status: built
---

# Phone layout

## What was already there

Epic 8 story 2 built the phone frame: the bottom navigation, the floating ＋ with Share and Receive,
two cards to a row, and the details window filling the screen below 600 px.

## What this story adds

- **The system back button.** The shell calls `BackgroundUseCases.BackPressed()`; the router sends
  `back.changed`; the screen closes an open menu first, then the window on top (as Escape does, so a
  busy window stays), and when nothing is open sends `app.leave`. Leaving does what closing the window
  does (AD-14): out of sight while something is shared or moving (`IBackgroundPresence.Hide`),
  otherwise the app ends. The Android shell's call to `BackPressed()` arrives with story 1, which
  creates the Android target.
- **Phone checks in a real browser engine** (`src/ui/e2e`, `npm run test:phone`, run by `build.ps1`
  in the Windows CI job): at 360 × 740 with touch, in Edge (the engine of the app's web view), they
  check the bottom navigation and the ＋ above it, no sideways scrolling in grid and list with a long
  name, nothing in a card or row drawn over anything else, every control at least 44 × 44, details as a
  full-screen sheet, Help, Settings and the receive windows inside the screen, Portuguese wording, and
  axe with colour contrast.
- **What those checks found and this story fixed:** the search box, Stop and its menu, the connection
  line and the guided send's check box were under 44 px; the ＋ sat at a fixed height and covered the
  bar once the bar grew (it now belongs to the bar and floats just above it); list rows drew the
  buttons over the name and state (the buttons now take a line of their own); the last files could not
  scroll out from under the ＋.

## Verify

- Automated: `App.phone.test.tsx` (back closes menus and windows, then leaves), the router and core
  tests, the wiring test `The_phones_back_button_reaches_the_screen_and_leaving_keeps_a_shared_file_offered`,
  and `e2e/phone.e2e.ts`.
- Needs a person: one TalkBack pass on a phone (share and receive), once the Android app exists
  (story 1).
