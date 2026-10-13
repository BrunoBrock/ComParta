---
type: epic
title: "A shared file reaches any torrent app, anywhere, with no router setup"
parent: initiative-comparta
covers: [CAP-18, CAP-28]
after: []
assignee: ""
risk: high
---

# A shared file reaches any torrent app, anywhere, with no router setup

## Description

The maintainer's first goal (2026-10-13): put a file on the open internet from their PC and download it from anywhere, on a Windows PC or an Android phone, with ComParta or with any ordinary torrent app (qBittorrent, µTorrent, LibreTorrent, Flud), and nobody changes a router or a port.

The field test of 2026-10-13 showed why that does not work today. The ports of both ComParta and qBittorrent timed out when tested from outside the maintainer's home, so that PC cannot receive connections, and a phone on 4G or 5G never can (carrier-grade NAT). Two sides that can only connect out never meet. Public servers (CAP-29) help them find each other, not connect.

The fix is a **helper**: a small open-source program on an always-on computer that everyone can reach. The sharing ComParta connects out to it and hands it the file; the helper then serves the file to every app that asks, as an ordinary BitTorrent peer with its own public server. Because the helper speaks the normal protocol, any torrent app works, and so does ComParta itself on a PC or a phone. AD-23 holds the design.

## Outcome

A person shares a file on a PC behind any home router, copies its link, and someone on another network opens it in qBittorrent on Windows, or in a torrent app on an Android phone over mobile data, and gets the file. Nobody opened a port, and the person never saw a networking word.

## Requirements

- CAP-28: publish to any torrent app, on Windows and Android, with no router or port setup on either side.
- CAP-18: connect between homes; the helper is the last resort after port mapping, IPv6 and either side connecting out (AD-15).
- AD-23: the helper; AD-8 and AD-9 as amended 2026-10-13; AD-16: the copied invite is the engine's own link.
- AD-5: the person sees "helper", never a technical word.

## Done when

1. With port mapping off and incoming connections refused on the sharing PC, qBittorrent on a PC on another network downloads a shared file from its copied link (manual, recorded in `field-test.md` of epic 9).
2. The same link, opened in a stock Android torrent app on mobile data, downloads the file (manual).
3. An automated test in Windows CI runs a helper, a sharing ComParta that accepts no incoming connections and a receiver that accepts none either, and the file arrives through the helper alone, from the settings command through the router, use cases and real engine (CLAUDE.md testing rule).
4. Stopping a share removes it from the helper within a minute, and the helper never keeps more than its configured space.
5. The helper runs from one command on the free Oracle Cloud machine (Linux, Arm) and on a Windows PC, following a short guide.

## Boundaries

The helper program, ComParta's side of it, the link a person copies, and the guide to run a helper. Not a web page for downloading in a browser (later, see Notes), not watching while it arrives (CAP-30), not accounts or per-person access (epic 13).

## References

- parent — Planning/initiative-comparta/initiative-comparta.md
- roadmap — Planning/initiative-comparta/roadmap-comparta/roadmap-comparta.md, decisions P-17, D-3, D-5 and D-6
- spec — CAP-18, CAP-28, CAP-29, OQ-7
- architecture — AD-8, AD-9, AD-15, AD-16, AD-23
- epic 9 — the reachability facts, health and field test this builds on

## Notes

- Why not a relay that only ComParta understands: a stock app such as µTorrent cannot use it. A peer that is always reachable is the only thing every app already knows how to use.
- Why not hole punching: MonoTorrent has neither uTP nor hole punching, and carrier-grade NAT on phones defeats most of it anyway (architecture, Deferred).
- Fact (2026-10-13): MonoTorrent 3.0.2 ships a tracker server (`MonoTorrent.TrackerServer`, HTTP and UDP), proven in `PublicServersTests`, so the helper needs no second library.
- Assumption: a stock app finds the helper from the link's `tr=` (the helper's server) and `x.pe=` (the helper's address). Story 1 checks this with qBittorrent and two Android apps before anything else is built.
- Assumption: the helper keeps a full copy of each file while it is shared. Serving pieces on demand without a copy is harder and waits for a need.
- Risk: the helper sees what it passes along. For the family pilot it is run by the maintainer; the trade-off is stated in AD-23.
- Risk: a helper that accepts anything can be abused. Only ComParta installs holding the helper's key can add files, with a size limit per file and in total (story 2).
- Later, not planned: a browser download page on the helper (`https://helper/f/<id>`), so a phone needs no torrent app at all.
