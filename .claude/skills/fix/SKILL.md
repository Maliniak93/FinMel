---
name: fix
description: Reproduce a bug, find its root cause, publish a one-criterion fix spec as an issue on the FinMel GitHub project, and (after approval) build it with the same pipeline that ships features. Never patches without a failing reproduction.
argument-hint: "<bug description, failing test name, verify failure, or log path>"
disable-model-invocation: true
model: claude-opus-5-5
effort: high
---

Turn the bug in `$ARGUMENTS` into a verified fix. You diagnose and specify; the pipeline implements,
so the author of the fix is never its reviewer.

## 0. Is it already known?

`node scripts/plan-status.mjs` lists every open spec issue. One that
already covers the bug → say so and ask whether to build that one (`/build #<n>`) instead.

## 1. Reproduce before anything else

Pick the cheapest reproduction that actually shows the bug, in this order:

- A test name → `dotnet test <project> --filter "FullyQualifiedName~<Name>"` (or `cd web && npm test -- --watch=false`).
- A red `verify.mjs` → re-run it for the named projects only: `node scripts/verify.mjs --projects <A,B|web>`.
- A log path → read it; quote the failing lines.
- A runtime or UI symptom → run the stack (`run` skill: `dotnet run --project Skarbiec.AppHost`, `cd web && npm start`)
  and reproduce through `requests/<service>.http` or Playwright. Docker must be running.

No reproduction → stop and say exactly what you tried and what you saw. Do not guess at a fix.

## 2. Localize the root cause

- Facts spread over many files → one `Explore` subagent with a narrow question; read only the files it names.
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
- Design decisions = the fix approach and why it targets the cause.
- Acceptance criteria: **AC-1 is a reproduction test** that fails today and passes after the fix, named
  `Method_Scenario_Outcome` in the right test project (or `*.spec.ts` for Angular); **AC-2** = the touched
  projects stay green — proof `node scripts/verify.mjs --projects <A,B|web>`. Add more only for edge cases the
  root cause implies.
- Leave empty sections and HTML comments alone — publishing strips them.

## 3a. Split, when one fix is not one PR

Either of you can call for a split at any point of steps 1–3 — the user by asking, you by proposing it
with `AskUserQuestion` (the cut you recommend, one line per part, versus "keep it one fix") as soon as
the evidence shows one of:
- **several independent root causes** behind one symptom (each has its own reproduction and its own fix);
- **one cause whose fix must land in sequence** — a contract or data-model change, then its consumer,
  then the UI — where each step leaves master green;
- a fix too large to review as one diff.

A neighbouring refactor is never a part — it stays Out of scope, split or not.

Then follow **Splitting into an epic** in `.claude/skills/design/SKILL.md`, with these differences:
the umbrella is titled `Fix: <symptom>` and published with `--epic --kind fix`; every part is
`--kind fix` (so its branch is `fix/<part-slug>`) and keeps the shape above — its own **AC-1 reproduction
test that fails today**, reproducing the defect of its own layer, plus the `verify.mjs` AC for its projects.
A part whose reproduction only goes green once a later part merges is a wrong cut — re-cut it.

## 4. Approve, publish, build

Run `node scripts/gh-project.mjs check --body-file <scratchpad>/fix-<slug>.md` and fix what it lists.
Then present at most 10 lines: symptom, root cause + evidence, fix approach, tier, the AC-1 test name — for a
split, one line per part (title, tier, AC-1 test) instead of the last three. Ask for approval; iterate on the draft until the user says yes. Only after an explicit yes:

```
node scripts/gh-project.mjs create --title "Fix: <symptom>" --body-file <scratchpad>/fix-<slug>.md \
  --slug <slug> --tier <1|2> --kind fix
```

It prints `{"number","url","branch"}` — the branch is `fix/<slug>`. If it fails after the issue exists it
prints the URL: finish the missing fields with `node scripts/gh-project.mjs set <number> <field> <value>`
instead of creating a duplicate. For a split: the umbrella first (`--epic`), then each part in build order
with `--parent <umbrella number>`.

Then build it — for a split, the **first part only**; each next part is `/build #<n>` for the user once the
previous one has merged. Build exactly as `/build` does: `node scripts/gh-project.mjs prepare <number>`, then the
`Workflow` tool with `{ name: 'build-feature', args: <workflowArgs, verbatim> }`. The workflow posts
its own report on the issue; report to the user as `/build` does (at most 10 lines, ending with the
three `nextSteps` commands, which you never run).

## Hard constraints

- Write no production code and no test yourself — the test-writer writes the reproduction test, the implementer
  fixes it, the reviewer checks it. Your only artefact is the issue.
- Never run `git add`, `git commit`, `git push`, `git switch`, `git checkout`, `git stash`. Read-only git is fine.
- A flaky test is a bug in the test or the fixture, not a reason to retry until green — treat it like any other bug.
- Never widen the fix into cleanup. One cause, one fix, one PR — several causes or a sequenced fix become an
  epic of such parts (3a), never one wide PR.
