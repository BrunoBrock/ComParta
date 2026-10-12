---
type: roadmap
title: "ComParta roadmap: from the first build to Windows and Android"
parent: initiative-comparta
created: '2026-10-10'
updated: '2026-10-12'
based_on: 'branch mvp-share-and-send (share, invite and receive built; CI green on Windows)'
---

# ComParta roadmap

This is the plan for the rest of the product, written from the state of the code on the
`mvp-share-and-send` branch, not from `main` alone. It is the product owner's view: what ships in
which release, in which order, and which decisions are still open. The spec owns *what*, the
architecture owns *how*, the UX design owns *how it looks and reads*, and each epic's
`tickets.toml` owns its stories.

| Document | Owns |
| --- | --- |
| [spec](../spec-comparta/spec-comparta.md) | Capabilities CAP-1 to CAP-25, constraints, non-goals |
| [architecture](../architecture-comparta/architecture-comparta.md) | AD-1 to AD-21, Deferred |
| [UX design](../ux-comparta/ux-comparta.md) | Screens, flows, words, design tokens, accessibility |
| [initiative tickets](../tickets.toml) | The 14 epics in build order |
| `epic-*/tickets.toml` | The stories of each epic |

## Where we are (2026-10-10)

Built on `mvp-share-and-send`, CI green on a Windows runner:

- Share a file in place, see the list, stop sharing, missing-file marker (epic 1, stories 1 to 12).
- Copy an invite, paste it on another PC, receive into `Downloads\ComParta`, resume after
  restart, cancel and clear (first version of epic 2).
- Plain-language guards, generated bridge types, fake bridge, two-engine transfer test.

Not proven yet:

- A transfer between **two different homes**. Same-network transfers work. MonoTorrent has no
  hole punching, so two homes connect only when at least one side is reachable.
- The app on **a PC other than the developer's** (epic 1, Done when 6).
- Anyone non-technical **using it without help**.

The screen is a plain two-section list. It works, but it is not yet the "looks like Google Drive"
experience the product promises.

## Releases

Each release ends with real people, not with a build. A release is done when its success check
passes, not when its stories are built.

### R1 Family pilot (Windows)

**Goal:** Bruno's own family installs ComParta on their Windows PCs and sends a large video
between two homes without help, in Portuguese.

**Success check:** three non-technical relatives, in at least two homes, each install the app from
a link, send and receive one large file, and say which screen confused them. At least one transfer
crosses two homes on ordinary home internet.

| Order | Epic | Why it is in R1 |
| --- | --- | --- |
| 1 | 1 Share a file (finish) | Refactor sweep, then check the release build on a second PC. |
| 2 | 2 Send to a person (follow-ups) | First version built. Follow-ups: ComParta links, friendly invite message, confirm before receiving, one running copy. |
| 3 | 9 Connect between homes (field test first) | The biggest product risk. Measure before building more UI on top of it. |
| 3b | 15 Speed bar and details | Bruno's request of 2026-10-12: the total speed and a Stop button on top, and an information button on every file. Built next, before epic 8 redesigns the screen. |
| 4 | 6 Brazilian Portuguese | The pilot family speaks Portuguese; testing in English tests the wrong thing. Also brings the settings store. |
| 5 | 8 Drive-like home | The product promise. Every later screen uses its design system. |
| 6 | 4 Follow a transfer | The sender sees that someone is getting the file; stuck states explain themselves; notifications. |
| 7 | 10 Keep sharing in the background | Closing the window must not silently stop a transfer. |
| 8 | 3 Safe received content | Nothing from another person reaches a relative's PC without Windows' own protection. |
| 9 | 12 Help, settings and first run | Welcome, your name in invites, support report. |
| 6b | 14 Download from any link | Bruno's "Both" decision: paste a download link or open a download file, with pause, speed and time left; More options later. |
| 10 | 5 Install and update | One download, signed, updates without uninstalling. Stories 1 and 2 can start as soon as epic 1 is done. |

This is the order in [tickets.toml](../tickets.toml). With one builder, follow it. With more,
the field test (9.1), the catalog work (6.1, 6.2) and packaging (5.2) can run beside the UI work,
and `tickets.py next` lists what is unblocked at any moment. The longest chain runs through the
refactor sweep (1.13) and epic 8's frame, grid, received items, details panel, accessibility pass
and usability sessions; epic 8 is the critical path of R1.

### R2 Albums and phones

**Goal:** a person sends a whole photo album, and a relative who only has a phone uses ComParta.

**Success check:** a folder of 200 photos goes from a PC to an Android phone and from a phone to
a PC, both started from the phone's own share menu or a WhatsApp message.

