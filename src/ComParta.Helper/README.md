# ComParta helper

A small program for a computer everyone can reach (a cloud machine, or a PC with forwarded ports).
A ComParta that cannot be reached, behind a home router or on mobile data, hands the helper the
files it shares; the helper fetches them over the connection ComParta opens to it and serves them to
any torrent app, so nobody changes a router. The design is AD-23 in the architecture.

It runs three things:

| Port | Default | What |
|---|---|---|
| `PeerPort` | 6881 (TCP) | Where torrent apps download the files. |
| `TrackerPort` | 6969 (TCP and UDP) | The helper's public server: `http://<address>:6969/announce` and `udp://<address>:6969`. It lists only the files the helper holds. |
| `ControlPort` | 6970 (TCP) | Where ComParta adds and removes files with the key. HTTPS when a certificate is set. |

## Run it

```
dotnet run --project src/ComParta.Helper -- --Helper:PublicAddress=203.0.113.7 --Helper:Key=<at least 16 characters>
```

Settings come from `helper.json` beside the program, then environment variables (`Helper__Key`),
then the command line (`--Helper:Key=...`). It refuses to start, and says why, while `PublicAddress`
or `Key` is missing.

| Setting | Meaning |
|---|---|
| `PublicAddress` | The address the internet reaches this computer at. Apps are sent here. |
| `Key` | The secret ComParta sends to add or remove a file. Keep it out of anything public. |
| `MaxFileBytes`, `MaxTotalBytes` | The biggest file, and the space for all files. The oldest file is dropped to make room. |
| `DataFolder` | Where the copies live, relative to the program's folder. Files held survive a restart. |
| `CertificatePath`, `CertificateKeyPath` | PEM files for HTTPS on the control port. Without them the key travels in plain text, so use that only for testing. |

## Control requests

Every request carries `Authorization: Bearer <key>`.

- `PUT /files` with the share's description (the `.torrent` bytes ComParta keeps) as the body:
  `201` and `{"id": "<content id>"}` when added, `200` when already held, `400` when it is not one
  plain file, `413` when it is too big, `401` without the key.
- `DELETE /files/<id>`: `204` when removed, together with the helper's copy; `404` when not held.
- `GET /files`: what the helper holds, with each file's progress.

## Rules it keeps

- Only files a ComParta holding the key added are listed on its public server, so it is not an open
  server for anything else.
- One plain file per description; a name that is a path is refused.
- An app may name an address for itself only if the request came from that address, so nobody can
  make the server send apps to a third party.

The full guide to running a helper on a free cloud machine is a later story (docs/helper.md).
