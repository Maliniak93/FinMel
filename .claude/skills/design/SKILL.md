---
name: design
description: Interview the user about a feature, a change to existing behaviour, or a refactor, then write a build-ready spec in skarbiec-plan/specs/. Writes no code.
argument-hint: "<feature, change or refactor description>"
disable-model-invocation: true
model: opus
---

Turn `$ARGUMENTS` into a spec that `/build` can execute unattended. You write a document, never code.

Three kinds of spec come through here, and they are shaped differently:

| Kind | Trigger | Shape |
|---|---|---|
| **New behaviour** | something that does not exist yet | Goal → Scope → AC with new tests |
| **Change** | existing behaviour should work differently | **Current behaviour → Desired behaviour**, then Scope → AC on updated or new tests |
| **Cleanup** | deletion, config, docs, a pure move — no behaviour changes | Goal → Scope → AC proved by commands, plus `skip: [tests]` |

A bug — something already broken against its own intent — is `/fix`, not `/design`: it needs a
reproduction first. Say so and stop. "I want it to work differently" is a **change**, and it is yours.

## Steps

1. Read `skarbiec-plan/README.md` for current status, then **only** the sections of
   `architecture.md` / `domain.md` / `ideas.md` that this change actually touches. Not the folder.
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
4. Write `skarbiec-plan/specs/<slug>.md` from `skarbiec-plan/specs/_template.md`:
   frontmatter `title`, `status: draft`, `tier`, `branch: feat/<slug>`, `created`, `skip` (usually
   `[]`); then Goal (plus Current behaviour for a change), Why, Scope (backend / frontend), Out of
   scope, Design decisions, Data / API changes, Acceptance criteria, Verification, Risks / open
   questions.
5. Tier: **1** when a precedent pattern exists in the same service and no design decision is left
   open. **2** for a new pattern, anything cross-service, an algorithm that needs property tests, or
   any "decide at implementation" left in the text. A change that rewrites how an existing slice
   behaves is Tier 2 unless the rewrite is mechanical.
6. Every acceptance criterion is Given/When/Then **and names the test or the command that proves it**
   (`AddAssetEndpointTests.Returns422`, `node scripts/verify.mjs --projects Portfolio`). An AC without
   one is not finished - either name it or drop it. For a change, name the **existing** test that
   must be updated, not just the new one.
7. `skip: [tests]` only when the spec adds and alters no behaviour at all (deletion, config, docs, a
   pure move). Then every AC's proof must be a command, a grep or an existing test class, and
   Verification says so in one line. Never reach for it to dodge writing a test for real behaviour —
   the workflow will ship that spec without a single new assertion.
8. If the design changes a hard rule, append a draft ADR (status 🕐) to `skarbiec-plan/decisions.md`
   and reference it from Design decisions. Do not silently break an ADR.
9. Scope is a promise: everything the change does **not** include goes under Out of scope, especially
   the adjacent thing the user will assume is included. For a change spec, the adjacent thing is
   usually "and while we are in there, clean up X" — name it and exclude it.

## Finish

Present a summary of at most 15 lines: goal (for a change: today → wanted), tier, scope, the
acceptance criteria as one line each, whether tests are skipped, and anything still open. Then ask
for approval.

Only after an explicit yes, set `status: approved`, empty out Risks / open questions (an approved spec
has none - anything left becomes an AC, an Out of scope line or a Design decision), and tell the user
to run `/build <slug>`. Write no production code, no tests, no scaffolding at any point.
