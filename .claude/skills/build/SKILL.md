---
name: build
description: Build a spec issue from the FinMel GitHub project end-to-end - branch, failing tests, implementation, verification, adversarial review, commit, push and PR - and leave the merge to you.
argument-hint: "#<issue> [--tier 1|2] [--skip tests,review] [+Nk]"
disable-model-invocation: true
model: haiku
effort: low
---

You relay; scripts and the `build-feature` workflow do all the work. Decide nothing yourself.

1. Run `node scripts/gh-project.mjs prepare <issue> [--tier …] [--skip …]` with the issue number and any
   `--tier`/`--skip` from `$ARGUMENTS`. Never pass a `+Nk` token to it — that is the turn's budget
   directive and the workflow reads it itself. No issue number in `$ARGUMENTS` → run
   `node scripts/plan-status.mjs` instead, print its **Next up** line and stop.
2. `"ok": false` → print `reason` and `next`, and stop.
3. `"ok": true` → call the `Workflow` tool with `{ name: 'build-feature', args: <workflowArgs, verbatim> }`
   and wait. (`prepare` already moved the card to In progress; `resumed: true` means a previous run
   left its branch — say so in one line.) The workflow posts its own run report on the issue.

## Report (at most 10 lines)

- `shipped` → the `prUrl` (the merge is the user's), the issue `url`, branch and `commit`, tests
  (`tests`, or "none — skip-tests"), `filesTouched` count, `rounds`, each of `minorFindings` as
  `file:line — claim` ("none" when empty).
- `blocked` → `stage`, then `reason`/`failures`/`findings` trimmed to one line each, then: fix the
  spec (`/design` amends the issue) or the named problem by hand, and re-run `/build #<n>` — the work
  stays on its branch. At `stage: branch` the tree held changes that are not this spec: name them and
  stop; never offer to sweep them into the branch. At `stage: ship` the change is verified and
  reviewed but not shipped: print `reason` and the `nextSteps` commands verbatim for the user to finish by hand.

Never commit, push, open or merge anything yourself — the workflow ships, the user merges — and never touch the issue.
