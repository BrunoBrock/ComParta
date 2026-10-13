---
id: SPEC-comparta
companions:
  - '../architecture-comparta/architecture-comparta.md'
  - '../ux-comparta/ux-comparta.md'
  - '../roadmap-comparta/roadmap-comparta.md'
sources:
  - '../../ReadMe.md'
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate. Source documents listed in frontmatter are for traceability — consult them only if you need narrative rationale or prose color this contract intentionally omits.

# ComParta

## Why

A vision to realize, answering a pain. Sharing large personal files (family videos, photo albums, documents) today means uploading them to a cloud provider, with storage limits, subscriptions, privacy trade-offs and dependence on a third party. Peer-to-peer transfer removes all of that, but existing torrent programs are built for power users and are confusing and unsafe for everyday people. ComParta lets parents, grandparents, relatives and friends, people who can use a smartphone and email but nothing more complex, send files directly to each other.

Bruno's first goal (2026-10-13): a file shared from a PC can be downloaded from anywhere, with ComParta or with any other torrent app, on Windows computers and Android phones, and nobody has to change a router or open a port.

## References

The products ComParta measures itself against (decided 2026-10-13). They are references for behaviour and feel only; no code is copied (Constraints).

| Product | What ComParta takes from it |
| --- | --- |
| qBittorrent | Works with every other torrent app through the standard protocol and links; downloads from any public link or download file (CAP-25); details of a file; pause and continue; speeds. Its power-user screens stay out of the main view. |
| Stremio | Media first: pictures on every card, and a video that plays while it is still arriving (CAP-30, proposed). It runs on Windows and Android with the same feel. |
| Google Drive | The look and the way of working: a grid or list of files, a details panel, search and sort, share by copying a link, and later "Shared with me" (CAP-13, CAP-14, CAP-24). |

## Capabilities

