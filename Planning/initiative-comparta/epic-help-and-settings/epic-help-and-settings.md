---
type: epic
title: "A person is welcomed, can change the few things that matter, and can get help"
parent: initiative-comparta
covers: [CAP-21, CAP-22]
after: []
assignee: ""
risk: low
---

# A person is welcomed, can change the few things that matter, and can get help

## Description

The first time ComParta opens it explains itself in three short steps, asks for a name to show in invites and whether to start with Windows, and prepares the person for Windows' network question. A Settings page holds the handful of choices. A Help page answers common questions offline, and "Copy a report" gives the family helper what they need without any telemetry.

## Outcome

Maria understands what ComParta is for before her first file arrives; when something goes wrong, Ana gets a useful report pasted into WhatsApp instead of a phone description of the screen.

## Requirements

- CAP-21: first run and settings. CAP-22: help without a technician.
- AD-9: nothing is sent automatically; the report is copied by the person.
- UX design: "First run", "Information architecture" (Help, Settings), "Words".

## Done when

1. A fresh install shows the welcome steps once; skipping them works; they never show again.
2. Settings changes language, your name, start with Windows, received folder, theme and the guided-send choice; every change survives a restart.
3. Help answers at least the ten questions in story 4 in both languages, offline.
4. "Copy a report" copies a plain-text report with no file names or contents, and nothing leaves the PC unless the person pastes it.

## Boundaries

Welcome, settings, help and report screens and the settings they need. Not the settings' effects owned elsewhere (language in epic 6, start with Windows in epic 10, connection health in epic 9).

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- spec — CAP-21, CAP-22
- architecture — AD-7, AD-9, AD-11, Consistency Conventions (Languages, settings)
- UX design — "First run", "Words"

## Notes

- Waits on epic-brazilian-portuguese because: it provides the settings store and the language choice.
- Waits on epic-drive-like-home because: it provides the frame and components.
- Waits on epic-connect-between-homes because: the report includes connection facts.
- Built 2026-10-20: story 5. `support.report` puts the report on the clipboard and returns it; the Help page shows all of it under the button. The router writes down every problem code a command answers with, and the last 20 are kept in the app's own data (setting `recentProblems`), so they survive a restart. The report is in English whatever the screen's language, because it is for whoever helps; it is the one text core writes itself. It names no file, path, invite or address: the router's outside address only says whether it is public or shared with other homes.
- Built 2026-10-22: story 2, the last of the epic. Settings shows the name of the folder received files go to, with "Choose another folder" (Windows' folder dialog, AD-7) and "Use the usual folder". Each transfer stores the folder it started in (`transfers.folder`, schema 7; null is the usual Downloads\ComParta), so a new choice only moves new transfers; running ones continue and finished ones open where they are. Unfinished parts sit in `.in-progress` under the transfer's own folder, so a folder on another drive gets the file by a rename, and room is measured on that drive. A folder ComParta cannot write to, or one inside its own data, is refused (`received.folderUnusable`); a chosen folder that is gone when a file is received, for example an unplugged drive, starts nothing (`received.folderMissing`). The screen is given the folder's name only, never its path.
