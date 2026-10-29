---
title: "Release workflow from a tag"
ticket: 4
status: done
---

# Release workflow from a tag

Built before the Store listing (story 3): everything but the Store submission runs now, and the
Store step is in the workflow, switched off until its secrets exist.

## What this story adds

- **`.github/workflows/app-release.yml`**, on tags `v*` (and by hand, or on a pull request that
  changes it, where it only keeps the files as downloads). On a Windows runner: the release notes
  script's tests, `./build.ps1 Check`, the MSIX (`Package`), installing and starting it with
  `check-package.ps1 -Version`, the zip (`Zip`) checked by `check-zip.ps1`, then `SHA256SUMS.txt`
  and the notes. A second job, the only one with `contents: write`, creates a **draft** release
  (a pre-release for tags such as `v1.2.3-test`). A third sends the MSIX to the Store with the
  Store's own command-line tool (`msstore publish --noCommit`), skipped while any of the five
  `STORE_*` secrets is missing; `RUNNING.md` names them.
- **The version** comes from the tag (`v1.2.3-test` is `1.2.3` inside) and is stamped into the
  MSIX and into the zip's app (`ApplicationDisplayVersion`), which Settings, About shows.
- **`./build.ps1 Zip`**: the self-contained Publish folder (with .NET and the Windows App SDK
  inside) plus `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt`, as `publish\ComParta-<version>-win-x64.zip`.
- **`.github/scripts/release_notes.py`**: one line per pull request merged since the previous `v*`
  tag, from squash subjects (`Title (#12)`) and merge commits (`Merge pull request #12`, title in
  the body); never names or emails.
- **Story 6, the documented half**: README, "Installing without the Store", in plain words with the
  SmartScreen warning and how to get past it. Signing (SignPath) waits for the repository to be public.

## Verify

- Automated, in the Windows CI job (`ci.yml`, package job) and in the release workflow:
  `check-zip.ps1` unpacks the zip, checks it carries the license, the notices and its own .NET,
  starts `ComParta.exe` with the runner's .NET pointed away, and drives it through its own screen
  over the web view's debugging port (`drive-app.mjs`): About's version is the tag's, a file picked
  in the real Windows file dialog is shared and the screen hears `shares.changed`, then `app.quit`
  closes it. `check-package.ps1 -Version` checks the MSIX version. `test_release_notes.py` covers
  "since the last tag", a first release, test tags and "no emails".
- Needs a person: pushing the first real tag and publishing the draft; the Store secrets after
  story 3; downloading the zip on a real PC shows the SmartScreen warning the README describes.
