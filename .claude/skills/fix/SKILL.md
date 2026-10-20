---
name: fix
description: Reproduce a bug, find its root cause, publish a one-criterion fix spec as an issue on the FinMel GitHub project, and (after approval) build it with the same pipeline that ships features. Never patches without a failing reproduction.
argument-hint: "<bug description, failing test name, verify failure, log path, or idea:<slug>>"
disable-model-invocation: true
model: claude-opus-5-5
effort: high
---

Turn the bug in `$ARGUMENTS` into a verified fix. You diagnose and specify; the pipeline implements,
so the author of the fix is never its reviewer.

`$ARGUMENTS` = `idea:<slug>` → `node scripts/idea.mjs show <slug>` is the bug report (shaped by `/idea`):
start the reproduction from its "Kroki reprodukcji"; its "Hipotezy" are unverified; its "Powiązane"
names related issues. After `create` publishes, run `node scripts/idea.mjs mark <slug> used --issue <n>`.

`$ARGUMENTS` = `e2e:#<n>` → **unattended e2e repair**, launched by `scripts/autobuild.mjs` after e2e part `#<n>` blocked
at `e2e` (app). Nobody is watching: never call `AskUserQuestion`, never build. Follow **Unattended e2e repair** below
instead of steps 0 and 4.

## 0. Is it already known?

`node scripts/plan-status.mjs` lists every open spec issue. One that
already covers the bug → say so and ask whether to build that one (`/build #<n>`) instead.

## 1. Reproduce before anything else

Pick the cheapest reproduction that actually shows the bug, in this order (when it needs tests or the stack, run
`node scripts/preflight.mjs --no-web` first — it starts Docker and stops a stale stack):

- A test name → `dotnet test <project> --filter "FullyQualifiedName~<Name>"` (or `cd web && npm test -- --watch=false`).
- A red `verify.mjs` → re-run it for the named projects only: `node scripts/verify.mjs --projects <A,B|web>`.
- A log path → read it; quote the failing lines.
- A runtime or UI symptom → run the stack (`run` skill: `dotnet run --project Skarbiec.AppHost`, `cd web && npm start`)
  and reproduce through `requests/<service>.http` or Playwright. Docker must be running. Stop it with
  `node scripts/stop-stack.mjs` once you have the reproduction — a running stack locks the build outputs
  the pipeline needs.

No reproduction → stop and say exactly what you tried and what you saw. Do not guess at a fix.

## 2. Localize the root cause

- Facts spread over many files → one `Explore` subagent (runs on Haiku) with a narrow question; read only the files it names.
- Follow the failure to its cause, not its first symptom: a 500 is a symptom, an unregistered handler is a cause;
  a stale dashboard value is a symptom, a consumer that never publishes is a cause.
- State the root cause with evidence you saw — a quoted line, a test output, a trace. No evidence, no claim.
- If the cause is a missing design (an event that does not exist, a rule the ADRs never decided), say so and stop:
  that is `/design`, not `/fix`.

## 3. Draft the fix issue

In the session scratchpad as `fix-<slug>.md`, from `.claude/skills/design/issue-template.md` —
**never in the repo**. Keep it short:

- Title `Fix: <symptom>`. Tier **1** unless the fix changes an event contract, a data model, or
  crosses a service boundary (then **2**).
- Goal = expected behaviour, one sentence. **Current behaviour** = the observed symptom and the
  reproduction you ran. Why = the root cause and its evidence.
- Scope = the files and slices that must change; Out of scope = the neighbouring refactor you were tempted by.
- Code map = what your localization already found, as `path:start-end — why` lines: the file with the root
  cause, the test class and `Fixtures/` helpers the reproduction test belongs in, the nearest
  precedent. The build agents start from it instead of re-localizing the bug.
- Depends on = an open issue that must merge first, if any (rare for a fix).
- Area = `node scripts/areas.mjs of <Code map paths>`, i.e. the area of the file holding the cause; a fix that
  must change backend and frontend files together is Fullstack by the Fullstack rule, otherwise an epic of fixes
  (step 3a), one part per area.
