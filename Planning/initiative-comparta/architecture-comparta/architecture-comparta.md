---
name: 'ComParta'
type: architecture-spine
purpose: build-substrate
altitude: initiative
paradigm: 'hexagonal (ports and adapters), web-view UI as a driving adapter'
scope: 'ComParta whole product: Windows (R1), folders and Android (R2), people I share with (R3)'
status: final
created: '2026-10-08'
updated: '2026-10-10'
binds: []
sources:
  - '../../ReadMe.md'
companions:
  - '../ux-comparta/ux-comparta.md'
---

# Architecture Spine — ComParta

## Design Paradigm

Hexagonal (ports and adapters). The core holds the product's rules and knows nothing about the screen, the torrent engine, the database or the operating system. Everything else plugs into ports the core defines.

| Layer | Namespace / directory | Holds |
| --- | --- | --- |
| Core | `ComParta.Core` | Domain (`Share`, `Transfer`, `Person`, `Invite`), use cases, ports |
| Contracts | `ComParta.Contracts` | Bridge command names, event names, DTOs, error codes, and `BridgeRouter`, which runs a command against the core without any shell |
| Driven adapters | `ComParta.Engine.MonoTorrent`, `ComParta.Storage.Sqlite` | Implementations of core ports |
| Shell (driving adapter) | `ComParta.App` | .NET MAUI host, `BridgeHost`, platform ports (file picker, content opener, paths) |
| UI | `src/ui` | React app; renders state, sends commands |

## Invariants & Rules

```mermaid
graph TD
  UI[src/ui React] -->|bridge only| App[ComParta.App shell]
  UI -.->|generated types| Contracts[ComParta.Contracts]
  App --> Core[ComParta.Core]
  App --> Contracts
  App --> Engine[ComParta.Engine.MonoTorrent]
  App --> Storage[ComParta.Storage.Sqlite]
  Engine --> Core
  Storage --> Core
  Contracts --> Core
```

An arrow means "may depend on". `ComParta.Core` depends on no other project and on no MAUI, MonoTorrent, SQLite or web-view package. Only `ComParta.App` wires adapters to ports.

### AD-1 — C# core, React UI, MAUI shell [ADOPTED]

- **Binds:** all
- **Prevents:** a codebase the maintainer cannot read, and separate cores per platform.
- **Rule:** All product logic is C# in `ComParta.Core`. All screens are React in `src/ui`. The shell is .NET MAUI with one `HybridWebView`; the same core and UI must serve Windows now and Android later. `[ASSUMPTION]` MAUI on Windows is adequate for the MVP; a WPF + WebView2 shell is the fallback and must stay a drop-in because of AD-2 and AD-3.

### AD-2 — The torrent engine lives behind one port

- **Binds:** engine, core, storage, contracts
- **Prevents:** engine lock-in; engine types spreading into storage, contracts and UI.
- **Rule:** Core talks to the engine only through `ITransferEngine`. MonoTorrent types, info-hashes, magnet links and `.torrent` data never appear outside `ComParta.Engine.MonoTorrent`. The adapter maps `ShareId` to its own engine identifiers and keeps that mapping in its own data folder.

### AD-3 — One bridge between UI and core

- **Binds:** UI, shell, contracts
- **Prevents:** each story inventing its own channel (local HTTP server, ad hoc messages, direct JS file access).
- **Rule:** The UI reaches the app only through `src/ui/src/bridge`. Commands go UI → core as `InvokeDotNet` calls on the single `BridgeHost` class. Events go core → UI as one raw-message envelope. No local web server, no `fetch` to anything, no other interop. Command names, event names and DTOs are declared once in `ComParta.Contracts`; the TypeScript types are generated from it and never written by hand.

### AD-4 — Core owns state; the UI is a projection

- **Binds:** UI, core
- **Prevents:** two sources of truth; UI state drifting from what is really shared.
- **Rule:** The UI keeps no product state of its own beyond view state (open dialog, selected row). It loads a snapshot with a query command and updates from change events. Every change is a command; the UI never edits its copy optimistically without a command.

### AD-5 — Plain-language vocabulary

