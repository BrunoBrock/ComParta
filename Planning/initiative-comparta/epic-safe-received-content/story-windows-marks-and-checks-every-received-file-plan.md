---
title: "Windows marks and checks every received file"
ticket: 3
status: in-progress
---

# Windows marks and checks every received file

## What this story adds

- **`IReceivedFileGuard` (AD-13)** in core, in its own file `src/ComParta.Core/ReceivedFileGuard.cs`:
  one call that takes the full path of one finished received file and answers `Passed` or `Blocked`.
  It may take seconds, so core always calls it off the UI thread. The same file holds
  `NoReceivedFileCheck`, the stand-in for systems with no such check (Android, where the platform's
  own sandbox applies). Its name says that it checks nothing.
- **Core runs the guard before the item shows as received.** When the engine reports a receive as
  done, `TransferUseCases` does not show it as done yet. It saves the transfer as arrived but not yet
  checked, keeps showing it as receiving at 100 %, and hands each file to the guard once on a
  background task. A single file is checked as itself. For a folder (epic 11), every file inside is
  checked, as `IFileSystem.ListFiles` lists them. Files left out under More options are not on
  disk, so nothing checks them. If every file passes, the transfer becomes `done`. If any file is
  blocked, it becomes `blocked`, and a public download stops being shared on. Repeated done reports
  are ignored while a check runs, and once it has run.
- **The outcome is remembered.** `Transfer` gains `Check` (`ReceivedCheck`: `Pending`, `Passed`,
  `Blocked`), stored in a new `check_result` column (schema version 9). Rows from before this story
  count as passed, so files received earlier are not looked at again. After a restart, `Passed`
  shows as done and `Blocked` shows as blocked. Neither runs the guard again. `Pending` (the app
  closed in the middle of a check) runs the check once more.
- **Nothing tells the person a blocked file arrived.** The "arrived" notification fires only on the
  change to `done`, which a blocked transfer never reaches. Open, show in folder, open with,
  thumbnails and the viewer already work only on `done` items (`ReceivedUseCases.Find`).
- **Windows adapter** `src/ComParta.Shell/AttachmentGuard.cs`: calls Windows' attachment services
  (`IAttachmentExecute`) on its own single-threaded thread, with a fixed ComParta client id and the
  title "ComParta". The source given is `about:internet`, never the other person's address, so Windows
  writes the internet mark (`Zone.Identifier`, `ZoneId=3`). Then it calls `Save`, which lets the
  installed antivirus look at the file. A refusal from Save, or a file gone afterwards, means blocked.
  If the check cannot run at all, the file passes without a mark, as before this story. That case is
  covered by the Shell test on the Windows runner. The adapter starts no process (story 5's guard).
  Why `about:internet` rather than the plain word "ComParta": a source that is not an address may be
  read as a place on this computer, and then no mark is written.
- **Composition:** `PlatformParts` gains the guard: `AttachmentGuard` on Windows,
  `NoReceivedFileCheck.Instance` on Android. `AppServices` passes it to `TransferUseCases`.
- **Screen:** the blocked chip and sentence already exist in the UX design ("Blocked by your
  antivirus", "Your antivirus stopped this file. ComParta will not open it.") in English and pt-BR.
  The card and the details offer no Open, Show in folder or Open with, only Remove. In the details,
  the "Where" line says nothing about Received files for a blocked one. The fake bridge refuses
  pause and continue on a blocked transfer, as the router does.

## Acceptance criteria

1. A received file that passes has the `Zone.Identifier` stream with `ZoneId=3` before the screen
   first shows it as received.
2. A file the guard blocks shows as blocked, with the UX sentence. Open, show and the viewer are not
   offered and are refused by the router (`received.notFound`). No "arrived" notification is sent,
   and a public download is no longer shared.
3. The guard runs once per finished receive, for every file in a folder, and never for the person's
   own shares or on the UI thread. A restart does not run it again, except for a check the app never
   finished.
4. Nothing opens by itself and no process starts (`GuardTests` still pass). Android builds and
   behaves as before.

## Verify

- Automated:
  - Core tests with a fake guard:
    - pass leads to `done`; block leads to `blocked` with no notification;
    - a folder with one blocked file is blocked;
    - repeated done reports call the guard once;
    - after a restart, `Passed` and `Blocked` do not call it, and `Pending` calls it once;
    - removing a transfer during a check leaves it removed.
  - Storage tests: the upgrade from version 8, and the value saved and read back.
  - Shell test on Windows: a temp file gets `ZoneId=3`, and a missing file is blocked.
  - Wiring tests (Windows CI) through `BridgeRouter`, with two real engines:
    - receive a file; the guard sees it once before the screen's first `done`; on Windows the file
      carries the mark;
    - receive with a blocking guard; the screen shows `blocked` and never `done`, `received.open` is
      refused, and no notification is sent.
  - Vitest: the blocked card and the details in en and pt-BR, with Open not offered.
- Needs a Windows PC:
  - Right-click a received file, Properties: it shows "This file came from another computer" with
    Unblock.
  - The EICAR test file is reported blocked by Microsoft Defender, and the card says so.
  - A large video still finishes quickly.
  - A public download kept shared is still checked; check whether the antivirus can remove a file
    that is still being shared.
