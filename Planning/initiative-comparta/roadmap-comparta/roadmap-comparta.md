---
type: roadmap
title: "ComParta roadmap: from the first build to Windows and Android"
parent: initiative-comparta
created: '2026-10-10'
updated: '2026-10-20'
based_on: 'main at 18ce1c1 (PR #30)'
---

# ComParta roadmap

This is the plan for the rest of the product, written from the state of the code on `main`
(updated 2026-10-20; the drawings are in [roadmap.html](roadmap.html)). It is the product owner's view: what ships in
which release, in which order, and which decisions are still open. The spec owns *what*, the
architecture owns *how*, the UX design owns *how it looks and reads*, and each epic's
`tickets.toml` owns its stories.

| Document | Owns |
| --- | --- |
| [spec](../spec-comparta/spec-comparta.md) | Capabilities CAP-1 to CAP-33, references, constraints, non-goals |
| [architecture](../architecture-comparta/architecture-comparta.md) | AD-1 to AD-26, Deferred |
| [UX design](../ux-comparta/ux-comparta.md) | Screens, flows, words, design tokens, accessibility |
| [initiative tickets](../tickets.toml) | The 18 epics in build order |
| [roadmap drawings](roadmap.html) | The whole plan in build order, every epic story by story, and how a file reaches any app, drawn |
| `epic-*/tickets.toml` | The stories of each epic |

## Where we are (2026-10-20)

On `main`, 55 of 129 stories done.

