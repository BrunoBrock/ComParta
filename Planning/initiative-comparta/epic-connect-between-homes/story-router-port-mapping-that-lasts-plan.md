---
title: 'Router port mapping that lasts, with the public address in the invite'
type: 'feature'
ticket: '6'
created: '2026-10-12'
status: done
baseline_revision: 'f1107973ca9211503d1736a4d9f3cfa681b37bbe'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: ''
review_source: ''
lenses_ran: []
review_loop_iteration: 0
context:
  - '{project-root}/Planning/initiative-comparta/architecture-comparta/architecture-comparta.md'
  - '{project-root}/Planning/initiative-comparta/epic-connect-between-homes/story-engine-reports-reachability-facts-core-turns-them-into-healt-plan.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A device outside the home network cannot find the sender. The invite carries only local addresses, nothing reads the router's own address, and nothing renews the router mapping: a NAT-PMP lease ends after two hours, and a router that restarts forgets it.

**Approach:** The engine reads the router's external address through Mono.Nat and reports it as the fact `ExternalAddress`; it asks the router for the mapping again every ten minutes; and the invite carries the public address with the router's external port first, only when that address is public.

## Boundaries & Constraints

**Always:** Nothing leaves the local network beyond what Mono.Nat already sends to the router (AD-9). Subscribe to `NatUtility.DeviceFound` before the engine starts and never start or stop discovery; the handler catches everything and never blocks the search loop. An invite holds at most six address hints (a receiver keeps six), the public one first, no duplicates. `GetInviteAsync` reads the cached address and makes no network call. Tests use fake devices and the fake router, never `SharingPolicy.Default`, a real router or `NatUtility`.

**Never:** Build PCP (Mono.Nat has none). Listen on IPv6 (story 5). Add the firewall fact (story 4). Change core, the contract, the UI or how a receiver reads hints. Put a private, shared-range, IPv6 or unknown address in an invite. Build edge cases from `todo-later.md`. Replace MonoTorrent's forwarder.

## I/O & Edge-Case Matrix

