---
name: design
description: Interview the user about a feature, a change to existing behaviour, or a refactor, then - after approval - publish a build-ready spec as an issue on the FinMel GitHub project. Writes no code.
argument-hint: "<feature, change or refactor description | idea:<slug>>"
disable-model-invocation: true
model: claude-opus-5-5
effort: high
---

Turn `$ARGUMENTS` into a spec that `/build` can execute unattended. The spec is drafted locally,
discussed until approved, and only then published as a GitHub issue with Status **Todo** on the
FinMel project. You write a document, never code.

`$ARGUMENTS` = `idea:<slug>` → `node scripts/idea.mjs show <slug>` is the request (shaped by `/idea`):
its "Decyzje już podjęte" are settled — do not re-ask them; its "Otwarte pytania dla /design" are yours
to ask or settle; its "Powiązane" names related issues — an open `#n` listed first is the issue to amend
(**Amending an existing issue** below). After `create`/`edit` publishes, run
`node scripts/idea.mjs mark <slug> used --issue <n>`.

Three kinds of spec come through here, and they are shaped differently:

| Kind | Trigger | Shape |
|---|---|---|
| **New** | something that does not exist yet | Goal → Scope → AC with new tests |
| **Change** | existing behaviour should work differently | **Current behaviour → Desired behaviour**, then Scope → AC on updated or new tests |
| **Cleanup** | deletion, config, docs, a pure move — no behaviour changes | Goal → Scope → AC proved by commands, plus skip-tests |

A bug — something already broken against its own intent — is `/fix`, not `/design`: it needs a
reproduction first. Say so and stop. "I want it to work differently" is a **change**, and it is yours.

## Steps

1. Run `node scripts/plan-status.mjs` — it lists every open spec issue — and `node scripts/gh-project.mjs queue` (the build
   queue in board order, with each item's `id`, `manual` and an epic's `next`), then read **only** the sections of `architecture.md` / `domain.md` / `ideas.md`
   that this change actually touches. Not the folder. If an open issue already covers the idea, say
   so and ask whether to extend it instead. An `idea:<slug>` prompt: its `area:` and `## Proponowany podział`
   are the starting cut (`epic` means an umbrella with those parts).
2. Need a fact about the code (does this slice exist, what does that consumer do, is there a
   precedent)? Send an `Explore` subagent for it. Do not read a dozen files yourself.
   **For a change or a refactor this is mandatory before you write a line of the spec**: send
   `Explore` after the current implementation — which slices, entities, events, components and tests
   own the behaviour today — and write what it found under "Current behaviour". A change spec that
   guesses at today's code produces a Tier-2 mess at implementation time.
   **Every spec gets a `## Code map`** (new behaviour too): ask Explore for the precedent
   slice/component to copy, the test class and `Fixtures/<Service>Api` helpers to extend, and the files
   that will change — as `path:start-end — why` or `path — Member()` lines, 5–15 of them — and paste that list into the draft. The test-writer,
   implementer and reviewer start from it instead of each rediscovering the code, which is where most
   of a build run's tokens went (measure with `node scripts/run-cost.mjs`).
   The draft's area: `node scripts/areas.mjs of <the Code map paths>`, checked against the general rule in
   `skarbiec-plan/workflow.md` → Area.
   If Explore finds the change needs another **open** issue merged first, list it under
   `## Depends on` — `prepare` then refuses to build until it is closed.
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
8. **Manual test plan.** Write `## Test manualny` (the shape is in `issue-template.md`) when the change is visible in
   the app. Omit the section for a `skip-tests` spec, a cleanup or a workflow spec. For a part or a single spec, a
   **part plan**: a numbered **Przygotowanie** block (the standard preparation in `skarbiec-plan/workflow.md` →
   Manual test plan, plus what this change adds), then 5–15 numbered **Kroki** on concrete PLN amounts, dates,
   tickers and routes, each ending in `→ <expected result>`. Describe behaviour, not exact labels, unless the label
   is an AC. Write the steps in Polish. A part with no UI effect of its own gets only the line
   `Weryfikacja w planie epica, krok <n>.`, where `<n>` is its step in the umbrella's plan. The approval summary
   names the plan's step count (`Test manualny: 9 kroków`), or `brak planu` when the section is omitted.
