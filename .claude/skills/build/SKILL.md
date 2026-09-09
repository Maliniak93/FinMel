---
name: build
description: Build an approved spec end-to-end - failing tests, implementation, verification, adversarial review, commit and PR - through the build-feature workflow.
argument-hint: "<spec slug or path> [--tier 1|2] [+Nk]"
disable-model-invocation: true
---

Run the `build-feature` workflow for the spec named in `$ARGUMENTS`. You orchestrate nothing yourself:
the workflow script owns the flow, the agents do the work.

## Steps

1. Resolve the spec. `$0` is a slug or a path: `<slug>` → `skarbiec-plan/specs/<slug>.md`; a path is
   used as given. Missing file → list `skarbiec-plan/specs/*.md` and stop.
2. Read the frontmatter (`title`, `status`, `tier`, `branch`).
3. **Stop unless `status: approved`.** Say which status it has and what to do:
   `draft` → finish it with `/design`, `building` → a run is already in flight or a previous run
   stopped mid-way (re-running is safe, say so), `done` → it already shipped; point at the `## Result`
   PR link and suggest a new spec instead.
4. Tier: from the frontmatter, overridden by `--tier 1|2` when given.
5. A `+Nk` argument is the user's token budget directive for the turn — leave it in place and do not
   put it into `args`; the workflow reads it through `budget`.
6. Set the frontmatter to `status: building` by editing the spec file directly; the spec is tracked,
   so this edit ships inside the feature's PR.
7. Call the `Workflow` tool with
   `{ name: 'build-feature', args: { spec: "<resolved path>", tier: <tier>, maxRounds: 2 } }`
   and wait for it.

## Report (at most 10 lines, after the workflow returns)

- `ready-for-merge` → status, PR URL, branch, tests written (count + names), files touched (count),
  fix rounds, and one line: **merging is yours** - review the PR and merge it when CI is green.
- `blocked` → the `stage`, then the `failures` or blocking `findings` verbatim but trimmed, then the
  choice: fix the spec and re-run `/build <slug>` (the workflow resumes from the last green stage), or
  fix the named problem by hand first. Leave `status: building` in the spec - it is accurate.
- The workflow already left the spec at `status: done` with the PR link on a successful ship. Do not
  edit the spec yourself after a run, and never merge the PR.
