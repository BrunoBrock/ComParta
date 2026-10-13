---
type: ux-design
title: "ComParta UX design: a file drive for people who are not technical"
parent: initiative-comparta
created: '2026-10-10'
updated: '2026-10-10'
binds: [epic-drive-like-home, epic-send-to-a-person, epic-follow-a-transfer, epic-help-and-settings, epic-android, epic-share-a-folder, epic-brazilian-portuguese]
sources:
  - '../spec-comparta/spec-comparta.md'
  - '../architecture-comparta/architecture-comparta.md'
---

# ComParta UX design

The spec says what a person can do; this document says what they see and read while doing it.
UI stories cite its sections by heading. AD-4, AD-5, AD-7 and AD-11 still bind: the UI shows
state the core owns, never uses torrent words, never handles a path, and takes every word from
the translation catalog.

## People we design for

| Who | Example | What they know | What scares them |
| --- | --- | --- | --- |
| **Maria, 68, the receiver** | The maintainer's aunt. Uses WhatsApp on her phone and a laptop for email and YouTube. | Tapping links, attaching a photo to a message. | "Installing things", warnings, English words, anything that might "break the computer". |
| **Paulo, 45, the sender** | Filmed a birthday, has a 4 GB video. Uses Google Drive at work. | Files and folders, drag and drop, "share with". | Wasting an evening when it does not work and not knowing why. |
| **Ana, 30, the family helper** | The one who gets called when something does not work. | Enough to install apps and read a report. | Debugging over the phone without seeing the screen. |

Design for Maria first. If Maria can receive a file from a WhatsApp message without calling Ana,
the design works.

## Principles

1. **One obvious next step.** Every screen has one primary button. Empty screens say what to do.
2. **Say what is happening and what to do.** Every state has a sentence; every problem names an
   action the person can take ("Ask Paulo to open ComParta").
3. **Nothing happens by surprise.** Receiving asks first. Deleting asks first. Quitting during a
   transfer asks first. Nothing received ever opens by itself.
4. **Files stay where they are.** Repeat it where it matters (share, stop sharing): it is the
   fear we most need to calm.
5. **Familiar over clever.** Borrow Google Drive's and Windows Explorer's layout, icons and words.
   No new metaphors.
6. **Large, calm, readable.** 16 px minimum text, 44 px targets, generous spacing, no
   information density for its own sake.

## Words

The vocabulary rule (AD-5) bans torrent words. This table fixes the words we use instead, so every
screen, message and translation says the same thing.

| Concept | English | Português (Brasil) | Never |
| --- | --- | --- | --- |
| Something I offer | shared file / shared folder | arquivo compartilhado / pasta compartilhada | torrent, seed |
| Offering it | sharing | compartilhando | seeding |
| The text sent to someone | invite | convite | magnet, link técnico |
| Getting a file | receive / get the file | receber / baixar o arquivo | download (in headings), leech |
| Bytes moving | sending / receiving | enviando / recebendo | swarm, peers |
| The other person's computer | the other person's computer / phone | o computador / celular da outra pessoa | peer, node |
| Where files arrive | Received files | Arquivos recebidos | downloads folder (in headings) |
| Can others reach me | "Other people can reach this computer" | "Outras pessoas conseguem chegar a este computador" | port, NAT, firewall (except inside the report) |

Voice: second person, short sentences, verbs first on buttons ("Share files", "Get the file",
"Copy invite"). No exclamation marks, no blame ("could not", never "you failed"), no jargon in
errors, no error numbers on screen (the codes go to the report).

## Information architecture

```text
ComParta
├── Shared by me        (Compartilhados por mim)   files and folders I offer
├── Received            (Recebidos)                files and folders others sent me
│     └── On the way    (A caminho)                transfers still running, pinned on top
├── My people           (Minhas pessoas)           R3 only
├── Shared with me      (Compartilhados comigo)    R3 only
├── Help                (Ajuda)
└── Settings            (Configurações)
```

