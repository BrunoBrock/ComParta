# Working in this repository

## Before starting any new work

1. Run `git fetch origin` and list every remote branch: `git branch -r`.
2. For each branch that is not `main`, check what it has that `main` lacks:
   `git log --oneline origin/main..origin/<branch>`.
3. When any branch has commits `main` does not (the maintainer's own branches, such as
   `mvp-share-and-send`, above all), stop and tell the maintainer before building on top of `main`.
   Do not start work that may duplicate or conflict with it until they say how to proceed.
4. Build new work on a branch from the latest `main`, never on top of an unmerged branch,
   unless the maintainer asks for that.

This rule exists because work was once built twice: a branch with the maintainer's own commits
(reachability facts, the `build.ps1` menu) was missed while epics were stacked on `main`.

## Testing every feature end to end

Core tests and UI tests with the fake bridge are not enough: buttons have shipped that did nothing
in the real app (the speed bar's Stop, "Stop sharing"). Every feature also needs a test that drives
it through the real wiring: the bridge command as JSON, the router, the use cases and the real
engine and storage, and back to the event the screen shows. These tests run in the Windows CI job.
The fake bridge must behave like the real router; when they differ, the real one wins and the
fake is fixed. Whatever still needs a person at a Windows PC is listed in the pull request.

## Standing rules

- Never merge a pull request; the maintainer merges. Open new work as a draft pull request.
- No technical words reach the person (AD-5): see the guard tests in `tests/ComParta.Core.Tests`.
- Things that need a real Windows PC or real people are listed in the pull request for the maintainer.
