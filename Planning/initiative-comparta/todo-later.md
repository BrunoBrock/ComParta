# To review later: edge cases and small items

Set aside by the maintainer on 2026-10-12 so the work on sending over the internet stays small. None of these blocks a story. Review the list before a public release, or when the field test (epic 9, stories 1 and 8) shows one of them matters.

## Not planned anywhere yet

- **The saved listening port is busy at start-up.** A new port is chosen and saved in `port.txt`, so the address hints in invites given out earlier go stale and a manual router forward stops working. Lookup through the public network still works, and a copied invite always uses the current port. Related: epic 9, story 6.
- **A laptop changes network.** The invite is rebuilt each time it is copied; check that the router mapping and the connection health follow the change (epic 9, stories 2 and 6).
- **Upload limit and connection cap.** None exists today; a leaked invite lets strangers use the sender's upload bandwidth. Architecture, Deferred.
- **Invites that expire, or can be revoked for one person.** Today removing the share is the only way to cut access. Architecture, Deferred.
- **An option to turn the public lookup network off.** Then only invites that carry an address work, and the fingerprint is not announced. Architecture, Deferred.
- **The encryption is obfuscation.** RC4 header or full hides traffic from casual inspection only; real confidentiality needs a different design. Epic 13's spike may cover it.
- **Done when 4 of epic 2 is stricter than AD-8.** "Someone without the invite cannot obtain the file" does not hold for someone who learns the fingerprint from the lookup network. Reword it when the privacy model is settled (epic 13).
- **A pasted invite can name any IPv4 address.** The app tries up to six addresses from it, including addresses on the receiver's own network. Low risk; consider limiting private ranges when `InviteCodec` (AD-16) lands.
- **MonoTorrent 3.0.2 reads data from the internet on an open port.** Its releases are infrequent. Keep it updated, and remember the engine can be swapped behind the port (AD-2).

## Already planned elsewhere (on 2026-10-10, so not repeated here)

- A router with no inbound path, a relay, another engine: epic 9, stories 7, 9 and 10, and the architecture's Deferred table.
- IPv6, port mapping that lasts, the Windows Firewall rule, the connection indicator and its help page: epic 9, stories 3 to 6.
- Phones on mobile data, which sit behind carrier-grade NAT: epic 7 Notes and epic 9's IPv6 work.
- The stronger privacy model (per-person keys, no fingerprint on the lookup network): epic 13.