Two primary actions are always visible, like Drive's "New" button:

- **Share files** (Compartilhar arquivos): opens the picker; also the drop target.
- **Receive with an invite** (Receber com convite): opens the receive dialog.

## Layout

### Desktop (Windows, window ≥ 900 px wide)

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│  ◉ ComParta                      [ 🔍 Search my files…            ]   ⚙  ?   │
├───────────────────────┬──────────────────────────────────────────────────────┤
│ ┌───────────────────┐ │  Shared by me                     ▦ Grid  ☰ List  ⇅  │
│ │ ＋ Share files     │ │                                                      │
│ └───────────────────┘ │  ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐         │
│ ┌───────────────────┐ │  │ [thumb]│ │ [thumb]│ │  🎞    │ │  📄    │         │
│ │ ✉ Receive with an │ │  │        │ │        │ │        │ │        │         │
│ │   invite          │ │  ├────────┤ ├────────┤ ├────────┤ ├────────┤         │
│ └───────────────────┘ │  │Natal.mp4│ │Fotos/  │ │Aniv.mov│ │Receita │         │
│                       │  │4.2 GB   │ │200 fot.│ │1.1 GB  │ │120 KB  │         │
│  ⬆ Shared by me   (4) │  │● Sending│ │✓ Ready │ │◌ Getting│ │⚠ Not   │         │
│  ⬇ Received       (2) │  │  to 1   │ │        │ │  ready │ │  found │         │
│  ─────────────────    │  └────────┘ └────────┘ └────────┘ └────────┘         │
│  ? Help               │                                                      │
│  ⚙ Settings           │                                                      │
│                       │                                                      │
│  ● Others can reach   │                                                      │
│    this computer      │          (drop files anywhere here to share them)    │
└───────────────────────┴──────────────────────────────────────────────────────┘
```

- **Left rail** (240 px): the two primary actions as large buttons, the two sections with counts,
  Help and Settings, and the connection indicator at the bottom (green dot, amber dot with
  "Limited", red with "Others cannot reach this computer"), which opens the connection help.
- **Main area**: section title, view toggle (grid or list, remembered), sort menu, the items.
- **Details panel** (320 px, slides in from the right when an item is selected; a full-screen
  sheet below 900 px).
- **Drop overlay**: dragging files over the window dims it and shows "Drop to share. The files
  stay where they are."

### Speed bar (CAP-27)

A thin bar runs along the top of the window, under the header, on every screen:

```text
│  ↓ Receiving 2.4 MB/s   ↑ Sending 650 KB/s                 [ ■ Stop ▾ ]   │
```

- The two totals always show, 0 included, so the bar never jumps. Narrow windows keep the arrows
  and numbers and drop the words.
- **Stop** stops both. Its menu (▾) offers "Stop sending and receiving" (default, first),
  "Stop sending only" and "Stop receiving only".
- While something is stopped, the stopped side reads "Receiving stopped" or "Sending stopped",
  the bar takes the amber tone, and the button becomes **▶ Continue**, which starts everything
  stopped again.
- A stopped item shows the chip "Stopped" and the sentence "Stopped with everything else. Choose
  Continue at the top to go on." Its own Pause and Continue buttons are hidden while stopped.

### Details window (CAP-26)

Every card and row has an **ⓘ** button ("Details of {name}"). It opens a window, a full-screen
sheet on phones, that updates every second while open:

| Row | Shared file | Received file |
| --- | --- | --- |
| Size | 4.2 GB | 4.2 GB, 1.9 GB received (45 %) |
| State | Ready to send | Receiving |
| Where | the file's folder on this computer | Received files (the final name when done) |
| Added | 10 Oct 2026, 14:05 | 10 Oct 2026, 14:07 |
| Came from | (not shown) | An invite / A download link / A download file |
| Sending now | 650 KB/s | 0 B/s |
| Receiving now | (not shown) | 2.4 MB/s, about 12 minutes left |
| Connected computers | 1 | 3 |
| Sent since ComParta started | 1.1 GB | 12 MB |

The details panel of epic 8 (story 8.8) later shows the same rows beside the list instead of in a
window.

### Phone (Android, and desktop windows < 600 px)

```text
┌──────────────────────────────┐
│ ComParta              🔍  ⋮  │
├──────────────────────────────┤
│ Shared by me          ▦ ☰ ⇅ │
│ ┌──────────┐ ┌──────────┐    │
│ │ [thumb]  │ │ [thumb]  │    │
│ │Natal.mp4 │ │Fotos/    │    │
│ │● Sending │ │✓ Ready   │    │
│ └──────────┘ └──────────┘    │
│                              │
│                     ┌──────┐ │
│                     │  ＋  │ │  floating button: Share / Receive
│                     └──────┘ │
├──────────────────────────────┤
│  ⬆ Shared   ⬇ Received   ⚙   │  bottom navigation
└──────────────────────────────┘
```

Between 600 and 900 px the rail collapses to icons with labels underneath.

## Items

Every shared or received item is a **card** in grid view and a **row** in list view, built from
the same data.

| Part | Content |
| --- | --- |
| Picture | Thumbnail for photos and videos (AD-18); otherwise a large file-type icon: video, photo, music, document, spreadsheet, presentation, archive, program, folder, other. A folder shows up to four thumbnails of its photos. |
| Name | One line, ellipsis in the middle so the extension stays visible ("Aniversário da v…2024.mp4"). The full name is in the tooltip and details. |
| Size | Formatted for the language (AD conventions: the UI formats bytes). Folders add "· 200 files". |
| Status chip | Icon + short word, colour is never the only signal. See "States". |

Card size: 176 × 200 px on desktop, two columns on a phone. List row height: 56 px.

Selecting an item (click, Enter, tap) opens the details panel. Double-click or a second Enter
does the item's main action: **Copy invite** for a shared item, **Open** for a received one.

### Details panel

```text
┌──────────────────────────────┐
│ ✕                            │
│        [ large thumbnail ]   │
│ Natal 2024.mp4               │
│ Video · 4.2 GB               │
│ Shared since 10 Oct 2026     │
│                              │
│ ● Sending to 1 person: 45 %  │
│   About 12 minutes left      │
│                              │
│ ┌──────────────────────────┐ │
│ │  ✉  Copy invite          │ │  primary
│ └──────────────────────────┘ │
│    ▣  Show invite as QR code │  R2
│    📁 Show in folder          │
│    ⊘  Stop sharing            │  destructive, last
│                              │
│ The file stays on your       │
│ computer, exactly where it   │
│ is.                          │
└──────────────────────────────┘
```

A received item's panel: **Open** (primary), **Show in folder**, **Remove from list**,
**Delete file** (destructive, confirms). A received program or script shows the CAP-9 warning
above the buttons and its Open asks again.

## States

Shared items:

| Core state | Chip | Sentence in details |
| --- | --- | --- |
| preparing | ◌ Getting ready | "ComParta is reading the file so it can be sent. Large videos take a few minutes." |
| ready, nobody receiving | ✓ Ready to send | "Copy the invite and send it to someone." |
| ready, someone receiving | ● Sending to {n} | "Sending to {n} person: {percent} %. Keep ComParta open." |
| ready, someone finished | ✓ Sent {n} times | "{n} people have the whole file." |
| unavailable | ⚠ Not found | "The file was moved, renamed, changed or deleted. Put it back, or stop sharing it." |

Received items and transfers:

| Core state | Chip | Sentence |
| --- | --- | --- |
| connecting (< 2 min) | ◌ Looking for {sender} | "Looking for the other person's computer." |
| waiting (≥ 2 min) | ◌ Waiting for {sender} | "The other computer is not answering. Ask {sender} to open ComParta. It will continue by itself." |
| receiving | ● {percent} % | "Receiving: {received} of {total}. About {time} left." |
| noSpace | ⚠ No space | "This computer is out of space. Free up {needed} and it will continue." |
| done | ✓ Received | "It is in your Received files." |
| blocked | ⛔ Blocked by your antivirus | "Your antivirus stopped this file. ComParta will not open it." (AD-13) |
| failed | ⚠ Could not receive | "The file changed on the other computer or the invite is no longer valid. Ask for a new invite." |

The person's name ({sender}) comes from the invite when the sender gave one, else "the other
person". It is never presented as verified.

## Key flows

### Send a file, first time (guided)

The **Share files** button runs a three-step dialog the first three times, then the person may
tick "Don't show the steps again".

```text
Step 1 of 3  Choose        [Windows file dialog opens]
Step 2 of 3  Getting ready  ▓▓▓▓▓▓░░░░  "Reading Natal 2024.mp4… The file stays where it is."
Step 3 of 3  Send the invite
             ┌──────────────────────────────────────────────────────────┐
             │ Paulo wants to send you "Natal 2024.mp4" (4.2 GB) with   │
             │ ComParta.                                                │
             │ 1. Get ComParta: <store link>                            │
             │ 2. Open this invite in ComParta: comparta://receive/…    │
             └──────────────────────────────────────────────────────────┘
             [ Copy message ]   (primary)       [ Show as QR code ] (R2)
             "Paste it into WhatsApp or an email to the person. Keep ComParta open
              until they have the file."