| Situation | Result |
|---|---|
| address in invite is off in the policy | no address hints at all |
| mapping obtained, external port known, external address a public IPv4 | invite starts with that address and the external port, then local hints |
| mapping pending, refused or not requested | local hints only |
| external address unknown, private, shared range (100.64/10) or IPv6 | local hints only; the fact keeps the raw address, so health is limited when the router is behind carrier-grade NAT |
| a router is found | its address is read at once and every ten minutes, and each mapping MonoTorrent holds is asked for again |
| the router is gone or errors | no error escapes; the last good address is kept for fifteen minutes, then it is unknown |
| the app stops cleanly | the router mapping is removed (MonoTorrent's stop does it) |

</frozen-after-approval>

## Code Map

- `src/ComParta.Engine.MonoTorrent/MonoTorrentTransferEngine.cs:61-74,307-328` -- internal constructor `(paths, Factories, TimeProvider, isOutsideLocalNetwork)`: add an optional `RouterWatcher`. `GetInviteAsync` appends one `x.pe` hint per local address (`LocalAddresses`, first six, no dedupe): replace with a pure internal function that takes the public endpoint and the local addresses. `StartAsync` and `StopAsync` start and dispose the watcher and its timer.
- `src/ComParta.Engine.MonoTorrent/ReachabilityProbe.cs` -- `Read` hardcodes `ExternalAddress: null`; fill it from the watcher when forwarding is allowed. `ReadMapping` already gives `ExternalPort`.
- New `src/ComParta.Engine.MonoTorrent/RouterWatcher.cs` -- keeps the routers Mono.Nat found, reads their external address, asks again for the mappings.
- Verified (Mono.Nat 3.0.0, MonoTorrent 3.0.2): `NatUtility.DeviceFound` is static, replays nothing and has no "lost" event; its handler runs on the search loop. MonoTorrent starts discovery for UPnP and NAT-PMP when the first manager starts, never renews, never re-adds after a router restart, and its `Created` list holds the requested ports. `INatDevice` is public: `GetExternalIPAsync()` (UPnP: one request on the local network; NAT-PMP: cached, no traffic), `CreatePortMapAsync(Mono.Nat.Mapping)`. `Mono.Nat.Mapping(protocol, privatePort, publicPort, 0, description)`. Re-asking through the device, not MonoTorrent's forwarder, avoids the mapping flickering to pending.
- Shell: `App.xaml.cs:21` and `AppServices.cs:55` stop the engine when the window closes, waiting five seconds; nothing to change.
- `tests/ComParta.Engine.MonoTorrent.Tests/TwoDevicesTests.cs:135-153,170` -- invite tests: the last hint must still end with the listen port, so local hints stay last; none under the closed policy.
- `tests/ComParta.Engine.MonoTorrent.Tests/ReachabilityFactsTests.cs` -- `FakeRouter` (private nested), `WithRouter`, `StartAsync`, `TestClock`; lines 156 and 219 assert a null `ExternalAddress`, which stays true with no watcher address. Move the shared fakes to a shared file in the test project.

## Tasks & Acceptance

**Execution:**
- [ ] `src/ComParta.Engine.MonoTorrent/RouterWatcher.cs` -- device list; `Current` raw external IPv4 (never 0.0.0.0); `RefreshAsync` reads each device and re-asks each held mapping; last good address kept fifteen minutes; every error swallowed; optional `NatUtility` subscription whose handler hands off to a task; `Dispose` unsubscribes -- the only code that touches Mono.Nat
- [ ] `MonoTorrentTransferEngine.cs`, `ReachabilityProbe.cs` -- optional watcher; subscribe before `new ClientEngine`; ten-minute timer and a refresh when a device is found; fill `ExternalAddress` only when forwarding is allowed; invite hints built by the pure function; dispose on stop
- [ ] `tests/ComParta.Engine.MonoTorrent.Tests` -- shared fakes file; `RouterWatcherTests.cs` (fake device: address read, unspecified address, private address kept raw, error then grace then unknown, re-ask for each held mapping with the same ports, nothing re-asked without a mapping); hint function tests (public first, no duplicate, at most six, IPv6 ignored); invite tests for every matrix row with the fake router and a fake device; stopping removes the mapping on the fake router; closed policy sends nothing

**Acceptance Criteria:**
- Given a fake router that agreed and a device reporting a public address, when an invite is copied, then its first hint is that address with the router's external port, local hints follow, and there are at most six.
- Given a private, shared-range or unknown address, or a mapping not obtained, or the policy off, when an invite is copied, then it carries no public hint (and none at all with the policy off).
- Given a known device, when a refresh runs, then the address is read again and each held mapping is asked for again; an error leaves the last good address for fifteen minutes and escapes nowhere.
- Given the engine stops, then the fake router saw the mapping removed.
- Given any test, then nothing real is contacted.
- Given the full build, then every test passes.

## Implementation Notes

## Plan Change Log

## Review Triage Log

## Design Notes

Decided in the epic Notes: the router's address is read through Mono.Nat, a shared-range address means carrier-grade NAT, the public address goes first. Planner's choices, to confirm: a ten-minute refresh (covers the two-hour NAT-PMP lease and a router restart) and a fifteen-minute grace for a missing address. The fact keeps the raw address so core can say "limited"; only a public one reaches the invite. MonoTorrent's `Created` list holds the requested port, not one the router may have changed, so the external port is in practice the listen port; read it from the facts anyway.

## Verification

`dotnet` is at `C:\Program Files\dotnet`; add it to PATH if the shell does not find it. Do not commit, and do not stage or edit anything outside this story (`_bmad/render/` is not yours). Never run `npm ci`: a dev server holds files in `src/ui`; use `npm install` if packages are missing.

**Commands:**
- `dotnet test tests/ComParta.Engine.MonoTorrent.Tests -c Release` -- expected: all pass
- `.\build.ps1 Check` -- expected: exit 0 (UI tests, build, all C# tests)
- Manual, maintainer only: on a home router with UPnP on, health becomes good and the invite carries the router's external address.
