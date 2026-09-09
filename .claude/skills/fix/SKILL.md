---
name: fix
description: Reproduce a bug, find its root cause, write a one-criterion fix spec, and (after approval) run the same build pipeline that ships features. Never patches without a failing reproduction.
argument-hint: "<bug description, failing test name, verify failure, or log path>"
disable-model-invocation: true
model: opus
effort: high
---

Turn the bug in `$ARGUMENTS` into a verified fix. You diagnose and specify; the pipeline implements, so the
author of the fix is never its reviewer.

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

## 3. Write the fix spec

`skarbiec-plan/specs/fix-<slug>.md` from `skarbiec-plan/specs/_template.md`, kept short:

- Frontmatter: `title: Fix: <symptom>`, `status: draft`, `branch: feat/fix-<slug>`, `created`, `tier` — **1** unless
  the fix changes an event contract, a data model, or crosses a service boundary (then **2**).
- Goal = observed vs expected, one sentence each. Why = the root cause and its evidence.
- Scope = the files and slices that must change; Out of scope = the neighbouring refactor you were tempted by.
- Design decisions = the fix approach and why it targets the cause.
- Acceptance criteria: **AC-1 is a reproduction test** that fails today and passes after the fix, named
  `Method_Scenario_Outcome` in the right test project (or `*.spec.ts` for Angular); **AC-2** = the touched
  projects stay green — proof `node scripts/verify.mjs --projects <A,B|web>`. Add more only for edge cases the
  root cause implies.
- Risks / open questions empty.

## 4. Approve and build

Present at most 10 lines: symptom, root cause + evidence, fix approach, tier, the AC-1 test name. Ask for
approval. Only after an explicit yes: set `status: approved`, then call the `Workflow` tool with
`{ name: 'build-feature', args: { spec: 'skarbiec-plan/specs/fix-<slug>.md', tier: <tier>, maxRounds: 2 } }`
and report exactly as `/build` does (status, PR URL, tests, files, rounds — merging is the user's).

## Hard constraints

- Write no production code and no test yourself — the test-writer writes the reproduction test, the implementer
  fixes it, the reviewer checks it. Your only files are the spec and nothing else.
- Never run `git add`, `git commit`, `git push`, `git switch`, `git checkout`, `git stash`. Read-only git is fine.
- A flaky test is a bug in the test or the fixture, not a reason to retry until green — treat it like any other bug.
- Never widen the fix into cleanup. One cause, one fix, one PR.