```

After **Copy message** the button turns into "✓ Copied" for three seconds and a toast repeats
what to do next.

### Receive a file

Entry points, all ending in the same confirmation:

1. **Click the link** in an email or chat (where the program makes `comparta://` clickable). The
   app opens or comes to the front.
2. **Receive with an invite** → a dialog with one large text box. If the clipboard holds an invite
   when the dialog opens, it says "We found an invite you copied" and fills it in.
3. **Paste anywhere** in the window (Ctrl+V) with an invite on the clipboard.
4. **Phone**: the share menu or a tapped link (R2), or scanning a QR code (R2).

```text
┌──────────────────────────────────────────────┐
│  Get this file?                              │
│                                              │
│  🎞  Natal 2024.mp4                          │
│      4.2 GB · from Paulo                     │
│                                              │
│  It will be saved in your Received files.    │
│  You have 120 GB free.                       │
│                                              │
│         [ Not now ]   [ Get the file ]       │
└──────────────────────────────────────────────┘
```

Name and size in this dialog come from the invite (AD-16) and are labelled as such; the final name
on disk is the sanitised one. When there is not enough space the primary button is disabled and
the dialog says how much to free.

### First run

Three cards with one illustration each, "Next" as the primary button, "Skip" as a link.

1. "Send files straight to people you know. No account, no cloud."
2. "Your files stay where they are. ComParta never moves, copies or changes them."
3. "Keep ComParta open while someone is getting your file. It waits in the corner of the screen
   when you close the window."