- **Binds:** UI, contracts, core
- **Prevents:** torrent jargon leaking to users through names that started in code.
- **Rule:** The words seed, peer, leech, swarm, tracker, magnet, torrent, hash and DHT are allowed only inside `ComParta.Engine.MonoTorrent`. Everywhere else the terms are `Share` (something you offer), `Transfer` (bytes moving), `Person`, `Invite`.

### AD-6 — Sharing never touches the original; receiving has one home

- **Binds:** core, engine, shell, UI
- **Prevents:** stories disagreeing on what "remove" means, causing data loss; received files scattered across the disk.
- **Rule:** Adding a share references the file where it is; it is never copied, moved or modified. Removing a share stops offering it and deletes only ComParta's own records; the original file is never deleted or changed. Received content is written only under the single Received root supplied by `IAppPaths`, and every path taken from received metadata passes through one sanitising function in core before use.

### AD-7 — File paths enter only through the native picker

- **Binds:** UI, shell, core
- **Prevents:** path handling split between JavaScript and C#; UI code passing arbitrary paths to the core.
- **Rule:** No bridge command accepts a filesystem path from the UI. Choosing a file is a command that makes the shell open the native picker behind `IFilePicker`; the core receives the result. The UI is given display names and sizes, never a path it must send back.
- **Amended 2026-10-10:** three more native entry points count as "the picker": files dropped on the window, caught by the shell's native drop target (not the web view's); items handed over by the operating system's share menu on Android; and a folder or several files picked at once (AD-19). All three go through `IFilePicker`'s sibling port `IIncomingItems`, which raises an event the core turns into shares. The UI still never sends a path, and on Android an item is a content reference, not a path (AD-20).

### AD-8 — Network behaviour is set in one place; devices meet only through an invite

- **Binds:** engine, core
- **Prevents:** a story switching on wider discovery and exposing family files; inconsistent network settings per share.
- **Rule:** All discovery, encryption and port settings come from one `SharingPolicy` object in core, applied only by the engine adapter. The policy (decided 2026-10-08) allows three ways for two devices to meet, all keyed by the invite: the sender's own network address carried inside the invite, local-network discovery, and the public BitTorrent lookup network (DHT). Peer exchange and trackers stay off, addresses of servers found in a pasted invite are dropped, and connections are encrypted. No other code sets engine network options.
- **Accepted trade-off:** the content's fingerprint is announced on the public lookup network, so a share is unlisted, not secret: only someone holding the invite can ask for it, but the fingerprint is not hidden from that network. A stronger model (per-person keys) stays in Deferred.

### AD-9 — No hosted services, no accounts, no telemetry

- **Binds:** all
- **Prevents:** a cloud dependency arriving through a convenience feature.
- **Rule:** No code calls a service operated by the ComParta project or a third-party hosted API, and nothing is collected or sent. There is no sign-in. When identity is needed it is a key pair generated and kept on the device.

### AD-10 — Each kind of data has one owner

- **Binds:** storage, engine, core
- **Prevents:** two components writing the same record; engine cache mixed with product data.
- **Rule:** Product data lives in one SQLite database written only by `ComParta.Storage.Sqlite` through core repository ports. Engine cache and resume data live in a sibling folder written only by the engine adapter. Both locations come from `IAppPaths`. `ShareId` is a GUID generated by core.

### AD-11 — Errors are codes; words belong to the UI

- **Binds:** core, contracts, UI
- **Prevents:** English prose from C# appearing in a translated screen; UI stories matching on message text.
- **Rule:** Every command returns `{ ok, value }` or `{ ok: false, error: { code } }`. Codes are stable strings declared in `ComParta.Contracts`. All user-facing text lives in the UI's translation catalog, keyed by code; core and adapters return no display text.

### AD-12 — Every side runs without the other

- **Binds:** UI, core, engine
- **Prevents:** stories that can only be built or checked with the whole app running.
- **Rule:** `src/ui` runs in a normal browser against a fake bridge that implements the full contract. `ComParta.Core` is tested against in-memory fakes of every port. A story that adds a command adds it to the contract, the fake bridge and the core tests in the same change.

### AD-13 — Received content is marked and checked by Windows, not by us

