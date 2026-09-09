---
name: design
description: Interview the user about a feature or change, then write a build-ready spec in skarbiec-plan/specs/. Writes no code.
argument-hint: "<feature or change description>"
disable-model-invocation: true
model: opus
---

Turn `$ARGUMENTS` into a spec that `/build` can execute unattended. You write a document, never code.

## Steps

1. Read `skarbiec-plan/README.md` for current status, then **only** the sections of
   `architecture.md` / `domain.md` / `ideas.md` that this change actually touches. Not the folder.
2. Need a fact about the code (does this slice exist, what does that consumer do, is there a
   precedent)? Send an `Explore` subagent for it. Do not read a dozen files yourself.
3. Ask only questions whose answer changes the design. Use `AskUserQuestion`, one topic per question,
   at most about five in total. Anything you can decide from the ADRs, the rules or an existing
   pattern, decide - and record it under Design decisions instead of asking.
4. Write `skarbiec-plan/specs/<slug>.md` from `skarbiec-plan/specs/_template.md`:
   frontmatter `title`, `status: draft`, `tier`, `branch: feat/<slug>`, `created`; then Goal, Why,
   Scope (backend / frontend), Out of scope, Design decisions, Data / API changes, Acceptance
   criteria, Verification, Risks / open questions.
5. Tier: **1** when a precedent pattern exists in the same service and no design decision is left
   open. **2** for a new pattern, anything cross-service, an algorithm that needs property tests, or
   any "decide at implementation" left in the text.
6. Every acceptance criterion is Given/When/Then **and names the test or the command that proves it**
   (`AddAssetEndpointTests.Returns422`, `node scripts/verify.mjs --projects Portfolio`). An AC without
   one is not finished - either name it or drop it.
7. If the design changes a hard rule, append a draft ADR (status 🕐) to `skarbiec-plan/decisions.md`
   and reference it from Design decisions. Do not silently break an ADR.
8. Scope is a promise: everything the change does **not** include goes under Out of scope, especially
   the adjacent thing the user will assume is included.

## Finish

Present a summary of at most 15 lines: goal, tier, scope, the acceptance criteria as one line each,
and anything still open. Then ask for approval.

Only after an explicit yes, set `status: approved`, empty out Risks / open questions (an approved spec
has none - anything left becomes an AC, an Out of scope line or a Design decision), and tell the user
to run `/build <slug>`. Write no production code, no tests, no scaffolding at any point.