Capabilities marked **[MVP]** form the first build (Windows desktop: add or remove a shared file, and send it to another person's computer with an invite). CAP-6, CAP-7 and a first form of CAP-8 were pulled into the first build on 2026-10-08. Each later capability names its release: **[R1]** family pilot on Windows, **[R2]** albums and Android, **[R3]** people I share with (see the roadmap companion). CAP-13 to CAP-24 were added on 2026-10-10 when the rest of the product was planned; CAP-26 and CAP-27 on 2026-10-12; CAP-28 to CAP-30 on 2026-10-13.

- **CAP-1 [MVP] Add a share**
  - **intent:** A person picks a file on their PC and it joins the list of things they share.
  - **success:** After picking a file in the Windows file dialog it shows in the list with its name and size; the original file is byte-identical and in the same place.
- **CAP-2 [MVP] See my shares**
  - **intent:** A person sees everything they currently share.
  - **success:** The list shows each share's name and size, shows an empty-state message when there are none, and is the same after closing and reopening the app.
- **CAP-3 [MVP] Remove a share**
  - **intent:** A person stops sharing a file without losing it.
  - **success:** After confirming, the share leaves the list and stays gone after restart; the original file still exists unchanged.
- **CAP-4 [MVP] Plain-language experience**
  - **intent:** A person understands every screen and message without torrent knowledge.
  - **success:** No UI string contains seed, peer, leech, swarm, tracker, magnet, torrent, hash or DHT; every error code has a plain-language message telling the person what to do.
- **CAP-5 [MVP] Missing-file handling**
  - **intent:** A person learns when a shared file was moved, renamed or deleted outside the app.
  - **success:** Such a share is marked unavailable in the list and can still be removed.
- **CAP-6 [MVP] Invite a person**
  - **intent:** A person offers a share to someone they choose, through a channel they already know (email, chat).
  - **success:** The sender copies an invite they can paste into an email or chat; someone without the invite cannot ask for the share (see AD-8 for the limits of this).
- **CAP-7 [MVP] Receive a share**
  - **intent:** A person accepts an invite and gets the file.
  - **success:** Pasting an invite brings the content directly from the sender's device into the Received folder, with no account and no hosted service involved, and never overwrites a file already there.
- **CAP-8 [MVP, first form] Follow a transfer**
  - **intent:** A person sees whether a file is on its way, done or stuck.
  - **success:** Each transfer shows progress and a plain-language state, and says what to do when it cannot proceed (for example, the other person's device is off).
  - **R1 adds:** the sender also sees when someone is getting a shared file and how far along they are; a transfer shows roughly how long is left; running out of disk space and a file that changed on the sender's side each have their own plain state.
- **CAP-9 [R1] Safe handling of received content**
  - **intent:** Received files cannot harm the rest of the computer.
  - **success:** Received content is written only under the Received root, hostile file names cannot escape it or disguise their type, nothing received is run automatically, Windows treats every received file as coming from the internet (AD-13), and opening a program or script that was received asks a second time.
- **CAP-10 [R2] Use on a phone**
  - **intent:** A person does the same things on an Android phone.
  - **success:** Add, remove, invite and receive work on Android with the same words and flows as on desktop, starting from the phone's own share menu or from a link in a chat app.
- **CAP-11 [R1] Install and stay current**
  - **intent:** A non-technical person installs and updates the app without technical steps.
  - **success:** Installation on Windows is one download with no extra prerequisites and no unknown-publisher warning, updating needs no manual uninstall and keeps the person's shares, and the app makes no update check of its own (AD-17).
- **CAP-12 [R1] Brazilian Portuguese**
  - **intent:** A person uses the app in Brazilian Portuguese.
  - **success:** Every UI string, error message and invite message has a pt-BR translation; the app starts in the Windows display language when it is supported; the person can change it and the choice survives a restart; sizes and dates follow Brazilian formatting.
- **CAP-13 [R1] Browse like a file drive**
  - **intent:** A person finds what they share and what they received the way they find files in Google Drive.
  - **success:** Shared and received items show as a grid with pictures (photo and video thumbnails, file-type icons) or as a list; the person can switch view, search by name, sort by name, date or size, and see an item's details in a side panel. The choice of view survives a restart.
- **CAP-14 [R1] Use what I received**
  - **intent:** A person opens, finds and tidies the files they received without leaving ComParta.
  - **success:** From a received item the person can open it, show it in its folder, remove it from the list, or delete the file after confirming. Opening follows CAP-9.
- **CAP-15 [R1] Add files the natural way**
  - **intent:** A person shares a file the way they already move files around.
  - **success:** Dropping files from Windows Explorer onto the window shares them, as picking them does. On Android the phone's share menu offers ComParta (R2).
- **CAP-16 [R1] An invite that explains itself**
  - **intent:** The person who gets an invite understands what it is and what to do, even without ComParta installed.
  - **success:** Copying an invite produces a short message in the sender's language naming the sender (when they gave a name), the file and its size, how to get ComParta, and a ComParta link. Pasting the whole message, or clicking the link where the chat or email program allows it, opens the receive step, which shows the file's name and size and asks before anything is received. A phone in the same room can scan the invite as a QR code (R2).
- **CAP-17 [R1] Keep sharing when the window is closed**
  - **intent:** A person does not stop a transfer by closing the window.
  - **success:** While something is shared or moving, closing the window keeps ComParta running in the notification area and says so the first time; ComParta can start with Windows; quitting while someone is receiving a file asks first; the computer does not go to sleep in the middle of a transfer.
- **CAP-18 [R1] Connect between homes**
  - **intent:** Two people in different homes connect without knowing anything about networks.
  - **success:** The app opens the router port where the router allows it, uses IPv6 where the internet connection has it, connects when either of the two devices is reachable, and shows in plain words whether others can reach this computer and what to try when they cannot. The field test in epic 9 sets the target success rate.
  - **amended 2026-10-13:** when neither device can be reached, the file passes through the helper (CAP-28, AD-23), so no person ever has to change a router or open a port.
- **CAP-19 [R2] Share a folder or several files**
  - **intent:** A person sends a whole photo album at once.
  - **success:** A person picks a folder, or several files, and it becomes one share with one invite; the receiver gets the same folder structure under the Received root; adding files to the folder later does not change what was shared, and the screen says so.
- **CAP-20 [R1] Know when something arrives**
  - **intent:** A person learns that a file arrived, or that someone got their file, without watching the screen.
  - **success:** When a file finishes arriving, and when someone finishes getting a shared file, the operating system shows a notification; clicking it opens the item in ComParta.
- **CAP-21 [R1] First run and settings**
  - **intent:** A person understands the app the first time and can change the few things that matter.
  - **success:** The first run explains in three short steps what ComParta does, that files stay where they are, and that ComParta must stay open; it asks for the name to show in invites (optional) and whether to start with Windows; it explains the network permission before Windows asks. Settings holds language, received folder, start with Windows, your name, and about (version and licenses).
- **CAP-22 [R1] Get help without a technician**
  - **intent:** A person, or the relative who helps them, can find out what is wrong.
  - **success:** An in-app help page answers the common questions offline. "Copy a report" copies a plain-text summary (version, system, connection state, recent error codes, no file names or contents) that the person can paste into a message to whoever helps them. Nothing is sent automatically (AD-9).
- **CAP-23 [R1] Usable by older people**
  - **intent:** People with weaker eyesight, less precise hands or a screen reader use ComParta comfortably.
  - **success:** Base text is at least 16 px, touch and click targets are at least 44 px, contrast meets WCAG 2.2 AA, every action works by keyboard with a visible focus, every control has a screen-reader name, and the layout still works at 200 % text size. An automated accessibility check runs in the UI tests.
- **CAP-25 [R1] Download from any link**
  - **intent:** A person downloads things published on the internet the way they would with qBittorrent, without learning its vocabulary.
  - **success:** Pasting a download link from a website, or opening a download file a website gave them, adds it to Received next to files from people; it shows progress, speed and time left; the person can pause and continue it; the file lands in the Received folder under the same safety rules (CAP-9). Advanced controls (choosing which files of a download to get, speed limits, how long to keep sharing after it finishes) sit under "More options", never on the main screen. Added 2026-10-10 after Bruno chose "Both" (roadmap decision P-10).
- **CAP-26 [R1] Details of any file**
  - **intent:** A person, or the relative helping them, can see everything ComParta knows about one file without it crowding the main screen.
  - **success:** Every shared and received file shows an information button. It opens a details window with the name, size, where the file is on this computer, its state, when it was added, how fast it is sending and receiving now, how many other computers are connected for it, and how much has gone each way. The numbers update while the window is open. No technical file-sharing word appears (AD-5). Added 2026-10-12 at Bruno's request (roadmap decision P-11).
- **CAP-27 [R1] See and stop all the traffic at once**
  - **intent:** A person sees at a glance how fast ComParta is using their internet and can stop it in one click, for example before a video call.
  - **success:** A bar along the top of the window always shows the total speed of everything being received and everything being sent. Next to it, a Stop button stops both at once; its menu also offers stopping only sending or only receiving. While something is stopped, the bar says so and the button becomes Continue. The choice is remembered after a restart. Stopping never cancels a transfer or removes a share. Added 2026-10-12 at Bruno's request (roadmap decision P-11).
  - **amended 2026-10-13:** each file also shows its own receive and send speed, and has its own pause/play button on its card and in its details window; a paused shared file stays paused after a restart (PR #19, roadmap decision P-15).
- **CAP-28 [R1] Publish to any torrent app, anywhere**
  - **intent:** A person puts a file on the internet from their PC, and anyone with the link gets it, from another home or from a phone on mobile data, with ComParta or with the torrent app they already have.
  - **success:** With no router or port changed on either side, the copied link opened in qBittorrent on a Windows PC on another network, and in a stock torrent app on an Android phone on mobile data, delivers the file byte-identical, and the original is untouched. The person can also save a download file for apps that do not open links. Built on the helper (AD-23, epic 16). Added 2026-10-13 at Bruno's request (roadmap decision P-17).
- **CAP-29 [R1] Name public servers**
  - **intent:** A person who knows public torrent servers adds them, so other apps find what they share the way they find any public torrent.
  - **success:** Settings holds a list of server addresses (one per line, checked, at most 20); every shared file is announced to them, including files already shared, and the copied link carries them. They help others find the computer, not reach it (CAP-28 does that). Built 2026-10-13 (PR #21, roadmap decision P-12).
- **CAP-30 [R2, proposed] Watch while it arrives**
  - **intent:** Like Stremio, a person starts a received video before the whole file is in.
  - **success:** A video that is arriving shows Play once its start is in; playing fetches the next part first, and seeking far ahead waits plainly instead of failing. Proposed 2026-10-13 from the Stremio reference; Bruno confirms before an epic is written.
- **CAP-24 [R3] People I share with**
  - **intent:** After the first invite, a person sends to the same person again without copying anything, and only the people a share was sent to can get it.
  - **success:** Accepting an invite can also add the sender to "My people"; later shares sent to someone in "My people" appear in their "Shared with me" view when both devices are on; the content's fingerprint is no longer announced in the clear on the public lookup network. The design is settled by the spike in epic 13 before stories are written.

## Constraints

- AD-1 to AD-12 in the architecture companion are binding.
- No account, no cloud, no hosted service, no telemetry (AD-9). One exception since 2026-10-13: the helper (AD-23), open source and named in Settings, which carries files only while they are shared.
- No person ever changes a router setting or opens a port, on the sharing side or the receiving side (2026-10-13).
- A shared file is consumable by any standard torrent app, not only ComParta (2026-10-13).
- The target user can use a smartphone and email but nothing more complex; any step needing networking or torrent knowledge is a defect.
- Private by default: content reaches only people who hold its invite; nothing is listed or searchable. The invite itself is the secret: anyone holding it can download, so the app never presents it as safe to post publicly. The limits of this are stated in AD-8.
- Sharing never copies, moves, changes or deletes the original file (AD-6).
- The MVP is Windows desktop only; nothing in it may block the Android shell (AD-1).
- Open source. qBittorrent, Stremio, Google Drive and other products are reference only; no code is copied from them.
- Every story is buildable and checkable on its own (AD-12).

## Non-goals

- Public search or discovery of content from strangers.
- Cloud storage. The helper (AD-23) keeps a copy only while a file is shared and is not a place to store files.
- Power-user features on the main screen: queues, RSS, search plugins, per-connection detail. (Opening download links and download files, with speed limits and file choice under "More options", moved into scope as CAP-25 on 2026-10-10.)
- In the MVP: mobile, an installer.
- An update check, crash reporting or usage statistics of any kind (AD-9, AD-17).
- iOS, macOS and Linux before R3.
- Editing, previewing or syncing files inside ComParta. It looks like a file drive, but it is not a sync service: a share is a snapshot sent to people, not a folder kept in step.
- A hosted relay run by the project. Superseded 2026-10-13: Bruno's goal needs a helper on an always-reachable computer (AD-23); who runs it beyond his family is decision D-6.

## Success signal

- **MVP:** a person who has never used a torrent program adds a file on Windows, sends its invite to someone, and that person receives the file on their own PC; the original file is untouched.
- **Product:** two people in different homes move a large family video directly between their devices with no account and no hosted service.
- **R1 family pilot:** three non-technical relatives in at least two homes install ComParta from a link, send and receive a large file in Portuguese without help, and at least one transfer crosses two homes.
- **R1 publish check (2026-10-13):** a file shared from Bruno's PC, with no router or port setting changed, is downloaded by qBittorrent on another home's PC and by a torrent app on an Android phone on 4G.
- **R2:** a folder of 200 photos goes from a PC to an Android phone and back, started from the phone's share menu or a chat link.

## Assumptions

- The MVP shares single files only; folders and several files come in R2 (CAP-19).
- Both people keep ComParta running during a transfer (CAP-17 makes that the default). Until the helper (CAP-28) is in place, at least one of the two devices must accept incoming connections (CAP-18, AD-15); the test of 2026-10-13 showed that is often not the case.
- The pilot family speaks Brazilian Portuguese; English stays the source language of the catalog.
- A person's name in invites is whatever they type; it is not verified and the UI never calls it an account.
- Unfinished received data is kept in a hidden folder inside the Received folder and moved to its final name when complete.
- CAP-5 is not in the input; it follows from sharing files in place (AD-6).
- Remove asks for confirmation and states that the original file is kept.
- Adding the same file twice is rejected with a plain message instead of creating a duplicate.

## Open Questions

- OQ-1: Answered for the first release by AD-8 (invite carries the sender's address, plus local discovery and the public lookup network). On 2026-10-12 the maintainer accepted that shared files are reachable from the whole internet, with the limits stated in AD-8. The stronger model where only chosen people can download is CAP-24, settled by the epic 13 spike. Reliable connection when both people are behind strict routers is CAP-18; whether a chosen relay is allowed is decision D-3 in the roadmap.
- OQ-2: Answered 2026-10-10 by AD-13: no AppContainer; received files carry the Windows internet mark and go through the attachment services API, which runs the installed antivirus; programs and scripts ask before opening.
- OQ-3: Which open source license? Recommendation Apache-2.0; decision D-1 in the roadmap. Needed before the repository goes public and before epic 5 story 1.
- OQ-4: Answered in part by AD-17: no in-app update check; updates come from the store. The channel (Microsoft Store recommended, or paid code signing) is decision D-2 in the roadmap.
- OQ-5: Answered 2026-10-10: folders and several files are CAP-19, in R2, before Android.
- OQ-6: What success rate between homes is good enough to call CAP-18 met? Set from the epic 9 field test.
- OQ-7: Who runs the helper, and pays for it, once people outside Bruno's family use ComParta? Decisions D-5 and D-6 in the roadmap.
