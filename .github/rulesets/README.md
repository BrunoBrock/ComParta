# Branch rulesets

`protect-main.json` protects `main`:

- Changes reach `main` only through a pull request, and every review thread must be resolved.
- The pull request must be up to date with `main`, and these checks must pass: `build-and-test`
  (CI), `Coverage` (PR checks), the three `CodeQL` scans, `Dependency review`,
  `Vulnerable packages` and `Secret scan` (Security).
- `main` cannot be deleted or force-pushed.
- No approval is required, so a single maintainer can still merge. Raise
  `required_approving_review_count` when more people review.

To apply it: **Settings → Rules → Rulesets → New ruleset → Import a ruleset**, choose this
file, then **Create**. GitHub does not apply files in this folder by itself; after changing the
file, import it again or edit the ruleset in Settings to match.
