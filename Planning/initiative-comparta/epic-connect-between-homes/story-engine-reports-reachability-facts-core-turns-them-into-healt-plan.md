---
title: 'Engine reports reachability facts; core turns them into health'
type: 'feature'
ticket: '2'
created: '2026-10-12'
status: done
baseline_revision: '93cd84d0951eb1a841755ec5d8dfacf11f6ddfcb'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 0
context:
  - '{project-root}/Planning/initiative-comparta/architecture-comparta/architecture-comparta.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Nothing tells the app, or the person, whether other devices can reach this computer. The later indicator, help page and invite work (stories 3 to 7) need one trustworthy answer built only from what the engine observes (AD-15, AD-9).

**Approach:** The engine reports neutral `ReachabilityFacts` through `ITransferEngine`; core reduces them to `ConnectionHealth` (good, limited, blocked, or none when nothing can be measured) and exposes `connection.get` and `connection.changed` over the bridge, with generated types and fake-bridge support. No UI text. The Windows Firewall fact is deferred to story 4 (`deferred-work.md`).

## Boundaries & Constraints

**Always:** Say "another device" in code and comments outside the engine project, never the nine banned words (a guard test fails on them, including in comments and test names). Health rules live only in core. Nothing contacts a service to test reachability. The closed policy never touches a router. Contract, fake bridge, core tests and generated TypeScript change together (AD-12).

**Never:** Add UI strings or a screen (story 3). Listen on IPv6 (story 5). Read the router's external address (story 6; the fact exists and stays null). Add the firewall fact or rule (story 4). Start or stop Mono.Nat discovery (it is global). Build edge cases from `todo-later.md`. Touch files outside this story.

## I/O & Edge-Case Matrix

Rules, first match wins (`ExternalAddress` null means not read yet):

| Facts | Health |
|---|---|
| not listening and port not busy (nothing shared or received) | none (null) |
| listen port busy | blocked |
| incoming connection from outside seen in the last hour | good |
| mapping obtained and external address null or public; or global IPv6 present and listening on IPv6 | good |
| mapping pending | none (still asking the router) |
| mapping obtained, address private or shared range (10/8, 172.16/12, 192.168/16, 100.64/10) | limited |
| mapping refused or not requested, no usable IPv6 | blocked |

</frozen-after-approval>

## Code Map

