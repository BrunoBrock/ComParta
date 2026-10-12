---
type: epic
title: "Two people in different homes connect without knowing about networks"
parent: initiative-comparta
covers: [CAP-18]
after: []
assignee: ""
risk: high
---

# Two people in different homes connect without knowing about networks

## Description

Today two copies of ComParta connect on the same network, and between homes only when at least one computer is reachable from the internet. MonoTorrent has no hole punching, and AD-9 forbids a hosted relay. This epic measures how often real homes connect, then raises that rate with everything that needs no server (router port mapping, IPv6, either side connecting out), and tells the person in plain words when their computer cannot be reached and what to try.

## Outcome

The product success signal: two people in different homes move a large family video directly between their devices. When it cannot work, both people understand why and what to try, instead of waiting forever on "Looking for the other person's computer".

## Requirements

- CAP-18: connect between homes, with plain-language health and help.
- AD-15: reachability improved and explained, never faked with a server; AD-8: network settings only in `SharingPolicy`.
- UX design: the connection indicator in the rail, the "waiting" state and the "Stuck transfer" flow.

## Done when

1. A field test matrix (story 1) is recorded, and repeated after stories 4 to 7 with a higher success rate.
2. A transfer works between two homes where only the receiver is reachable, and where only the sender is.
3. On a connection with IPv6, two homes connect over IPv6 without any router setting.
4. The rail shows this computer's connection health, and the help page explains it with no networking word.
5. Nothing in the epic contacts a service to test reachability (AD-9, AD-15).

## Boundaries

Reachability between two devices running ComParta. Not a relay unless decision D-3 allows it (story 9), not another engine unless the field test calls for it (story 10), not Android networking specifics (epic 7 reuses this work).

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- roadmap — Planning/initiative-comparta/roadmap-comparta/roadmap-comparta.md, decisions P-7 and D-3
- spec — CAP-18, Open Questions OQ-1 and OQ-6
- architecture — AD-2, AD-8, AD-9, AD-15, Deferred (relay, another engine)
- UX design — "Stuck transfer", "Layout" (connection indicator)

## Notes

- Sequencing: story 1 comes first and decides how much of the rest matters. Stories 2 to 7 are independent of each other after story 2's facts exist.
- Assumption: Mono.Nat (shipped with MonoTorrent) handles UPnP and NAT-PMP; PCP is a bonus if it does.
- Unknown: whether MonoTorrent 3.0.2 listens and announces on IPv6 (lookup network over IPv6, BEP 32); story 5 finds out.
- Risk: in Brazil, mobile networks and some fibre providers use carrier-grade NAT for IPv4; IPv6 is the main hope there.
- Waits on epic-send-to-a-person because: it provides the receive path being measured.
- Built 2026-10-11: stories 2, 3, 5, 6 and 7, and the code half of story 4.
  - Health: core decides `blocked` (not accepting connections, or a Windows Firewall rule blocks the app), `good` (the router opened a port, or someone connected in the last hour), else `limited`. An IPv6 address alone counts as `limited`, because many home routers refuse incoming IPv6.
  - IPv6 (story 5): MonoTorrent 3.0.2 listens on IPv6 as well as IPv4, and invites now carry global IPv6 addresses. Its lookup network takes one endpoint, so it stays on IPv4. The IPv6 transfer test runs only where the machine has IPv6, which the Windows CI runner does.
  - Port mapping (story 6): `PortMapper` replaces MonoTorrent's own forwarder, asks for UPnP and NAT-PMP mappings for 2 hours, renews every hour, removes them on quit, and puts the router's public address first in the invite. Mono.Nat 3.0.0 has no PCP.
  - Either side reachable (story 7): a test proves a sender that accepts no connections delivers by connecting out to the receiver.
  - Firewall (story 4): the unpackaged build reads the app's rules from the registry and offers "Open Windows settings" when only blocking rules exist; the MSIX manifest declares inbound rules. The manual check on a clean PC waits for the MSIX build (epic 5) and for a person.
  - Still open: stories 1 and 8 (field tests, need people in two homes).