Since 2026-10-13: the helper program (PR #25, epic 16 story 2) and ComParta handing shared files
to a helper computer set in Settings (PR #30, story 3) are merged; the buttons for other torrent
apps (story 4) are being built. ComParta is open source under the Zero-Clause BSD license (PR #29,
D-1). On 2026-10-20 the maintainer asked for friends, folders shared with friends like Google
Drive, files only from friends by default, and playing files inside the app (P-19, P-20).

Built before that:

- Share, send and receive (epics 1 and 2); the copied invite is now a standard link that other
  torrent apps open too (PR #23, AD-16 amended).
- The Drive-like home (epic 8 stories 1 to 10), the speed bar and details window (epic 15), follow
  a transfer (epic 4), keep sharing in the background (epic 10), Brazilian Portuguese (epic 6
  stories 1 to 4), download from any link (epic 14 stories 1 to 3).
- Reachability facts, the connection indicator, IPv6 and port mapping that lasts (epic 9).
- Fixed on 2026-10-13 after the maintainer's reports: Stop and Stop sharing did nothing, and a share
  stayed on "Getting ready" (both were waiting on a slow home router); per-file speeds and
  pause/play; public servers (CAP-29).
- Every feature is now tested through the real wiring in Windows CI (`tests/ComParta.Wiring.Tests`).

What the field test of 2026-10-13 showed: the maintainer could not get a file shared from their PC on their
phone over 4G, with ComParta or with qBittorrent. Both apps' ports timed out from outside, and a
phone on mobile data cannot receive connections either. Two sides that can only connect out never
meet. That is now the top of the plan: epic 16, a helper every torrent app can use.

Not proven yet: anyone non-technical using it without help, and the app on a PC other than the
developer's (epic 1 story 14).

## Releases

Each release ends with real people, not with a build. A release is done when its success check
passes, not when its stories are built.

### R1 Family pilot (Windows)

**Goal:** The maintainer's own family installs ComParta on their Windows PCs and sends a large video
between two homes without help, in Portuguese.

**Success check:** three non-technical relatives, in at least two homes, each install the app from
a link, send and receive one large file, and say which screen confused them. At least one transfer
crosses two homes on ordinary home internet.

| Order | Epic | Why it is in R1 |
| --- | --- | --- |
| **Now** | **16 Reachable from anywhere** | **The maintainer's first goal (2026-10-13): a shared file reaches any torrent app on Windows or Android, anywhere, with no router or port setup. A helper on an always-reachable computer (AD-23); spike with stock apps first.** |
| **Next** | **13 Friends** | **The maintainer's ask (2026-10-20): add a friend and see what they share; a new install accepts files only from friends, with a switch on the first Settings screen. Moved from R3 so the first release people install already has the default. Starts with a design spike.** |
| **Then** | **18 Open and play** | **The maintainer's ask (2026-10-20): songs, videos and pictures play inside ComParta; anything else offers to open its folder.** |
| 1 | 1 Share a file (finish) | Refactor sweep, then check the release build on a second PC. |
| 2 | 2 Send to a person (follow-ups) | First version built. Follow-ups: ComParta links, friendly invite message, confirm before receiving, one running copy. |
| 3 | 9 Connect between homes (field test first) | The biggest product risk. Measure before building more UI on top of it. |
| 3b | 15 Speed bar and details | The maintainer's request of 2026-10-12: the total speed and a Stop button on top, and an information button on every file. Built next, before epic 8 redesigns the screen. |
| 4 | 6 Brazilian Portuguese | The pilot family speaks Portuguese; testing in English tests the wrong thing. Also brings the settings store. |
| 5 | 8 Drive-like home | The product promise. Every later screen uses its design system. |
| 6 | 4 Follow a transfer | The sender sees that someone is getting the file; stuck states explain themselves; notifications. |
| 7 | 10 Keep sharing in the background | Closing the window must not silently stop a transfer. |
| 8 | 3 Safe received content | Nothing from another person reaches a relative's PC without Windows' own protection. |
| 9 | 12 Help, settings and first run | Welcome, your name in invites, support report. |
| 6b | 14 Download from any link | The maintainer's "Both" decision: paste a download link or open a download file, with pause, speed and time left; More options later. |
| 10 | 5 Install and update | One download, signed, updates without uninstalling. Stories 1 and 2 can start as soon as epic 1 is done. |

This is the order in [tickets.toml](../tickets.toml). With one builder, follow it. With more,
the field test (9.1), the catalog work (6.1, 6.2) and packaging (5.2) can run beside the UI work,
and `tickets.py next` lists what is unblocked at any moment. The longest chain runs through the
refactor sweep (1.13) and epic 8's frame, grid, received items, details panel, accessibility pass
and usability sessions; epic 8 is the critical path of R1.

### R2 Folders and phones

**Goal:** a person shares folders with friends like in Google Drive, sends a whole photo album, and a relative who only has a phone uses ComParta.

**Success check:** a folder of 200 photos goes from a PC to an Android phone and from a phone to
a PC, both started from the phone's own share menu or a WhatsApp message.

| Order | Epic | Why |
| --- | --- | --- |
| 1 | 11 Share a folder or several files | Changes the share model; Android builds on the final model. |
| 2 | 7 Android | Same core and UI in a MAUI Android shell, with the player from epic 18. |
| 3 | 17 Folders shared with friends | The maintainer's ask (2026-10-20): make folders and share them with friends like Google Drive; each file travels peer to peer, changes reach friends (AD-25). After Android so it works on phones from the start. |

R3 (people I share with) was folded into R1 (epic 13, friends) and R2 (epic 17, folders) on 2026-10-20.

Later, not planned: iOS, macOS, Linux; a browser download page on the helper; watch while it
arrives (CAP-30, proposed from the Stremio reference, waits for the maintainer's yes; it would build on epic 18's player).

## Decisions taken in this plan

These were open questions or gaps; the plan settles them so stories can be written. Each one is
recorded where it binds.

| Id | Decision | Recorded in |
| --- | --- | --- |
| P-1 | pt-BR ships in R1, before the family pilot, and the app starts in the Windows display language. | Epic 6, spec CAP-12 |
| P-2 | OQ-2 answered: no AppContainer sandbox. Received files get Windows' own "came from the internet" mark and antivirus check through the attachment services API; programs and scripts are flagged and never opened without a second confirmation. | AD-13, epic 3 |
| P-3 | OQ-5 answered: folders and several files as one share in R2, before Android. | AD-19, epic 11 |
| P-4 | The app keeps running in the notification area when the window is closed and something is shared or moving; it starts with Windows when the person agrees at first run. | AD-14, epic 10 |
| P-5 | Invites become a ComParta link (`comparta://`) inside a friendly message, with the engine text opaque inside it; old raw invites still work. | AD-16, epic 2 |
| P-6 | Files and folders enter only through native pickers, native drag and drop, and the operating system's share menu; the UI still never sends a path. | AD-7 (amended) |
| P-7 | Connection work starts with a measured field test; IPv6 and router port mapping come before any thought of a relay or another engine. | AD-15, epic 9 |
| P-8 | No in-app update check, so AD-9 stays strict. Updates come from the store the app was installed from. | AD-17, epic 5 |
| P-10 | The maintainer chose "Both" (2026-10-10): ComParta also downloads from any public link or download file, like qBittorrent but simpler. CAP-25, AD-21, epic 14 in R1. | Spec CAP-25, AD-8 (amended), AD-21, epic 14 |
| P-11 | The maintainer asked (2026-10-12) for an information button on every file opening its details, and a speed bar on top with a Stop button (stop sending, receiving, or both by default). CAP-26, CAP-27, AD-22, epic 15 in R1, next in build order. | Spec CAP-26, CAP-27, AD-22, epic 15 |
| P-12 | The maintainer asked (2026-10-13) to name public servers when sharing. Built in PR #21: a Settings list, every share announces to them, the link carries them. | Spec CAP-29, AD-8 (amended) |
| P-13 | The maintainer changed (2026-10-13, PR #23) the copied invite to the engine's own standard link, so other torrent apps open it. | AD-16 (amended), epic 2 |
| P-14 | Asking the router for a port runs in the background and gives up after 10 seconds; the mapping stays open while the app runs and is removed on quit (PRs #18 and #19, after the maintainer's reports that sharing hung and Stop did nothing). | AD-15, epic 9 Notes |
| P-15 | The maintainer asked (2026-10-13) for each file's own speed and a pause/play button on each card and in its details window. Built in PR #19. | Spec CAP-27 (amended), epic 15 Notes |
| P-16 | Working rules (2026-10-13): check every branch against main before new work; test every feature through the real wiring in Windows CI; new work as draft PRs; branches are deleted after merge. Changed 2026-10-20 (PR #29): Claude may merge its own PR once every check, the Windows wiring tests included, has passed. | `CLAUDE.md` |
| P-17 | The maintainer's goal (2026-10-13): a published file is consumable by any torrent app on Windows and Android, with no router or port setup on either side. Answers D-3: a helper every app can use, on an always-reachable computer. | Spec CAP-28, AD-23, AD-9 (amended), epic 16 |
| P-18 | References (2026-10-13): qBittorrent, Stremio and Google Drive. | Spec, References |
| P-19 | The maintainer asked (2026-10-20) for friends with access to their shared files, folders made and shared like Google Drive but sent peer to peer, and files accepted only from friends by default with a switch on the first Settings screen. Epic 13 becomes Friends and moves into R1; epic 17 Folders in R2. Default where the ask was open: "unknown sources" covers files and requests to connect from anyone who is not a friend, and an invite from someone new is a friend request. | Spec CAP-24, CAP-31, CAP-32; AD-24, AD-25; epics 13 and 17 |
| P-20 | The maintainer asked (2026-10-20) to open files inside the app: play songs, watch videos, see pictures; a file that cannot be played offers to open its folder. Default: the formats the system web view plays on Windows and Android; everything else falls back. Epic 18 in R1. | Spec CAP-33, AD-26, epic 18 |
| P-9 | The UI uses its own small component set and CSS variables, with bundled Lucide icons; no UI framework. Thumbnails come from the operating system through a shell port. | AD-18, UX design |

## Decisions only the maintainer can make

| Id | Question | Recommendation | Blocks |
| --- | --- | --- | --- |
| D-1 | ~~Which open source license?~~ | **Answered 2026-10-20:** Zero-Clause BSD (PR #29). | Epic 5 story 1 (third-party notices still to do) |
| D-2 | How do Windows users get the app? | **Microsoft Store first** (registration is free for individual developers since September 2025; the Store signs the package and delivers updates, so no paid certificate and no update check), plus a zip on GitHub Releases for people who cannot use the Store. Paid code signing only if the Store is refused. | Epic 5 stories 3 to 6 |
| D-3 | ~~If the field test shows homes often cannot connect, may a family member's always-on PC relay transfers for others?~~ | **Answered 2026-10-13 by P-17:** yes, through a helper every torrent app can use (AD-23). | Epic 16 |
| D-5 | Where does the first helper run? | **The free Oracle Cloud machine** (Always Free: Arm, up to 4 cores and 24 GB, about 10 TB a month out, São Paulo region; needs a card to sign up, and an idle machine can be reclaimed). For development, the maintainer's second PC with one forwarded port. AWS and Azure are free for 12 months only; Google Cloud's free machine has about 1 GB a month out, too little. Check the offers before signing up. | Epic 16 stories 5 and 6 |
| D-6 | Once people outside the family use ComParta, does the app ship with the project's helper turned on for everyone? | **Yes for the family pilot, decide again before a public release:** the cost, abuse (strangers storing files) and who can read what passes through it all grow with users. The alternative is that each family runs its own helper from the guide. | Epic 16 story 3 (the default), epic 5 (public release) |
| D-7 | Do download links and download files from websites count as unknown sources, so they are refused while the switch is off? | **Yes**: one rule the person can understand ("only from friends unless I turn this on"); pasting a website link while it is off says so and points to the switch. The alternative keeps website links always allowed, since the person chose them. | Epic 13 stories 7 and 8 |
| D-4 | Publish on Google Play (one-time US$25 developer fee)? | **Yes, at R2**, plus the signed APK on GitHub Releases. | Epic 7 story 11 |

D-2, D-5 and D-7 are asked now; D-6 waits for a public release; D-4 waits for R2.

## Readiness check

The BMAD readiness check asks whether every capability has an epic, every epic has stories that
can be built and checked on their own, and every story's blocker is named.

| Check | Result |
| --- | --- |
| Ticket tree is consistent | `python3 _bmad/method/scripts/tickets.py status Planning/initiative-comparta` reports 18 epics and 129 tickets (55 done, 2 built and waiting on a person, 2 in progress, 1 dropped), and no undeclared dependency. Known: epic 15 is listed before epic 6, which it waits on; both are built. |
| Every CAP is covered by an epic | Yes; see the `covers` field in [tickets.toml](../tickets.toml). CAP-24 and CAP-31 (friends) are covered by epic 13 and CAP-32 (folders) by epic 17; their stories after the spikes carry `refine = true`. CAP-33 is epic 18. |
| Every AD is cited by at least one epic | Yes. |
| Every epic in R1 and R2 has stories with a `verify` | Yes. |
| Stories that need a person (`hitl`) are marked | Yes: second-PC check, field tests, store submissions, usability sessions, translation review. |
| Stories blocked on a decision | Marked with `unknown`; see D-1 to D-4. |
| Epic 1 story 13 (refactor sweep) | Done 2026-10-12, before epic 8 changes the UI. |

## Risks

| Risk | Likelihood | Effect | What the plan does |
| --- | --- | --- | --- |
| Two homes behind carrier-grade NAT (common on Brazilian mobile and some fibre ISPs) cannot connect without a relay | **Confirmed 2026-10-13** | The product success signal fails | Epic 9 built IPv6, port mapping and either side connecting out; epic 16 adds the helper every app can use |
| The helper becomes a cost or an abuse target, or holds files people consider private | Medium | Bandwidth bills, takedown requests, lost trust | Key needed to add files, space limits, files dropped when a share stops, AD-23 trade-off stated plainly, D-6 before a public release |
| Stock Android apps do not find the helper from the link alone | Medium | CAP-28 fails on phones | Epic 16 story 1 tests LibreTorrent and Flud first; a saved download file is the fallback |
| WebView2 inside MAUI does not give native drag and drop | Medium | Drag and drop needs a workaround | Epic 8 story 6 is a spike first; the picker remains |
| MSIX file-system virtualisation hides the pilot's data folder from the Store build | Medium | Pilot users lose their list on upgrade | Epic 5 story 5 imports the unpackaged data on first packaged run |
| Android 15 limits background data transfer services to 6 hours a day | Medium | Very long transfers stop on phones | Epic 7 story 7 explains it and resumes; foreground while the screen is on |
| MonoTorrent's slow release cadence | Low | Bugs we cannot fix upstream | AD-2 keeps the engine swappable; epic 9 story 10 evaluates an alternative only if needed |
| Relatives do not keep the app open | High | Transfers stall | Epic 10 tray and start with Windows; epic 4 tells both sides plainly |
| The friends-only default blocks a relative's very first file | Medium | The first send fails and looks broken | An invite from someone new is a friend request first (epic 13 story 2); a refused stranger gets a plain message naming who tried and how to accept them |
| Keeping a friend's copy of a shared folder in step while devices come and go | Medium | Friends see stale folders | Versioned, signed lists (AD-25); a device that was off gets the newest version; the helper holds it when the owner cannot be reached |
| Common videos (HEVC, MKV) do not play in the system web view | High | "Watch videos" disappoints | They fall back to opening the folder (CAP-33); epic 18 story 6 measures which formats play on real PCs |

## References

ComParta measures itself against three products (P-18). The spec's References section says what
is taken from each; in short: **qBittorrent** for working with every other torrent app and
downloading any link, **Stremio** for media first and playing while a file arrives, **Google
Drive** for the look, the details panel and sharing by link.
