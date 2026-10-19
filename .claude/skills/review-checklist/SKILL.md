---
name: review-checklist
description: Adversarial checklist for reviewing a diff against its spec - blocking vs minor findings, evidence required.
user-invocable: false
---

# Review checklist
Purpose: a fresh-context, adversarial review of the working tree against one spec. You have not seen the
implementation being defended — verify every claim against the tree yourself, cite evidence, never take a prior
agent's report at face value.

## Gather your own evidence
1. Read the spec at the given path in full: Scope, Design decisions, Acceptance criteria, Out of scope.
2. The change is uncommitted on its branch before you are called; `master...HEAD` is empty here. `node scripts/review-diff.mjs --stat` lists it, `node scripts/review-diff.mjs -- <path>` shows a file's diff (new untracked files included), plain `node scripts/review-diff.mjs` shows everything. Nothing is staged.
3. Read only the `skarbiec-plan/architecture.md`/`domain.md`/`decisions.md` sections the spec names, plus the `.claude/rules/*` files scoped to what the diff touches.
4. Read the changed files and the tests that claim to prove them.
5. Run read-only commands to settle a claim instead of guessing: a targeted `dotnet test --filter`, a `grep` for a pattern a hard rule forbids, `ls` for a file the spec says should exist.

## Blocking findings — only these
Each section below is blocking for its Area; `Common` applies to every change.

### Common
- An acceptance criterion with no test that would fail if the behavior regressed, and no verified command output backing it.
- A spec with the `skip-tests` label but changed real behavior — the skip is only for deletion, config, docs or a pure move. "No new tests" is then not a finding on its own; **behavior shipped under that skip is.**
- Scope creep: anything in the diff the spec doesn't ask for, or that its Out of scope section forbids.
- A convention or hard rule changed without a matching update to `.claude/rules/*` or `skarbiec-plan/decisions.md`.
- A `## Test manualny` plan — kept from the spec or rewritten in `skarbiec-plan/issues/<n>.manual.md` — that names a route, screen or action absent from the code. Wording problems in it are minor.

### Backend
- A hard-rule violation: throwing (instead of returning `Result`/`Result<T>`) for an expected failure path; a Service/Repository layer, or MediatR; `UserId` sourced from the request body or route instead of JWT claims; an event published outside the MassTransit outbox, or a consumer that isn't idempotent; a consumer calling the event's publisher back over REST instead of using the state the event already carries; cross-database access or a foreign key between services; `float`/`double` used for money instead of `decimal`; a new call to an external price/FX API from anywhere other than a MarketData job or `ITickerVerifier`.
- A new user-owned resource with no tenancy isolation test.
- A migration that doesn't match the model it's supposed to represent.
- The API surface changed but the generated TS client or `requests/<service>.http` wasn't updated.

### Frontend
- A hard-rule violation: Angular code calling a service directly instead of going through the Gateway client.

### Scripts
- An acceptance criterion with no `node --test` test (`<name>.test.mjs`) and no command shown in the run report.

### Claude
- CLAUDE.md disagrees with `skarbiec-plan/workflow.md`, another skill or an agent.
- A reference to a removed skill, agent, script, flag or ADR remains in `.claude/`, `CLAUDE.md` or `skarbiec-plan/`.

### CI/Infra
- The acceptance criterion's command does not pass locally, or the runbook or `deploy/README.md` the change affects is not updated.

### Docs
- A link or path in a changed doc does not exist.

### E2E
- An e2e part (the spec carries the `e2e` label; the e2e agent wrote `e2e/tests/<epic-slug>.spec.ts` from the epic's `## Test manualny`): a plan step that is neither a `test.step('Krok <n>: …')` nor marked `(tylko manualnie)`; an assertion whose value differs from the plan; a changed file outside `e2e/`; a new `test.skip`, `test.fixme` or `waitForTimeout`; an existing e2e test weakened or deleted; a new `toPass` / `expect.poll` on a projection value with no re-fetch in its body (blocking). In a repair rebuild with a diagnosis, compare the test diff with `skarbiec-plan/issues/<n>.e2e.patch`: a changed expected value or plan assertion is blocking. Read `.git/e2e-result.json` for the run — never start the stack.
- The e2e test conforms to `.claude/rules/e2e.md`, and no production file changed.

### Areas without a test-writer
- Scripts, Claude, Docs, CI/Infra and E2E have no test-writer. A changed production file under `services/`, `web/` or `contracts/` in such a change is `blocking`: the ticket has the wrong Area, or it must be split.

Everything else — naming, style, duplication, a "nicer" alternative structure — is `minor`, or not worth reporting at all.

## Do not raise
Requests for extra abstraction, defensive code for cases the type system already makes impossible, tests for impossible cases, style preferences the rules don't actually state, or an alternative design the spec already settled. Flag only what affects correctness or the spec — a short spec faithfully implemented is a clean review; say so.

## Evidence discipline
Every finding needs `evidence` you actually saw: a quoted line, a command's output, or confirmation a file is absent. No evidence, no finding. Never edit a file to prove a point, and never run a git mutation.

## Output
```json
{
  "findings": [
    {
      "severity": "blocking",
      "file": "services/Portfolio/Skarbiec.Portfolio/Features/AddAsset/AddAssetHandler.cs",
      "line": 42,
      "claim": "AC-3 has no test",
      "evidence": "grep -r \"Unknown\" Skarbiec.Portfolio.Tests returns nothing",
      "suggestedFix": "add a slice test posting an unknown instrument id and asserting the mapped status"
    }
  ],
  "summary": "1 blocking, 2 minor"
}
```
`findings: []` with a one-line `summary` is a valid, complete result when the change is clean.
