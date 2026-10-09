---
name: build
description: Build a spec issue from the FinMel GitHub project end-to-end - branch, failing tests, implementation, verification, adversarial review, then commit, push and PR via ship.mjs - and leave the merge to you.
argument-hint: "#<issue> [--tier 1|2] [--skip tests,review] [+Nk]"
disable-model-invocation: true
model: haiku
effort: low
---

You relay; scripts and the `build-feature` workflow do all the work. Decide nothing yourself. Run in a fresh session or `claude --model haiku "/build #<n>"`.

1. No issue number in `$ARGUMENTS` → run `node scripts/plan-status.mjs` instead, print its **Next up** line
   and stop. Otherwise run `node scripts/preflight.mjs` and read its last stdout line, `PREFLIGHT_RESULT: <json>`.
   `"ok": false` → print each failed check as `name — detail → fix` and stop (the card is not moved). Checks with
   `"status": "fixed"` → one line saying what was started, stopped or installed, then continue.
2. Run `node scripts/gh-project.mjs prepare <issue> [--tier …] [--skip …]` with the issue number and any
   `--tier`/`--skip` from `$ARGUMENTS`. Never pass a `+Nk` token to it — that is the turn's budget
   directive and the workflow reads it itself. It also cuts or switches the issue branch.
3. `"ok": false` → print `reason` and `next`, and stop. `stage: "branch"` → the tree held changes that are
   not this spec: name the `files` and stop; never offer to sweep them into the branch.
4. `"ok": true` → call the `Workflow` tool with `{ name: 'build-feature', args: <workflowArgs, verbatim> }`
   and wait. (`prepare` already moved the card to In progress; `resumed: true` means a previous run
   left its branch — say so in one line.)
5. Run the returned `nextCommand` verbatim as ONE Bash call with `timeout: 3600000` (a ship rebuilds six
   compose images; the 2-min default cuts it off after the PR exists) — for `ready` and `blocked` alike — and
   read its last stdout line, `SHIP_RESULT: <json>`.

## Report (at most 11 lines)

- `ready` and SHIP_RESULT `ok` → the `prUrl` (the merge is the user's), the issue `url`, `branch` and
  `commit`, tests (`tests`, or "none — skip-tests"), `filesTouched` count, `rounds`, each of
  `minorFindings` as `file:line — claim` ("none" when empty), and `images` (the rebuilt compose
  image `revision` from `scripts/compose.mjs build`, or its `error`; a failed image build does not make the ship fail).
- `ready` and SHIP_RESULT not `ok` → `failedCommand` and `error`, then the remaining steps to finish by
  hand (commit, push, `gh pr create --base master`); the change is verified and reviewed.
- `blocked` → `stage`, then `reason`/`failures`/`findings` trimmed to one line each, then: fix the
  spec (`/design` amends the issue) or the named problem by hand, and re-run `/build #<n>` — the work
  stays on its branch.

Never commit, push, open or merge anything yourself — `ship.mjs` ships, the user merges — and never touch the issue.