9. If the design changes a hard rule, append a draft ADR (status 🕐) to `skarbiec-plan/decisions.md`
   and reference it from Design decisions. Do not silently break an ADR. This is the only repo file
   `/design` may touch.
10. Scope is a promise: everything the change does **not** include goes under Out of scope, especially
   the adjacent thing the user will assume is included. For a change spec, the adjacent thing is
   usually "and while we are in there, clean up X" — name it and exclude it.
11. Watch for a split the whole time — see **Splitting into an epic** below.

## Splitting into an epic

A split can come up at any moment — while you read the plan, when `Explore` reports back, in the
middle of the interview, after the draft, or when `/build` blocked because the spec was too big —
and from either side: the user says "split it", or you see the signal and **propose it yourself**
instead of silently writing one oversized spec. Propose with `AskUserQuestion`: the cut you
recommend (one line per part: what it delivers, what master can do after it merges), "keep it one
spec" and, where it makes sense, a coarser or finer cut. The user decides.

Signals worth a proposal:
- the ACs fall into groups that share no test project and no slice;
- the work spans services in sequence (contract + publisher → consumer → UI) and each step leaves
  master working;
- one part is mechanical (Tier 1, or `skip-tests` cleanup) and another is a new pattern (Tier 2) —
  splitting lets each run with its own tier and skip;
- the change has more than one area and fails the Fullstack rule (`skarbiec-plan/workflow.md` → Area; Backend
  plus Frontend alone is one Fullstack spec): cut an epic Backend → Frontend → E2E, one part per area, the E2E
  part only when the change is visible in the app;
- the diff would be too large to review in one sitting (roughly > 8 ACs or > 3 services) — past
  that, a single run's test-writer and implementer contexts grow past 300k tokens.

A good cut:
- **Each part is one issue, one branch, one PR**, with its own Goal, Scope, Out of scope and ACs that
  prove it on its own — never an AC that only passes once a later part merges.
- **Each part leaves master green and coherent.** A contract nobody publishes yet is fine; a
  half-migrated slice, or a UI that calls an endpoint that does not exist yet, is not.
- **Granular but not trivial:** a part too small for its own PR (a rename, one config line) folds into
  its neighbour. A spec that is merely long is still one issue.
- **Build order is merge order.** Parts are attached in build order and `prepare` refuses a part
  while an earlier sibling is open, because every branch is cut from master. Work that is truly
  independent of the rest is not a part — it is a separate spec with no epic.

Drafting: the umbrella from `.claude/skills/design/epic-template.md` as `<slug>.md` (Goal, Why,
Parts in build order, overall Out of scope, cross-part Design decisions — no AC), plus one full spec
per part from `issue-template.md` as `<slug>-<n>-<part-slug>.md`, each with its own tier, kind and
skip-tests. The umbrella gets the **whole-feature plan**: its `## Test manualny` is one continuous
scenario of about 15–30 steps, from an empty account to the final state, assuming all parts merged;
each part carries its own part plan or the `Weryfikacja w planie epica, krok <n>.` line (step 8).
Check the umbrella with `check --epic` and every part with `check` (the e2e part with `check --e2e`). The approval summary
lists the parts one line each (title, tier, kind, AC count, plan step count).

**The e2e part.** Every epic whose change is visible in the app ends with a last part drafted from
`.claude/skills/design/e2e-part-template.md` as `<slug>-<n>-e2e.md`: fill only its placeholders, keep its four fixed ACs.
It is created with `--e2e --parent <umbrella> --tier 1 --kind new` (never `--skip-tests`), attached last, and
depends on the part before it. Its `/build` run automates the umbrella's `## Test manualny` as Playwright tests,
refreshes that plan and runs the whole e2e suite; a stable app failure blocks it and stops `scripts/autobuild.mjs`. It has
no `## Test manualny` of its own. Add no e2e part to an epic with no app-visible change (infrastructure, workflow,
cleanup), nor to the e2e epic itself, whose smoke is its e2e. The approval summary lists the e2e part like any other
part (title, tier 1, kind new, 4 ACs, `brak planu`).

