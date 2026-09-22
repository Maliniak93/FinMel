---
name: build
description: Build an approved spec end-to-end - branch, failing tests, implementation, verification, adversarial review - and leave it staged for your own commit and PR.
argument-hint: "<spec slug or path> [--tier 1|2] [--skip tests,review] [+Nk]"
disable-model-invocation: true
---

Run the `build-feature` workflow for the spec named in `$ARGUMENTS`. You orchestrate nothing yourself:
the workflow script owns the flow, the agents do the work.

## Steps

1. Resolve the spec. `$0` is a slug or a path: `<slug>` → `skarbiec-plan/specs/<slug>.md`; a path is
   used as given. Missing file → list `skarbiec-plan/specs/*.md` and stop.
2. Read the frontmatter (`title`, `status`, `tier`, `branch`, `skip`).
3. **Stop unless `status: approved`.** Say which status it has and what to do:
   `draft` → finish it with `/design`, `building` → a run is already in flight or a previous run
   stopped mid-way (re-running is safe, say so), `done` → it already shipped; point at the `## Result`
   PR link and suggest a new spec instead.
4. Tier: from the frontmatter, overridden by `--tier 1|2` when given.
5. Skipped phases: the spec's `skip` list, overridden by `--skip a,b` when given. Only `tests` and
   `review` can be skipped — `verify` is the definition of green and always runs.
   - `skip: [tests]` belongs to a spec that adds no behaviour (deletion, config, docs, a pure move).
     Without it a spec whose test-writer produces nothing stops the run, which is the correct answer
     when the criteria were simply untestable as written.
   - `--skip review` is a deliberate one-off for a trivial run; it never belongs in a spec file.
6. A `+Nk` argument is the user's token budget directive for the turn — leave it in place and do not
   put it into `args`; the workflow reads it through `budget`.
7. Set the frontmatter to `status: building` by editing the spec file directly. `skarbiec-plan/specs/`
   is gitignored, so this edit stays local - it feeds `plan-status.mjs`, not the PR.
8. Call the `Workflow` tool with
   `{ name: 'build-feature', args: { spec: "<resolved path>", tier: <tier>, maxRounds: 2, skip: [<skipped phases>] } }`
   and wait for it.

## Phases the workflow runs

`Branch` (ops cuts `feat/<slug>` from master **before** anything is written) → `Tests` → `Implement`
→ `Verify` ⇄ `Implement` → `Review` (ops stages the tree, reviewer diffs `git diff --cached`) ⇄
`Implement`+`Verify` → `Stage` (spec → `done`, `git add -A`).

**The run never commits, pushes or opens a PR.** It ends with the whole change staged on its branch;
committing, pushing and opening the PR are the user's, by hand.

## Report (at most 10 lines, after the workflow returns)

- `staged` → branch, staged file count, tests written (count + names, or "none — spec skipped
  tests"), files touched (count), fix rounds, then the minor review findings (one line each:
  `file:line — claim`; say "none" when the list is empty), then the three commands to finish:
  `git commit -m "<spec title>"` · `git push -u origin <branch>` · `gh pr create --base master`.
  Do not run them and do not offer to.
- `blocked` → the `stage`, then the `failures` or blocking `findings` verbatim but trimmed, then the
  choice: fix the spec and re-run `/build <slug>` (the work sits in the working tree on `feat/<slug>`,
  so nothing is lost), or fix the named problem by hand first. Leave `status: building` in the spec -
  it is accurate.
- `blocked` at `stage: branch` means the working tree held changes that are not this spec. Name them
  and stop - do not offer to sweep them into the branch.
- The workflow already left the spec at `status: done` on a successful run. Do not edit the spec
  yourself afterwards, and never commit, push, open or merge anything.