| Order | Epic | Why |
| --- | --- | --- |
| 1 | 11 Share a folder or several files | Changes the share model; Android builds on the final model. |
| 2 | 7 Android | Same core and UI in a MAUI Android shell. |

### R3 People I share with

**Goal:** after the first invite, sending to the same person needs no copy and paste, and a share
is visible only to the people it was sent to.

| Order | Epic | Why |
| --- | --- | --- |
| 1 | 13 People and "Shared with me" | Closes the AD-8 trade-off (fingerprint on the public lookup network) and gives the Drive-like "Shared with me" view. Starts with a design spike. |

Later, not planned: iOS, macOS, Linux; a relay, unless decision D-3 below says otherwise.

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
| P-10 | Bruno chose "Both" (2026-10-10): ComParta also downloads from any public link or download file, like qBittorrent but simpler. CAP-25, AD-21, epic 14 in R1. | Spec CAP-25, AD-8 (amended), AD-21, epic 14 |
| P-11 | Bruno asked (2026-10-12) for an information button on every file opening its details, and a speed bar on top with a Stop button (stop sending, receiving, or both by default). CAP-26, CAP-27, AD-22, epic 15 in R1, next in build order. | Spec CAP-26, CAP-27, AD-22, epic 15 |
| P-9 | The UI uses its own small component set and CSS variables, with bundled Lucide icons; no UI framework. Thumbnails come from the operating system through a shell port. | AD-18, UX design |

## Decisions only Bruno can make

| Id | Question | Recommendation | Blocks |
| --- | --- | --- | --- |
| D-1 | Which open source license? | **Apache-2.0**: permissive like MIT, plus an explicit patent grant; compatible with MonoTorrent (MIT) and every package in use. GPL-3.0 if you want forks to stay open. | Epic 5 story 1, making the repository public |
| D-2 | How do Windows users get the app? | **Microsoft Store first** (registration is free for individual developers since September 2025; the Store signs the package and delivers updates, so no paid certificate and no update check), plus a zip on GitHub Releases for people who cannot use the Store. Paid code signing only if the Store is refused. | Epic 5 stories 3 to 6 |
| D-3 | If the field test shows homes often cannot connect, may a family member's always-on PC relay transfers for others? | **Decide after the field test (epic 9 story 1).** If needed: yes, opt-in, run by someone the family chooses; never a server run by the project. | Epic 9 story 9 |
| D-4 | Publish on Google Play (one-time US$25 developer fee)? | **Yes, at R2**, plus the signed APK on GitHub Releases. | Epic 7 story 11 |

D-1 and D-2 are asked now; D-3 waits for data; D-4 waits for R2.

## Readiness check

The BMAD readiness check asks whether every capability has an epic, every epic has stories that
can be built and checked on their own, and every story's blocker is named.

| Check | Result |
| --- | --- |
| Ticket tree is consistent | `python3 _bmad/method/scripts/tickets.py status Planning/initiative-comparta` reports 14 epics, 101 tickets, and no unpinned, undeclared or out-of-order dependency. |
| Every CAP is covered by an epic | Yes; see the `covers` field in [tickets.toml](../tickets.toml). CAP-24 (people) is covered by epic 13, whose stories after the spike carry `refine = true`. |
| Every AD is cited by at least one epic | Yes. |
| Every epic in R1 and R2 has stories with a `verify` | Yes. |
| Stories that need a person (`hitl`) are marked | Yes: second-PC check, field tests, store submissions, usability sessions, translation review. |
| Stories blocked on a decision | Marked with `unknown`; see D-1 to D-4. |
| Epic 1 story 13 (refactor sweep) | Not run yet; run it before epic 8 starts changing the UI. |

## Risks

| Risk | Likelihood | Effect | What the plan does |
| --- | --- | --- | --- |
| Two homes behind carrier-grade NAT (common on Brazilian mobile and some fibre ISPs) cannot connect without a relay | High | The product success signal fails | Epic 9: measure first, IPv6, port mapping, either-side-reachable, honest message; D-3 for a relay |
| WebView2 inside MAUI does not give native drag and drop | Medium | Drag and drop needs a workaround | Epic 8 story 6 is a spike first; the picker remains |
| MSIX file-system virtualisation hides the pilot's data folder from the Store build | Medium | Pilot users lose their list on upgrade | Epic 5 story 5 imports the unpackaged data on first packaged run |
| Android 15 limits background data transfer services to 6 hours a day | Medium | Very long transfers stop on phones | Epic 7 story 7 explains it and resumes; foreground while the screen is on |
| MonoTorrent's slow release cadence | Low | Bugs we cannot fix upstream | AD-2 keeps the engine swappable; epic 9 story 10 evaluates an alternative only if needed |
| Relatives do not keep the app open | High | Transfers stall | Epic 10 tray and start with Windows; epic 4 tells both sides plainly |