Then one form: "What name should people see in your invites?" (optional) and "Start ComParta when
this computer starts" (on). Then, before Windows' own network question appears: "Windows will ask
if ComParta can use the network. Choose **Allow** so the people you send to can reach you."

### Stuck transfer

The state sentence carries the fix. After 10 minutes in "waiting", the details panel adds a
**What can I do?** link to the connection help page, which shows this computer's connection state
and three checks in plain words (both open ComParta; try again later; if both computers say
"Limited", try from another network such as a phone hotspot).

## Notifications and toasts

- In-app **toasts** (bottom centre, 6 s, closable, announced to screen readers) confirm an action:
  "Invite copied", "Stopped sharing. The file is still on your computer."
- **System notifications** (CAP-20) only for things that happen while the person is away: a file
  arrived; someone finished getting your file; a transfer has been waiting for over an hour.
- Errors that need a decision use a **dialog**, never a toast.

## Visual design

### Tokens

CSS custom properties on `:root`, redefined for dark mode with `prefers-color-scheme` and an
explicit Settings choice (System, Light, Dark). The values below are the light theme; dark values
are fixed in epic 8 story 1 with the same contrast targets.

| Token | Light | Use |
| --- | --- | --- |
| `--color-bg` | `#F8FAFC` | Window background |
| `--color-surface` | `#FFFFFF` | Cards, panels, dialogs |
| `--color-text` | `#0F172A` | Body text (contrast 17:1 on surface) |
| `--color-text-muted` | `#475569` | Secondary text (7.5:1) |
| `--color-primary` | `#0B6E4F` | Primary buttons, links, selection (6.4:1 with white text) |
| `--color-primary-soft` | `#E3F2EC` | Selected card, hover |
| `--color-ok` | `#15803D` | Ready, received |
| `--color-busy` | `#1D4ED8` | Sending, receiving |
| `--color-warn` | `#B45309` | Not found, waiting, no space |
| `--color-danger` | `#B91C1C` | Destructive buttons |
| `--color-border` | `#CBD5E1` | Card and input borders |
| `--radius` | `12px` | Cards, buttons, dialogs |
| `--space` | `8px` | Spacing unit; use multiples |
| `--font` | system UI stack (Segoe UI Variable on Windows, Roboto on Android) | No web fonts to download |
| `--text-base` | `16px` / line height 1.5 | Body |
| `--text-sm` | `14px` | Only for secondary details, never for actions |
| `--text-lg` / `--text-xl` | `20px` / `28px` | Section titles / dialog titles |
| `--target` | `44px` | Minimum height of anything clickable |

