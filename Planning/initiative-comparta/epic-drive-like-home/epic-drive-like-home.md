---
type: epic
title: "ComParta looks and works like a simple file drive"
parent: initiative-comparta
covers: [CAP-13, CAP-14, CAP-15, CAP-23]
after: []
assignee: ""
risk: medium
---

# ComParta looks and works like a simple file drive

## Description

The first build is a plain page with two lists. This epic turns it into the experience the product promises: a window that looks like Google Drive or Windows Explorer, with a rail of sections, big "Share files" and "Receive with an invite" buttons, files as pictures in a grid or as a list, a details panel, search and sort, drag and drop, a guided first send, and full use of what was received. It also lays the design system every later screen uses.

## Outcome

Maria, who has never used a file-sharing program, recognises the screen as "like Drive", receives a file and opens it, and Paulo sends a video by dragging it onto the window.

## Requirements

- CAP-13: browse like a file drive (grid and list, thumbnails, search, sort, details).
- CAP-14: use what I received (open, show in folder, remove from list, delete).
- CAP-15: add files by dropping them (Windows); the share menu on Android is epic 7.
- CAP-23: usable by older people; the automated accessibility check starts here.
- UX design: every section binds; AD-4, AD-7 (amended), AD-18.

## Done when

1. The window matches the UX design's desktop and narrow layouts, in light and dark themes.
2. Shared and received items show as a grid with thumbnails or as a list; search, sort and the chosen view work and the view survives a restart.
3. A received file can be opened, shown in its folder, removed from the list and deleted after confirming.
4. Dropping files from Explorer onto the window shares them.
5. The first three sends use the guided steps.
6. `vitest-axe` passes on every screen, and a keyboard-only and Narrator pass finds no blocker.
7. Three people matching the UX design's personas complete the usability tasks; failures have tickets.

## Boundaries

The Windows screens and the shared UI components. Not new transfer states (epic 4), not settings and help pages (epic 12), not the phone layout's Android-only parts (epic 7), not folders as shares (epic 11; the grid already shows a folder card shape so epic 11 only fills it).

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- UX design — Planning/initiative-comparta/ux-comparta/ux-comparta.md, all sections
- spec — CAP-13, CAP-14, CAP-15, CAP-23
- architecture — AD-4, AD-7 (amended 2026-10-10), AD-12, AD-18

## Notes

- Sequencing: stories 1 and 2 first; they set the components and frame every other UI story in R1 uses. Then 3 to 11 in any order; 12 last.
- Epic 1 story 13 (refactor sweep) should run before story 2 replaces `App.tsx`.
- Unknown: whether a native drop target can sit over MAUI's `HybridWebView` on Windows, or whether WebView2 must forward the drop; story 6 starts with a spike and falls back to WebView2's `AllowExternalDrop` with the shell resolving dropped files natively.
- Assumption: Lucide icons and `vitest-axe` are acceptable new UI dependencies (MIT and MPL-2.0 respectively; both fit D-1 options).
- Waits on epic-share-a-file because: it provides the screen, bridge and commands this epic redesigns.
- Built 2026-10-12: story 1. Tokens live in `src/ui/src/tokens.css`, with dark values for both the system setting and an explicit `data-theme` (epic 12's choice). `src/tokens.test.ts` checks 4.5:1 for every text pair in both themes; it found white on the dark danger colour (about 2.3:1), fixed with a new `--color-on-danger`. Base components are in `src/ui/src/components/ui`; `?gallery` shows them in development only. Accessibility checks use `axe-core` directly instead of `vitest-axe`, which is unmaintained; the same rules run, colour contrast excepted, which the tokens test covers. The focus ring now applies to every focusable element.
- Built 2026-10-12: story 2. The header has Help and Settings; the rail adds them under a line. Settings holds the language for now (the header language menu is gone); Help explains the three steps and leads to the connection help. Between 600 and 900 px the rail shows icons with small labels; below 600 px a bottom bar holds the two places and Settings, and a floating ＋ holds Share and Receive. Both menus share `useMenu`. No drop target yet (story 6).
- Built 2026-10-12: the rest of story 3 (the view is now stored through `settings.set`, field `view`, instead of the web view's storage) and the sort half of story 7 (search was already built): the ⇅ menu orders by newest first, name (numbers in order, accents ignored) or largest first, stored as `sort`. `settings.set` takes every choice at once, so the UI keeps the latest copy and sends it whole.