- Design decisions = the fix approach and why it targets the cause.
- Acceptance criteria: **AC-1 is a reproduction test** that fails today and passes after the fix, named
  `Method_Scenario_Outcome` in the right test project (or `*.spec.ts` for Angular); **AC-2** = the touched
  projects stay green — proof `node scripts/verify.mjs --projects <A,B|web>`. Add more only for edge cases the
  root cause implies.
- `## Test manualny` = the reproduction steps as a person runs them in the app, numbered, with the result
  expected after the fix on each step's `→` line (a **Przygotowanie** block, then **Kroki**, in Polish, per
  `issue-template.md`). Under the same skip rule as `/design` step 8: omit it when the bug is not visible in the app.
- Leave empty sections and HTML comments alone — publishing strips them.

## 3a. Split, when one fix is not one PR

Either of you can call for a split at any point of steps 1–3 — the user by asking, you by proposing it
with `AskUserQuestion` (the cut you recommend, one line per part, versus "keep it one fix") as soon as
the evidence shows one of:
- **several independent root causes** behind one symptom (each has its own reproduction and its own fix);
- **one cause whose fix must land in sequence** — a contract or data-model change, then its consumer,
  then the UI — where each step leaves master green;
- a fix too large to review as one diff;
- the fix spans more than one area and fails the Fullstack rule (`skarbiec-plan/workflow.md` → Area): one part per area.

A neighbouring refactor is never a part — it stays Out of scope, split or not.

Then follow **Splitting into an epic** in `.claude/skills/design/SKILL.md`, with these differences:
the umbrella is titled `Fix: <symptom>` and published with `--epic --kind fix`; every part is
`--kind fix` (so its branch is `fix/<part-slug>`) and keeps the shape above — its own **AC-1 reproduction
test that fails today**, reproducing the defect of its own layer, plus the `verify.mjs` AC for its projects.
A part whose reproduction only goes green once a later part merges is a wrong cut — re-cut it.

## 4. Approve, publish, build

