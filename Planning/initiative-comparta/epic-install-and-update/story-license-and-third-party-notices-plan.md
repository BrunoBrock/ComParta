---
title: "License and third-party notices"
ticket: 1
status: done
---

# License and third-party notices

D-1 was answered with the Zero-Clause BSD license (PR #29); `LICENSE` stays as it is.

## What this story adds

- **`tools/ComParta.Notices`**, a small .NET console. It reads `obj/project.assets.json` of every
  project that ships (the app for Windows, the helper, core, contracts, the engine adapter, shell and
  storage) for every package restore resolved, direct or not, with the license, copyright and project
  each package's .nuspec declares, and the production packages of `src/ui/package-lock.json` with
  what each package's `package.json` and license file say.
- **A license policy** (`LicensePolicy.cs`): an allow-list of permissive licenses that can ship under
  0BSD (MIT, BSD, Apache-2.0, ISC, Zlib, MS-PL, CC0 and a few more), read as SPDX expressions (OR,
  AND, WITH, parentheses). A package with no license, an unknown one or one off the list (GPL, AGPL,
  LGPL, SSPL...) fails, naming the package. A short list of reviewed exceptions covers packages whose
  license is a file (MonoTorrent and the parts it brings are MIT files; the Windows App SDK, WebView2
  and Windows SDK build tools have Microsoft's own terms), each with the reason.
- **`THIRD-PARTY-NOTICES.txt`** at the repository root: generated, committed, deterministic (sorted,
  LF, no dates). Each part is a block of name and version, license, copyright and project, followed
  by the standard text of each license used, once. It ships with the app as a `MauiAsset` (the
  Windows output folder, the MSIX and the Android assets).
- **`build.ps1`**: Check, Publish and Package regenerate the notices and fail when a package cannot
  ship or the committed file is out of date (not left out by `-SkipTests`). The fresh file is left in
  `publish\`, and CI uploads it as the `third-party-notices` download when Check fails.
- **Settings, About**: "Licenses of the parts ComParta uses" opens a dialog with the notices, read
  through the read-only bridge command `about.notices` from the file the app ships.
- **`CONTRIBUTING.md`**: no license header in new files; new packages must pass the license step, how
  to regenerate the file and how to add a reviewed exception.

## Verify

- Automated: `tests/ComParta.Notices.Tests` (MIT and Apache pass; no license, GPL, AGPL, LGPL, SSPL
  and unknown fail naming the package; OR and AND expressions; reviewed exceptions; the same parts
  give the same file; NuGet and npm reading; a stale committed file fails the check); the wiring test
  that asks the real router for `about.notices`; UI tests for the button and dialog in en and pt-BR
  with axe; `check-package.ps1` asserts the file is in the installed MSIX.
- Needs a person at a Windows PC: open Settings, About, Licenses in the installed app and read that it
  shows and scrolls at normal and 200% text size.
- Follow-ups: the Android build's own packages are not listed yet (epic 7), and the helper's release
  zip does not carry the notices yet.
