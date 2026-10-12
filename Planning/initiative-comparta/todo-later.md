# To review later: edge cases and small items

Set aside by the maintainer on 2026-10-12 so the work on sending over the internet stays small. None of these blocks a story. Nothing here is planned; review the list before a public release, or when the check between two homes (epic-send-to-a-person, entry 5) shows one of them matters.

## Connection edge cases

- **A router with no inbound path** (carrier-grade NAT, strict routers). The app only reports "not reachable"; MonoTorrent has no uTP hole punching, so one side must accept incoming connections. Source: architecture, Deferred.
- **Help for a router that will not open the port by itself.** Entry 2.4 shows a short message only. Manual steps (forward the port, read the router's address) conflict with the spec's "no step needing networking knowledge". Source: spec OQ-1.
- **Phones on mobile data.** They usually sit behind carrier-grade NAT, so they can download from a reachable PC but other devices cannot connect to them. Source: epic-android Notes.
- **IPv6.** The engine listens on IPv4 only and invite address hints are IPv4 only (checked in the engine adapter). An IPv6 path could help where IPv4 is shared between customers.
- **"Reachable" can be wrong.** It means the router opened the port and reports a public address. Windows Firewall can still block the app (the first-run firewall prompt, if cancelled), and only a connection from outside proves the way in. No third-party "is my port open" service may be used (AD-9).
- **The saved listening port is busy at start-up.** A new port is chosen and saved in `port.txt`, so the address hints in invites given out earlier go stale and a manual router forward stops working. Lookup through the public network still works, and a copied invite always uses the current port.
- **A laptop changes network.** The invite is rebuilt each time it is copied, but check that the router mapping and the reachability state follow the change.

## Safety hardening (parked in the architecture's Deferred table)

- **Upload limit and connection cap.** None exists today; a leaked invite lets strangers use the sender's upload bandwidth.
- **Invites that expire, or can be revoked for one person.** Today removing the share is the only way to cut access.
- **An option to turn the public lookup network off.** Then only invites that carry an address work, and the fingerprint is not announced.
- **A stronger privacy model.** Per-person keys, and no fingerprint on the lookup network. Revisit before the app is offered outside the maintainer's circle.
- **The encryption is obfuscation.** RC4 header or full hides traffic from casual inspection only; real confidentiality needs a different design.
- **Done when 4 is stricter than AD-8.** "Someone without the invite cannot obtain the file" does not hold for someone who learns the fingerprint from the lookup network. Reword it when the privacy model is revisited.
- **A pasted invite can name any IPv4 address.** The app tries up to six addresses from it, including addresses on the receiver's own network. Low risk; consider limiting private ranges.
- **MonoTorrent 3.0.2 reads data from the internet on an open port.** Its releases are infrequent. Keep it updated, and remember the engine can be swapped behind the port (AD-2).