Run `node scripts/gh-project.mjs check --body-file <scratchpad>/fix-<slug>.md` and fix what it lists.
Then present at most 10 lines: symptom, root cause + evidence, fix approach, tier, the AC-1 test name — for a
split, one line per part (title, tier, AC-1 test) instead of the last three. Add `Obszar: <Area>` (one per part for a split, none for the umbrella); the user may change it. Add one line `Priorytet: P1 — <powód> → <output of node scripts/gh-project.mjs priority --where P1 --body-file <scratchpad>/fix-<slug>.md>` (P0 when the feature does not work at all; for a split the umbrella's, parts none; the user may change it). Ask for approval; iterate on the draft until the user says yes. Only after an explicit yes:

```
node scripts/gh-project.mjs create --title "Fix: <symptom>" --body-file <scratchpad>/fix-<slug>.md \
  --slug <slug> --tier <1|2> --kind fix --area <area> --priority <P0|P1>
```

It prints `{"number","url","branch"}` — the branch is `fix/<slug>`. It reads the card back and rewrites a missing field once;
if a Branch or Tier is still missing it exits non-zero with the ready `node scripts/gh-project.mjs set <number> <field> <value>`
commands (a missing Area or Priority is only a warning) — run those instead of creating a duplicate. For a split: the umbrella first (`--epic`), then each part in build order
with `--parent <umbrella number>` and its own `--area` (no `--priority`); the umbrella takes `--priority` and no `--area`.

Then build it — for a split, the **first part only**; each next part is `/build #<n>` for the user once the
previous one has merged. Build it exactly as `/build` steps 1–5 do and report as `/build` does.

## Unattended e2e repair (`e2e:#<n>`)

1. **The failure.** `node scripts/gh-project.mjs get <n>` gives the epic (`parent`). The newest comment on `#<n>`
   (`gh issue view <n> --comments`) is the block: test, `Krok <k>`, trace, patch, the e2e agent's one-line cause, its
   deviations and its `self-check:` line (what it rewrote with `expectProjected`, the wait, the result). The plan is the
   epic's `## Test manualny`, or the part's own when its body has a numbered `## Test manualny` (an own-plan part). Read it, the sibling parts' Goal / ACs / Design decisions
   (`node scripts/gh-project.mjs get <m>`), the test code in `skarbiec-plan/issues/<n>.e2e.patch` and
   `e2e/test-results/stack.log`. The closed `Fix:` parts of the same epic are earlier repairs: the same symptom again
   means the earlier fix missed the cause — go deeper, never repeat it.
2. **Reproduce** below the browser, as in step 1: a filtered `dotnet test`, an Angular spec, or the stack and an
   `.http` request. The e2e run counts as the symptom, not as the reproduction. Stop the stack when done; never start
   `scripts/e2e.mjs` or touch `scripts/compose.mjs`. Leave the working tree clean and on master — `autobuild.mjs` refuses a dirty one.
3. **Decide which side is wrong**, with a quoted line as evidence:
   - **app** — the app contradicts the plan, and nothing in the epic or its parts specifies what the app does → steps 2–3
     above, then `node scripts/gh-project.mjs check --body-file …` and
     `create … --kind fix --area <area> --parent <epic> --before <n>` (the part lands ahead of the e2e part, so the run builds and merges
     it first). Several causes → one part per cause, each `--before <n>`, in build order. No approval step.
   - **plan** — a part's AC or Design decision specifies exactly what the app does, and the plan step contradicts it →
     rewrite only the wrong steps of the epic's `## Test manualny` (numbered, Polish, `→` lines, the whole section) in the
     scratchpad, then `node scripts/gh-project.mjs section <epic> --heading "Test manualny" --file <path>` and
     `node scripts/gh-project.mjs comment <epic> --body-file <path>` naming the steps changed and the quoted spec line.
     Never loosen a step to dodge a defect: when in doubt, it is **app**.
   - **test** — through the API and after a reload the app reaches the plan's value within 30 s, and the test waits
     wrongly, does not re-fetch, or has a wrong selector: the fix lies entirely in `e2e/`. Write the diagnosis to the
     scratchpad and post it with `node scripts/gh-project.mjs comment <n> --body-file <path>`: first line
     `**E2E test defect** — <test title>, Krok <k>`, then what is wrong in the test, the evidence (trace, API calls,
     timings) and the fix hint (e.g. "assert through `expectProjected` with a reload"). Never a changed expected value.
     The next build of the part reads it as its newest comment: post nothing after it.
   - **stop** — no reproduction, a missing design (`/design`), or the cause is outside the app (the e2e harness, the stub,
     the environment, no project).
   - The boundary: a value that never arrives, arrives after more than 30 s (measured) or differs from the plan is
     **app**; no reproduction, a stub, harness or environment failure, or no project is **stop**.
4. The last line of your final message is exactly one line
   `FIX_RESULT: {"ok":true,"kind":"app","issues":[<numbers>],"summary":"…"}`, or `{"ok":true,"kind":"plan","summary":"…"}`,
   or `{"ok":true,"kind":"test","summary":"…"}`,
   or `{"ok":false,"kind":"stop","summary":"<what stops it and what the user must decide>"}`. `/fix e2e` changes no file.

## Hard constraints

- Write no production code and no test yourself — the test-writer writes the reproduction test, the implementer
  fixes it, the reviewer checks it. Your only artefact is the issue.
- Never run `git add`, `git commit`, `git push`, `git switch`, `git checkout`, `git stash` yourself — `ship.mjs`
  commits, pushes and opens the PR. Read-only git is fine. Never merge.
- A flaky test is a bug in the test or the fixture, not a reason to retry until green — treat it like any other bug.
- Never widen the fix into cleanup. One cause, one fix, one PR — several causes or a sequenced fix become an
  epic of such parts (3a), never one wide PR.