An e2e coverage epic (e2e tests for features that already exist) is different: it is a sequence of **own-plan** e2e parts,
one per area, each drafted from the own-plan variant in `e2e-part-template.md`, created with `--e2e --parent <umbrella>`.
Each carries its own numbered `## Test manualny`, names its `e2e/tests/*.spec.ts` file in the Goal, and its build
automates that plan and refreshes it on its own issue. The umbrella holds no plan and gets no trailing e2e part.

Converting an already published spec (the user asks, or `/build` blocked on it): the existing issue
stays a part — narrow its body to that part with `edit --body-file`, never delete and re-create it —
and the new epic is created around it. Attach in build order: create the epic, then for each part
either `create --parent <epic>` (new part) or `edit <n> --parent <epic>` (the existing issue).

## Finish

Run `node scripts/gh-project.mjs check --body-file <scratchpad>/<slug>.md` (`--epic` for an
umbrella) and fix what it lists — it checks Goal, Out of scope, that every AC checkbox names a
`proof:`, and the shape of `## Test manualny` (a warning when it is missing, an error when it has a `- [ ]`
line or neither a numbered step nor the `Weryfikacja w planie epica` line). Then present a summary of at most 15 lines: goal (for a change: today → wanted), kind, tier, scope, the
acceptance criteria as one line each, `Obszar: <Area>` (one line per part for a split, none for the umbrella; the user may change it before publishing), whether tests are skipped, the manual plan's step count (or `brak planu`), the split (if any), and anything
still open, plus one line `Kolejka: <position> — <reason>` where the position is `top`, `bottom`, `before:N` or `after:N`
against the queue (for an epic, the epic's position; parts never get one), e.g. `Kolejka: before:400 — przed #400, bo #400 czyta pole, które dodaje ten spec`.
Default `bottom`. Then ask for approval. Iterate on the draft until the user says yes; nothing is published
before that.

Only after an explicit yes — and with no open question left (each becomes an AC, an Out of scope line
or a Design decision) — publish. The script cleans and re-checks the draft itself:

```
node scripts/gh-project.mjs create --title "<title>" --body-file <scratchpad>/<slug>.md \
  --slug <slug> --tier <1|2> --kind <new|change|cleanup> --area <area> [--skip-tests] --queue <position>
```

`--queue` is the approved `Kolejka:` position and applies to a top-level item (a single spec or the umbrella). A part
(`--parent`) takes no `--queue`: it lands right after the previous part. Move an existing item later with
`node scripts/gh-project.mjs move <n> <position>`.

For a split: first the umbrella with `--epic` (its `--tier` is the highest of its parts, its
`--kind` the one most parts share), then each part in build order with `--parent <umbrella number>`
and its own `--slug`, `--tier`, `--kind`, `--area`, `--skip-tests`, the e2e part last with `--e2e` (which defaults `--area` to E2E). The umbrella takes no `--area`. The script prints `{"number","url","branch"}` per issue; if it fails
after the issue exists, it prints the URL — finish the missing fields with
`node scripts/gh-project.mjs set <number> <field> <value>` instead of creating a duplicate.

**Amending an existing issue** (step 1 found one, or `/build` stopped on a spec that needs fixing):
start the draft from `node scripts/gh-project.mjs get <n> --out <scratchpad>/<slug>.md --raw`, edit
only what changes, and after the same explicit yes publish with
`node scripts/gh-project.mjs edit <n> --body-file <scratchpad>/<slug>.md [--tier <1|2>] [--area <area>]`. Never create a second issue for
the same spec.

Reply with the issue link(s) and tell the user to run `/build #<number>` (for a split: the first
part — each next part is buildable once the previous one has merged). Write no production code, no tests, no scaffolding at any point.
