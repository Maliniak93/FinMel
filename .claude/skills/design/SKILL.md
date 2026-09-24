---
name: design
description: Interview the user about a feature, a change to existing behaviour, or a refactor, then - after approval - publish a build-ready spec as an issue on the FinMel GitHub project. Writes no code.
argument-hint: "<feature, change or refactor description>"
disable-model-invocation: true
model: claude-opus-5-5
effort: xhigh
---

Turn `$ARGUMENTS` into a spec that `/build` can execute unattended. The spec is drafted locally,
discussed until approved, and only then published as a GitHub issue with Status **Todo** on the
FinMel project. You write a document, never code.

Three kinds of spec come through here, and they are shaped differently:

| Kind | Trigger | Shape |
|---|---|---|
| **New** | something that does not exist yet | Goal → Scope → AC with new tests |
| **Change** | existing behaviour should work differently | **Current behaviour → Desired behaviour**, then Scope → AC on updated or new tests |
| **Cleanup** | deletion, config, docs, a pure move — no behaviour changes | Goal → Scope → AC proved by commands, plus skip-tests |

A bug — something already broken against its own intent — is `/fix`, not `/design`: it needs a
reproduction first. Say so and stop. "I want it to work differently" is a **change**, and it is yours.

## Steps

1. Run `node scripts/plan-status.mjs` — it lists every open spec issue — then read **only** the sections of `architecture.md` / `domain.md` / `ideas.md`
   that this change actually touches. Not the folder. If an open issue already covers the idea, say
   so and ask whether to extend it instead.
2. Need a fact about the code (does this slice exist, what does that consumer do, is there a
   precedent)? Send an `Explore` subagent for it. Do not read a dozen files yourself.
   **For a change or a refactor this is mandatory before you write a line of the spec**: send
   `Explore` after the current implementation — which slices, entities, events, components and tests
   own the behaviour today — and write what it found under "Current behaviour". A change spec that
   guesses at today's code produces a Tier-2 mess at implementation time.
3. Ask as many questions as the design genuinely needs — there is no quota. The filter is not the
   count, it is this: **ask only what changes the design, and only what you cannot settle yourself.**
   Anything an ADR, a rule in `.claude/rules/`, or an existing pattern already answers, decide and
   record under Design decisions instead of asking. Use `AskUserQuestion`, one topic per question,
   batched 2–4 per call so the user answers a screen at a time rather than a drip. When an answer
   opens a real fork, ask the follow-up — a spec that hides an open question inside prose is worse
   than one more round.
4. Draft the issue body in the session scratchpad as `<slug>.md`, from
   `.claude/skills/design/issue-template.md`. **Never write it into the repo** — the draft is not a
   deliverable, the issue is. Pick a kebab-case `<slug>`; the branch becomes `feat/<slug>`. Keep a
   list of open questions in the conversation, not in the draft. Leave the template's HTML comments
   and unused sections in place — publishing strips them — and never re-write the draft only to
   tidy it.
5. Tier: **1** when a precedent pattern exists in the same service and no design decision is left
   open. **2** for a new pattern, anything cross-service, an algorithm that needs property tests, or
   any "decide at implementation" left in the text. A change that rewrites how an existing slice
   behaves is Tier 2 unless the rewrite is mechanical.
6. Every acceptance criterion is a checkbox, Given/When/Then, **and names the test or the command
   that proves it** (`AddAssetEndpointTests.Returns422`, `node scripts/verify.mjs --projects Portfolio`).
   An AC without one is not finished - either name it or drop it. For a change, name the **existing**
   test that must be updated, not just the new one.
7. skip-tests only when the spec adds and alters no behaviour at all (deletion, config, docs, a pure
   move). Then every AC's proof must be a command, a grep or an existing test class, and Verification
   says so in one line. Never reach for it to dodge writing a test for real behaviour — the workflow
   will ship that spec without a single new assertion.
8. If the design changes a hard rule, append a draft ADR (status 🕐) to `skarbiec-plan/decisions.md`
   and reference it from Design decisions. Do not silently break an ADR. This is the only repo file
   `/design` may touch.
9. Scope is a promise: everything the change does **not** include goes under Out of scope, especially
   the adjacent thing the user will assume is included. For a change spec, the adjacent thing is
   usually "and while we are in there, clean up X" — name it and exclude it.
10. Split only when the work has parts that can be built, reviewed and merged **independently**, each
    on its own branch. Then draft one umbrella (Goal, Why, overall Out of scope, and a numbered list
    of the parts in build order — no AC) plus one full spec per part. A spec that is merely long is
    still one issue.

## Finish

Run `node scripts/gh-project.mjs check --body-file <scratchpad>/<slug>.md` (`--epic` for an
umbrella) and fix what it lists — it checks Goal, Out of scope, and that every AC checkbox names a
`proof:`. Then present a summary of at most 15 lines: goal (for a change: today → wanted), kind, tier, scope, the
acceptance criteria as one line each, whether tests are skipped, the split (if any), and anything
still open. Then ask for approval. Iterate on the draft until the user says yes; nothing is published
before that.

Only after an explicit yes — and with no open question left (each becomes an AC, an Out of scope line
or a Design decision) — publish. The script cleans and re-checks the draft itself:

```
node scripts/gh-project.mjs create --title "<title>" --body-file <scratchpad>/<slug>.md \
  --slug <slug> --tier <1|2> --kind <new|change|cleanup> [--skip-tests]
```

For a split: first the umbrella with `--epic`, then each part in build order with
`--parent <umbrella number>`. The script prints `{"number","url","branch"}` per issue; if it fails
after the issue exists, it prints the URL — finish the missing fields with
`node scripts/gh-project.mjs set <number> <field> <value>` instead of creating a duplicate.

**Amending an existing issue** (step 1 found one, or `/build` stopped on a spec that needs fixing):
start the draft from `node scripts/gh-project.mjs get <n> --out <scratchpad>/<slug>.md --raw`, edit
only what changes, and after the same explicit yes publish with
`node scripts/gh-project.mjs edit <n> --body-file <scratchpad>/<slug>.md [--tier <1|2>]`. Never create a second issue for
the same spec.

Reply with the issue link(s) and tell the user to run `/build #<number>` (for a split: the first
part). Write no production code, no tests, no scaffolding at any point.