- `src/ComParta.Core/Domain.cs` -- add `ConnectionHealth`, `PortMappingState` (NotRequested, Pending, Obtained, Refused), `ReachabilityFacts` (Listening, ListenPortBusy, PortMapping, ExternalPort int?, ExternalAddress IPAddress?, GlobalIPv6Present, ListeningOnIPv6, IncomingConnectionSeenRecently) and `NetworkAddresses.IsPublic`. Namespace `ComParta.Core`, 4-space indent.
- `src/ComParta.Core/Ports.cs:26-60` -- `ITransferEngine`: add `event Action<ReachabilityFacts>? ReachabilityChanged;` beside the two events.
- `src/ComParta.Core/UseCases/ShareUseCases.cs:23,27,147-164` -- pattern: subscribe in the constructor, `Changed` event, skip no-ops, raise outside the lock. Copy for the new `ConnectionUseCases.cs` (takes the engine; `Current()` returns the health or null).
- `src/ComParta.Contracts/Contract.cs:9-28,51-66,89-111` -- add `ConnectionGet`, `ConnectionChanged`, `ConnectionHealthDto`, `ConnectionDto(ConnectionHealthDto? Health)` and the `CommandSpecs`/`EventSpecs` entries.
- `src/ComParta.Contracts/BridgeRouter.cs:20-27,50-111,130-155` -- constructor parameter, event wiring, `case`, `ToDto`. Two call sites break: `src/ComParta.App/AppServices.cs:34` (tabs) and `tests/ComParta.Core.Tests/BridgeRouterTests.cs:24`.
- `src/ui/src/bridge/contract.generated.ts` -- regenerate with `dotnet run --project tools/ComParta.Contracts.TypeGen`; a stale file fails `GuardTests`.
- `src/ui/src/bridge/fakeBridge.ts:17-37,62-64,130-236` -- option `connection`, handler `'connection.get'`, `setConnection`, event emit. A missing handler fails silently, so test it.
- `src/ComParta.Engine.MonoTorrent/MonoTorrentTransferEngine.cs:57-74,77-93,183,383,422-470` -- events, `StartAsync`, settings, manager creation, the 500 ms report loop. Add an internal constructor taking `Factories` and `TimeProvider`.
- Verified engine facts: `ClientEngine.PortMappings` is public (`Created`, `Pending`, `Failed`; each `Mapping` has `Protocol`, `PrivatePort`, `PublicPort`). The router mapping is registered only when the first manager starts and removed when the last stops; the listener binds then too, and a busy port ends as `ListenerStatus.PortNotFree` without throwing. Read the listener through `Factories.Default.WithPeerConnectionListenerCreator`; fake the router with `WithPortForwarderCreator`. Incoming: `TorrentManager.PeerConnected`, `Direction.Incoming`, `args.Peer.Uri`.
- `tests/ComParta.Core.Tests/Fakes.cs:29-101` -- `FakeEngine`: add the event and a `Report(ReachabilityFacts)` helper. Test style and `[Theory]` tables: `ShareUseCasesTests.cs`.
- `tests/ComParta.Engine.MonoTorrent.Tests/TwoDevicesTests.cs:30-81` -- engine construction, `FreePort`, link-only receive; `PolicyTests.cs` for settings tests. Use `SharingPolicy.Closed`, never `Default`.

## Tasks & Acceptance

**Execution:**
- [x] `src/ComParta.Core/Domain.cs`, `Ports.cs`, `UseCases/ConnectionUseCases.cs` -- types, port event, the rules above, `Changed` only when health changes -- core owns the meaning
- [x] `tests/ComParta.Core.Tests/Fakes.cs`, `ConnectionUseCasesTests.cs` -- one theory row per matrix row, one change event per real change, `IsPublic` ranges
- [x] `src/ComParta.Contracts/Contract.cs`, `BridgeRouter.cs`, `tests/ComParta.Core.Tests/BridgeRouterTests.cs` -- command, event, DTOs; null health when idle; regenerate the TypeScript
- [x] `src/ComParta.App/AppServices.cs` -- build the service before `_engine.StartAsync` and pass it to the router
- [x] `src/ui/src/bridge/fakeBridge.ts`, new `fakeBridge.test.ts` -- seed and change each health and none
- [x] `src/ComParta.Engine.MonoTorrent/*` (new `ReachabilityProbe.cs`) -- listener status, mapping state, IPv6 check, outside-incoming window, one facts report every 2 s, raised only on change
- [x] `tests/ComParta.Engine.MonoTorrent.Tests/ReachabilityFactsTests.cs` -- idle; listening with no mapping under the closed policy; busy port; fake router for obtained, pending, refused; outside-incoming with the one-hour window; the IPv6 function

**Acceptance Criteria:**
- Given the closed policy and a link-only receive, when facts are reported, then listening is true, mapping is not requested and no router is contacted.
- Given a busy listen port, when something is received, then the facts say the port is busy and core reports blocked.
- Given a fake router that registered the mapping, when reported, then mapping is obtained and the external port is the router's public port.
- Given an incoming connection judged outside the local network, when reported, then the fact is true for one hour and false after.
- Given nothing shared or received, when `connection.get` runs, then health is null, and `connection.changed` is raised on each real change.
- Given the full build, then every test passes and the generated TypeScript is current.

## Implementation Notes

