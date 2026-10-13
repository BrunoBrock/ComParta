# ComParta

> Share files with the people you love. No cloud. No accounts. No technical knowledge required.

## Vision

ComParta is an open-source, sandboxed, user-friendly torrent application that lets
non-technical people share files directly with family and friends, without relying
on any cloud service.

## The Problem

Sharing large files (family videos, photo albums, documents) today usually means
uploading them to a cloud provider. That means storage limits, subscriptions,
privacy trade-offs and dependence on a third party. Peer-to-peer technology
solves this, but existing torrent clients are built for power users and are
intimidating, confusing, and often unsafe for everyday people.

## Intentions

- **Cloud-free:** files travel directly between people. No central server stores your data; when two devices cannot reach each other, a helper you choose passes the file along while it is shared, and keeps nothing after.
- **Simple:** anyone who can send a photo by chat can use it. No jargon (no "seeds", "trackers", "magnet links" in the UI).
- **Safe by design:** downloads and shared content run in a sandbox, isolated from the rest of the computer.
- **Private by default:** you share only with the people you choose.
- **Open source:** transparent, auditable, and community-driven.

## First goal (2026-10-13)

A file shared from a PC can be downloaded from anywhere, with ComParta or with any other torrent
app, on Windows computers and Android phones, and nobody has to change a router or open a port.

## References

qBittorrent (works with every torrent app, downloads any link), Stremio (media first, play while
it arrives) and Google Drive (the look, and sharing by link). See the spec's References section.

## Target Users

Non-technical people, such as parents, grandparents, relatives and friends, who
want to exchange files with people they know and trust.

## Principles

1. Simplicity over features.
2. Security and sandboxing are not optional.
3. Privacy by default.
4. No account, no cloud dependency.
5. Open and transparent.

## Out of Scope (for now)

- Public discovery or searching for content from strangers
- Cloud storage

## Status

Most of the Windows app is built (share, send, receive, the Drive-like screen, speeds, Portuguese).
Next is reaching any torrent app from anywhere with no router setup (epic 16). The rest is planned
in [the roadmap](initiative-comparta/roadmap-comparta/roadmap-comparta.md) and drawn in
[roadmap.html](initiative-comparta/roadmap-comparta/roadmap.html): R1 family pilot on Windows,
R2 albums and Android, R3 people I share with.

## License

To be decided (open-source license, e.g. MIT, Apache-2.0 or AGPL-3.0).