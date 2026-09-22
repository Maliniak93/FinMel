---
name: build
description: Build an approved spec end-to-end - branch, failing tests, implementation, verification, adversarial review, PR - through the build-feature workflow.
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
7. Set the frontmatter to `status: building` by editing the spec file directly; the spec is tracked,
   so this edit ships inside the feature's PR.
8. Call the `Workflow` tool with
   `{ name: 'build-feature', args: { spec: "<resolved path>", tier: <tier>, maxRounds: 2, skip: [<skipped phases>] } }`
   and wait for it.

## Phases the workflow runs

`Branch` (ops cuts `feat/<slug>` from master **before** anything is written) → `Tests` → `Implement`
→ `Verify` ⇄ `Implement` → `Review` (ops commits the tree, reviewer diffs `master...HEAD`) ⇄
`Implement`+`Verify` → `Ship` (push, PR, spec → `done`, minor findings posted on the PR).

## Report (at most 10 lines, after the workflow returns)

- `ready-for-merge` → status, PR URL, branch, tests written (count + names, or "none — spec skipped
  tests"), files touched (count), fix rounds, minor findings posted, and one line: **merging is
  yours** - review the PR and merge it when CI is green.
- `blocked` → the `stage`, then the `failures` or blocking `findings` verbatim but trimmed, then the
  choice: fix the spec and re-run `/build <slug>` (the work already sits committed on `feat/<slug>`,
  so nothing is lost), or fix the named problem by hand first. Leave `status: building` in the spec -
  it is accurate.
- `blocked` at `stage: branch` means the working tree held changes that are not this spec. Name them
  and stop - do not offer to sweep them into the branch.
- The workflow already left the spec at `status: done` with the PR link on a successful ship. Do not
  edit the spec yourself after a run, and never merge the PR.
