---
type: epic
title: "A person plays songs, watches videos and sees pictures inside ComParta"
parent: initiative-comparta
covers: [CAP-33]
after: []
assignee: ""
risk: medium
---

# A person plays songs, watches videos and sees pictures inside ComParta

## Description

Today "Open" hands a file to whatever Windows chooses (epic 8 story 5). This epic lets a person open a file inside ComParta: a picture shows full size, a song plays, a video plays, with the plain controls everyone knows. It works for files the person shares and files they received, on Windows and on Android.

When ComParta cannot play or show a file, it says so and offers the folder instead: "It is not possible to play this file. Open the folder it is in instead?" (pt-BR: "Não é possível reproduzir este arquivo. Abrir a pasta onde ele está?"). Nothing is opened by itself, and programs are never run (epic 3).

Asked by the maintainer on 2026-10-20 (roadmap decision P-20).

## Outcome

Maria gets the video of the Christmas dinner, clicks it, and it plays in ComParta. She clicks a document she cannot open, reads that it cannot be played here, and with one click sees it in its folder.

## Requirements

- CAP-33: open, play and view inside the app, with the folder as the fallback.
- AD-26 (the player is the web view's own; files reach it through a core-checked stream, never a path the UI chose), AD-7 (no path from the UI), AD-13 (received files keep Windows' marks), AD-5.
- UX design: the viewer over the home, its controls, and the fallback message.

## Done when

1. Pictures (JPG, PNG, GIF, WebP), songs (MP3, AAC/M4A, WAV, FLAC, OGG) and videos (MP4 with H.264 or AAC, WebM) open inside ComParta from a card, the details window and a double-click.
2. A picture can be zoomed and moved between with the arrow keys; a song or video can be paused, sought and set to full screen, with keyboard control.
3. A file ComParta cannot play shows the fallback message; "Open the folder" shows it in Explorer (or the phone's files app) with the file selected, and "Cancel" leaves things as they were.
4. A program or script never opens this way (epic 3's rule stands).
5. On Android the same viewer comes with the phone shell (epic 7 story 12).
6. A wiring test in Windows CI opens a picture, a song, a video and an unplayable file through the bridge, router, use cases and real storage, and checks the stream and the fallback event (CLAUDE.md testing rule); playback on a real PC is listed in the PR.

## Boundaries

Playing, showing and the fallback. Not editing pictures, playlists, subtitles download or a media library. Playing a video while it is still arriving is CAP-30, which builds on this player once confirmed.

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- spec — CAP-33, CAP-14, CAP-30, CAP-9
- architecture — AD-7, AD-13, AD-26
- roadmap — R1, P-20; reference: Stremio

## Notes

- Default taken 2026-10-20 (coordinator, where the ask was open): on Windows and Android the in-app player covers the common formats listed above; anything else falls back to the message.
- The formats are those the system web view plays without extra codecs; HEVC, MKV and AVI fall back for now.