- **Binds:** core, shell, UI
- **Prevents:** a home-made "virus check" that gives false comfort; received programs launched by one careless double-click; an AppContainer sandbox that would break sharing files in place.
- **Rule:** When a received item is complete, the shell's `IReceivedFileGuard` hands each file to the Windows attachment services API (`IAttachmentExecute`), which writes the "came from the internet" mark and lets the installed antivirus inspect it; a file the antivirus blocks becomes the transfer state `blocked` and is not shown as openable. Core decides which received files are "programs" from one list of risky extensions in `ReceivedNames` (executables, installers, scripts, shortcuts, Office macro formats); opening one needs a second, explicit confirmation command. Nothing received is ever opened except by an explicit `received.open` command. The app does not run in an AppContainer: sharing in place needs read access to files anywhere the person keeps them. On Android the guard is a no-op and the platform's own sandbox applies.
- **Answers:** OQ-2.

### AD-14 — The app stays present while it has work, through one shell port

- **Binds:** shell, core
- **Prevents:** transfers silently stopping when the window closes; each platform inventing its own background behaviour inside core.
- **Rule:** Core reports `HasActiveWork` (anything shared and ready, or any transfer not finished) through `IBackgroundPresence`. On Windows the shell keeps a notification-area icon and hides the window instead of quitting while there is active work, enforces a single running instance (a second launch, including one started by a `comparta:` link, forwards its arguments to the first and exits), registers start-with-Windows only when the setting is on, and asks Windows not to sleep while bytes are moving. On Android the same port runs a foreground service with a progress notification while a transfer is running. Quitting with a transfer in progress is a command the UI confirms first.

### AD-15 — Reachability is improved and explained, never faked with a server

- **Binds:** engine, core, shell, UI
- **Prevents:** a hidden hosted relay or "connectivity check" server sneaking in to make home-to-home work; network jargon reaching the person.
- **Rule:** The engine adapter listens on IPv4 and IPv6, asks the router for a port mapping (UPnP and NAT-PMP/PCP) when `SharingPolicy.AllowPortForwarding` is on, puts every address it has good reason to think is reachable (mapped IPv4, global IPv6) into the invite, and keeps looking for the other side on the lookup network so that a transfer works when *either* device is reachable. It reports neutral facts (`ReachabilityFacts`: mapping obtained, global IPv6 present, listening, firewall rule present, recent incoming connection seen) through `ITransferEngine`; core reduces them to `ConnectionHealth` (`good`, `limited`, `blocked`) and the UI explains that in plain words (UX design, "Stuck transfer"). No code contacts a service to test reachability. A relay, if decision D-3 allows one, is another adapter behind the same port, opt-in, and run by someone the person chooses.

### AD-16 — An invite is a ComParta link wrapping opaque engine text

- **Binds:** core, engine, shell, UI, contracts
- **Prevents:** engine words in what people paste; every story parsing invites its own way; a display name in an invite being trusted as a file name.
- **Rule:** `InviteCodec` in core is the only code that reads or writes invites. An invite is `comparta://receive/v1/<payload>`, where the payload is base64url of a small JSON object: the engine's opaque invite text (AD-2, never inspected by core), and display hints (sender name, item name, total size, item count) that the receive dialog shows labelled as "from the invite". The codec finds the link anywhere inside pasted text (a whole chat message), accepts the first build's raw engine text for compatibility, and rejects anything else with `invite.invalid`. Display hints never become file names: names on disk come only from the received metadata through the sanitising function (AD-6). The friendly message around the link is composed by the UI from the catalog. The shell registers the `comparta:` scheme with the operating system.

### AD-17 — Distribution through stores; no update check of our own

- **Binds:** shell, CI, all
- **Prevents:** an update check that is a network call to a service (AD-9); unsigned builds that Windows blocks for the people we most want to reach.
- **Rule:** The Windows release is an MSIX package published through the Microsoft Store (subject to decision D-2), which signs it and delivers updates; the same build is also attached to a GitHub Release as a zip for people who cannot use the Store. Android is an AAB on Google Play and a signed APK on GitHub Releases (decision D-4). The app contains no code that checks for updates; Settings shows the version. Releases are built only by the CI release workflow from a tag. The database carries a schema version and migrates forward at start-up; a newer app never needs the person to uninstall.

