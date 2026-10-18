---
name: build
description: Build a spec issue from the FinMel GitHub project end-to-end - branch, failing tests, implementation, verification, adversarial review, then commit, push and PR via ship.mjs - and leave the merge to you.
argument-hint: "#<issue> [--tier 1|2] [--skip tests,review] [--fresh] [+Nk]"
disable-model-invocation: true
model: haiku
effort: low
---

You relay; scripts and the `build-feature` workflow do all the work. Decide nothing yourself. Run in a fresh session or `claude --model haiku "/build #<n>"`.

1. No issue number in `$ARGUMENTS` → run `node scripts/plan-status.mjs` instead, print its **Queue** list
   and stop. Otherwise run `node scripts/preflight.mjs` and read its last stdout line, `PREFLIGHT_RESULT: <json>`.
   `"ok": false` → print each failed check as `name — detail → fix` and stop (the card is not moved). Checks with
   `"status": "fixed"` → one line saying what was started, stopped or installed, then continue.
2. Run `node scripts/gh-project.mjs prepare <issue> [--tier …] [--skip …] [--fresh]` with the issue number and any
   `--tier`/`--skip`/`--fresh` from `$ARGUMENTS`. Never pass a `+Nk` token to it — that is the turn's budget
   directive and the workflow reads it itself. It also cuts or switches the issue branch, and the
   `epic` info in `workflowArgs` (the last part of an epic also gets the epic and sibling bodies written locally).
   `"e2e": true` in `workflowArgs` marks an epic's e2e part: pass it through verbatim, the workflow then runs the
   e2e agent and the review instead of the test-writer and implementer.
3. `"ok": false` → print `reason` and `next`, and stop. `stage: "branch"` → the tree held changes that are
   not this spec: name the `files` and stop; never offer to sweep them into the branch.
4. `"ok": true` → call the `Workflow` tool with `{ ...workflow, args: workflowArgs }`, both verbatim from
   prepare's output (`workflow` is `{ name }` or, on a resume, `{ scriptPath }`). `replay` set → say in one line
   `replay: <steps> → live from <liveFrom>` (`→ ship` when `liveFrom` is null). Then wait. (`prepare` already moved the card to In progress; `resumed: true` means a previous run
   left its branch — say so in one line.)
5. As soon as the workflow completes, run `node scripts/ship.mjs <issue> --from-run` (the workflow's `nextCommand`;
   it reads the branch, title and run report from the workflow record, so a truncated result loses nothing) as ONE
   Bash call with `timeout: 3600000` (a ship rebuilds six compose images; the 2-min default cuts it off after the
   PR exists) — for `ready` and `blocked` alike — and read its last stdout line, `SHIP_RESULT: <json>`. Run nothing
   between the workflow and this call: no build, test, grep or file read, and ignore IDE/LSP diagnostics — the
   workflow's verify and review are the only verdict.

## GitHub limit

`prepare` or `ship.mjs` that exits 4 hit the GitHub API limit (stderr: `Limit GitHuba (<kind>) wyczerpany, reset o HH:MM` and a `GITHUB_LIMIT` line). Stop at once, no retry, no workaround: the final message is that reset time and "re-run `/build #<n>` after the reset" (after a `ship.mjs` exit 4 the work is verified and reviewed and stays on its branch; the re-run ships it).

## Report (at most 11 lines)

- `replay: <steps> → live from <liveFrom>` first, when prepare returned `replay`.
- `ready` and SHIP_RESULT `ok` → the `prUrl` (the merge is the user's), the issue `url`, `branch` and
  `commit`, tests (`tests`, or "none — skip-tests"), `filesTouched` count, `rounds`, each of
  `minorFindings` as `file:line — claim` ("none" when empty), and `images` (the rebuilt compose
  image `revision` from `scripts/compose.mjs build`, or its `error`; a failed image build does not make the ship fail),
  and the issues whose `## Test manualny` the run updated (`manualTest[].issue`, or "none"). An e2e part
  adds the line `e2e: <passed>/<total> green` from SHIP_RESULT `e2e`, plus its `flaky` tests when any.
- `ready` and SHIP_RESULT not `ok` → `failedCommand` and `error`, then the remaining steps to finish by
  hand (commit, push, `gh pr create --base master`); the change is verified and reviewed.
- `blocked` → `stage`, then `reason`/`failures`/`findings` trimmed to one line each, then: fix the
  spec (`/design` amends the issue) or the named problem by hand, and re-run `/build #<n>` — the work
  stays on its branch.
- `blocked` at `e2e` → the `class` (`app` or `environment`), the failed test, its `Krok <n>` step, the trace
  path and the patch path from the report. `ship.mjs` has saved the diff and cleaned the tree back to master:
  an `app` failure is fixed with `/fix e2e:#<n>` (diagnoses it and publishes the fix ahead of the e2e part), an `environment` one by fixing the machine; then re-run `/build #<n>`
  (the branch is recut from master and the tests are written again).

Never commit, push, open or merge anything yourself — `ship.mjs` ships, the user merges — and never touch the issue.