The green primary reads as "go/safe" and keeps clear of Google's blue so the app does not look
like an imitation of Drive.

### Components

One component per file (AD conventions), in `src/ui/src/components/ui/`:

`Button` (primary, secondary, quiet, danger; icon + label, never icon-only without a label except
in toolbars with a tooltip and accessible name), `IconButton`, `Card`, `Row`, `StatusChip`,
`ProgressBar` (with text percentage), `Dialog` (focus trap, Escape closes unless destructive is in
progress), `Toast`, `EmptyState` (illustration, sentence, primary action), `Sheet` (phone details),
`Rail`, `BottomNav`, `SearchBox`, `ViewToggle`, `SortMenu`, `FileIcon`, `Thumbnail` (falls back to
`FileIcon`), `Steps` (guided send).

Icons: Lucide, bundled at build time (no network fetch, AD-3), 24 px, 2 px stroke.

### Motion

Only to show where something went: a new item fades and slides in (150 ms); the details panel
slides (200 ms); progress bars animate their width. Everything respects
`prefers-reduced-motion`.

## Accessibility (CAP-23)

- WCAG 2.2 AA contrast for text and for status chip icons against their background.
- Keyboard: Tab order follows the visual order; arrow keys move between cards; Enter opens
  details; Delete asks to stop sharing or remove; Ctrl+V receives; Ctrl+O shares; Escape closes.
- Visible focus ring (3 px, `--color-primary`, offset 2 px) on every focusable element.
- Every control has an accessible name from the catalog; cards announce name, size and state.
- Progress is announced politely at most every 10 %; completion is announced assertively.
- Layout survives 200 % text zoom without horizontal scrolling at 1280 px wide.
- Automated: `vitest-axe` on every screen in the UI tests; manual: one Windows Narrator pass and
  one TalkBack pass per release.

## Usability testing

Each release before R1's pilot runs one round of three sessions with people who match Maria and
Paulo (epic 8 story 12). Tasks: receive a file from a WhatsApp message; send a video to someone;
find a received photo; stop sharing a file. A task fails when the person asks for help or gives
up; any failed task is a story in the next sprint.