### AD-18 — The UI owns a small design system; pictures come from the operating system

- **Binds:** UI, shell, contracts
- **Prevents:** a UI framework that fights the plain-language and accessibility rules; thumbnails made by reading file contents in JavaScript (which would need paths, AD-7).
- **Rule:** Screens use the components and CSS custom-property tokens in the UX design, written in `src/ui/src/components/ui`, with Lucide icons bundled at build time; no component framework and no CSS framework. Thumbnails are requested with `thumbnails.get { id, size }` and returned as a small PNG data URL produced by the shell's `IThumbnailProvider` (the Windows shell thumbnail cache; Android's media store), cached by the shell in a folder from `IAppPaths`. The UI never asks for a file's bytes.

### AD-19 — A share is a set of items with relative paths

- **Binds:** core, storage, engine, contracts, UI
- **Prevents:** folders bolted onto a one-file model; a received folder tree escaping the Received root.
- **Rule:** From R2 a `Share` has one or more `ShareItem`s (`RelativePath`, `SizeBytes`, and the platform source reference), and a `Kind` (`file`, `folder`, `collection`). A single file is a share with one item; existing shares migrate to that form. A share is a snapshot: files added to a folder later are not part of it, and an item that changes makes the share `partlyUnavailable`. The receiver recreates the relative paths under `Received/<share name>/`, each segment passing the sanitising function, with limits on item count, path depth and name length that fail the transfer with `invite.tooLarge` rather than truncating silently.

### AD-20 — Shared content is read through a platform port, not file paths

- **Binds:** core, engine, shell
- **Prevents:** Android support needing a second engine adapter; content URIs leaking into core logic.
- **Rule:** A share item's source is an opaque `ContentRef` that only the shell understands (a Windows path, or an Android content URI with a persisted read grant). The engine adapter reads shared bytes and checks existence through `IContentSource` (open for read at an offset, length, last-changed time), which the shell implements; MonoTorrent is given a piece writer backed by that port for offered content. Received content is still written by the engine under the Received root; on Android the shell then publishes finished items to the shared Downloads collection.

## Consistency Conventions

| Concern | Convention |
| --- | --- |
| Naming | C# projects and namespaces `ComParta.<Area>`; ports `I<Noun>`; DTOs end in `Dto`. Bridge commands `area.verbNoun` (`shares.add`, `shares.remove`, `shares.list`); events `area.changed`. React components PascalCase, one per file. |
| Data & formats | Ids are GUID strings. Times are ISO 8601 UTC. Sizes are bytes as integers; the UI formats them. JSON across the bridge is camelCase. Event envelope: `{ event, payload }`. |
| State & cross-cutting | Mutations only by commands (AD-4). Logs are local files under `IAppPaths`; never log file contents. Settings are stored in the product database. UI text only through the translation catalog; English first, Brazilian Portuguese planned. |
| Testing | xUnit for C#, Vitest for UI. Core tests use fakes, not MonoTorrent. Engine adapter has its own integration tests. UI tests run `vitest-axe` on every screen (CAP-23). |
| Languages | English is the catalog's source; every other language is a catalog with the same keys, checked by a test. The language is a setting stored in the product database; the shell supplies the system language as the default. |
| Platform ports | Anything that differs between Windows and Android is a port in core with one implementation per platform in `ComParta.App/Platforms/<OS>`: `IFilePicker`, `IIncomingItems`, `IContentSource`, `IContentOpener`, `IReceivedFileGuard`, `IBackgroundPresence`, `INotifier`, `IThumbnailProvider`, `IClipboard`, `IAppPaths`. |

## Stack

| Name | Version |
| --- | --- |
| .NET (LTS) | 10.0.x |
| .NET MAUI (`HybridWebView`) | 10.0.x |
| MonoTorrent | 3.0.2 |
| Microsoft.Data.Sqlite | 10.0.x |
| React | 19.2.x |
| Vite | 8.1.x |
| TypeScript | version shipped by the Vite 8.1 React template |
| xUnit / Vitest | latest stable at scaffold time |
| Lucide icons (`lucide-react`) | latest stable when epic 8 starts; bundled |
| vitest-axe | latest stable when epic 8 starts |
| Mono.Nat (comes with MonoTorrent) | as pinned by MonoTorrent |

