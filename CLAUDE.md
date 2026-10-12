# Working in this repository

## Before starting any new work

1. Run `git fetch origin` and list every remote branch: `git branch -r`.
2. For each branch that is not `main`, check what it has that `main` lacks:
   `git log --oneline origin/main..origin/<branch>`.
3. When any branch has commits `main` does not (Bruno's own branches, such as
   `mvp-share-and-send`, above all), stop and tell Bruno before building on top of `main`.
   Do not start work that may duplicate or conflict with it until he says how to proceed.
4. Build new work on a branch from the latest `main`, never on top of an unmerged branch,
   unless Bruno asks for that.

This rule exists because work was once built twice: a branch with Bruno's own commits
(reachability facts, the `build.ps1` menu) was missed while epics were stacked on `main`.

## Standing rules

- Never merge a pull request; Bruno merges. Open new work as a draft pull request.
- No technical words reach the person (AD-5): see the guard tests in `tests/ComParta.Core.Tests`.
- Things that need a real Windows PC or real people are listed in the pull request for Bruno.