- The engine's internal constructor is `(IAppPaths, Factories, TimeProvider, Func<IPAddress, bool>? isOutsideLocalNetwork = null)`. The fourth parameter is optional and only for tests: it lets a loopback connection count as "from outside", so the real `PeerConnected` path (direction and `Peer.Uri`) is tested end to end. The default is core's `NetworkAddresses.IsPublic`.
- With `AllowPortForwarding` off, the engine still calls `RegisterMappingAsync` on its forwarder (bookkeeping only, it is never started), so "no router is contacted" is tested as "the forwarder was never started and opened nothing". The facts say `NotRequested` from the policy, not from the forwarder's lists.
- With forwarding on and the listener up but no mapping in any list yet, the facts say `Pending`, so the first moments are none and never blocked.
- A router that has not answered within thirty seconds (`ReachabilityProbe.PendingLimit`, by the injected clock) counts as refused. The count starts when pending is first read and starts over when the state leaves pending or the engine stops listening; a router that records nothing at all is treated the same as one that stays pending. It was not tried against a real router here.
- `ListeningOnIPv6` is read from the listeners the engine created, so it turns true by itself once story 5 adds an IPv6 listener. `ExternalAddress` stays null until story 6.
- The generated TypeScript was regenerated with the type generator, which writes LF line endings; the other files in the working copy are CRLF.

## Plan Change Log

## Review Triage Log

Pass 1 (quick lens, 2026-10-12): 0 high, 1 medium, 3 low, 0 false, 0 maybe-false.

| Verdict | Route | Finding and evidence |
|---|---|---|
| medium | patch | A router that never answers, or is absent, never becomes `Refused`: `ReadMapping` returns `Refused` only from the `Failed` list (`ReachabilityProbe.cs:104`) and otherwise falls to `Pending` (`:106`), which core turns into none. A PC behind a router with UPnP off then shows no health instead of blocked. Also found independently while reading the diff. Routed patch, not bad_plan: the plan's own text settles the reading ("first seconds", and the `Refused` doc "no router that can be asked"), and the fix is local to one internal method with no public surface; the 30 s limit is the planner's choice, to confirm. |
| low | patch | The branch "forwarding on, listening, no list holds the port yet" (`:106`) has no test; every router fake puts the mapping into a list. Fix: a test with a router that ignores the request. |
| low | patch | `ReportReachability` stores the facts before raising (`MonoTorrentTransferEngine.cs:116`, `:118`), so a failed delivery is dropped, and the comment "the next tick tries again" (`:122`) is false. A retry would not help anyway: core skips an unchanged health (`ConnectionUseCases.cs:58`), and `connection.get` always has the current answer. Fix: correct the comment only. |
| low | patch | The skip guard (`:107`) makes a direct `ReportReachability()` call in a test a silent no-op when the 2 s timer is mid-report, and the tests append to a plain `List<T>` from the timer thread (`ReachabilityFactsTests.cs:257-258`, `:364-365`). A rare flaky failure. Fix: serialise the report with a lock instead of skipping, and use thread-safe collections in the tests. |

## Design Notes

Decided 2026-10-12 (maintainer): while idle the health is null, not a fourth value. Mapping pending is none, so the first seconds never flash red. `ExternalAddress` and `ListeningOnIPv6` exist now so the carrier-grade-NAT rule and IPv6 are tested before stories 5 and 6 fill them; until then they are null and false. The engine owns the one-hour window, so core needs no timer. The firewall fact and its rule (missing rule counts as blocked, decided the same day) move to story 4.

## Verification

`dotnet` is at `C:\Program Files\dotnet`; add it to PATH if the shell does not find it. Do not commit, and do not stage or edit anything outside this story (an untracked `src/package-lock.json` and `_bmad/render/` are not yours). The engine tests open real local ports; the firewall already allows the test host.

**Commands:**
- `dotnet test tests/ComParta.Engine.MonoTorrent.Tests -c Release` -- expected: all pass
- `dotnet test tests/ComParta.Core.Tests -c Release` -- expected: all pass, including the generated-file guard
- `.\build.ps1 Check` -- expected: exit 0 (UI tests, build, all C# tests)