Exact patch numbers are pinned by the scaffold story.

## Structural Seed

```mermaid
graph LR
  subgraph Device[The person's Windows PC]
    subgraph AppProc[ComParta.App process]
      WV[HybridWebView: React UI] <-->|bridge| BH[BridgeHost]
      BH --> UC[Core use cases]
      UC --> ENG[MonoTorrent adapter]
      UC --> DB[(SQLite)]
    end
    ENG -->|reads in place| Orig[Original files]
    ENG -->|writes only here| Recv[Received root]
  end
  ENG <-.->|direct, policy-controlled| Other[Another person's device]
```

```mermaid
erDiagram
  SHARE ||--|{ SHARE_ITEM : "contains (R2)"
  SHARE ||--o{ TRANSFER : "is moved by"
  PERSON ||--o{ INVITE : "receives"
  SHARE ||--o{ INVITE : "is offered through"
  PERSON ||--o{ DEVICE : "owns (R3)"
  SETTING }o--|| APP : "configures"
```

`SHARE` and `TRANSFER` exist in the first release; `SETTING` arrives with R1 (language, your name, start with Windows, received folder, theme, view). `SHARE_ITEM` arrives with R2 (AD-19). An invite is a ComParta link (AD-16) and is not stored as an entity; `PERSON` and `DEVICE` arrive with R3 after the epic 13 spike.

```text
ComParta/
  src/
    ComParta.Core/               # domain, use cases, ports
    ComParta.Contracts/          # bridge commands, events, DTOs, error codes
    ComParta.Engine.MonoTorrent/ # ITransferEngine implementation
    ComParta.Storage.Sqlite/     # repository implementations
    ComParta.App/                # MAUI shell, BridgeHost, platform ports
    ui/                          # React + Vite; build output -> ComParta.App/Resources/Raw/wwwroot
  tests/
    ComParta.Core.Tests/         # core, bridge router and architecture guards
    ComParta.Storage.Sqlite.Tests/
    ComParta.Engine.MonoTorrent.Tests/
  tools/
    ComParta.Contracts.TypeGen/  # writes src/ui/src/bridge/contract.generated.ts
  Planning/
```

Operational envelope: the product is an installed desktop app with no servers and no environments beyond the developer's machine and the user's device. Continuous integration builds and tests on a Windows runner.

## Deferred

| Item | Why it can wait | Revisit when |
| --- | --- | --- |
| Stronger privacy model: per-person keys so that only chosen people can download, and no fingerprint on the public lookup network | The first release uses the invite-as-secret model in AD-8. A design spike (epic 13 story 1) comes first; one candidate is announcing a value derived from a secret in the invite instead of the content fingerprint. | R3, or before the app is offered outside the maintainer's circle, whichever is first. |
| A relay for two devices that cannot reach each other | AD-15 covers IPv6, port mapping and either-side-reachable first. Any relay must be opt-in and run by someone the person chooses (decision D-3). | After the epic 9 field test measures how often homes fail to connect. |
| Another engine with uTP and hole punching | AD-2 keeps a swap cheap; MonoTorrent works on the same network and when one side is reachable. | Only if the field test shows failures a different engine would fix. |
| Open source license | Does not affect code structure; MonoTorrent's MIT license fits MIT, Apache-2.0 and GPL-3.0. Decision D-1. | Before the repository is made public. |
| iOS, macOS and Linux shells | AD-1 keeps the path open; not in R1 to R3. | After R3. |
| Type generation tool for `ComParta.Contracts` → TypeScript | Built in the first release as `tools/ComParta.Contracts.TypeGen`. | Closed. |
| Packaging, code signing and updates | Answered by AD-17; the channel is decision D-2. | Closed once D-2 is answered. |
| Operating-system sandbox on Windows, and scanning or marking of received files | Answered by AD-13. | Closed. |
| Android shell, background sharing on phones | Answered by AD-14 and AD-20; the phone layout is in the UX design. | Closed; built in epic 7. |
| Accessibility and visual design system | Answered by AD-18 and the UX design. | Closed. |
