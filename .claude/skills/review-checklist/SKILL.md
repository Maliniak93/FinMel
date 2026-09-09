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
2. `git status --porcelain` and `git diff` for uncommitted work; `git diff master...HEAD` for what's already committed on the branch. Check both — an unstaged file is still part of the change under review.
3. Read only the `skarbiec-plan/architecture.md`/`domain.md`/`decisions.md` sections the spec names, plus the `.claude/rules/*` files scoped to what the diff touches.
4. Read the changed files and the tests that claim to prove them.
5. Run read-only commands to settle a claim instead of guessing: a targeted `dotnet test --filter`, a `grep` for a pattern a hard rule forbids, `ls` for a file the spec says should exist.

## Blocking findings — only these
- An acceptance criterion with no test that would fail if the behavior regressed, and no verified command output backing it.
- A hard-rule violation: throwing (instead of returning `Result`/`Result<T>`) for an expected failure path; a Service/Repository layer, or MediatR; `UserId` sourced from the request body or route instead of JWT claims; an event published outside the MassTransit outbox, or a consumer that isn't idempotent; a consumer calling the event's publisher back over REST instead of using the state the event already carries; cross-database access or a foreign key between services; `float`/`double` used for money instead of `decimal`; Angular code calling a service directly instead of going through the Gateway client; a new call to an external price/FX API from anywhere other than a MarketData job or `ITickerVerifier`.
- Scope creep: anything in the diff the spec doesn't ask for, or that its Out of scope section forbids.
- A new user-owned resource with no tenancy isolation test.
- A migration that doesn't match the model it's supposed to represent.
- The API surface changed but the generated TS client or `requests/<service>.http` wasn't updated.
- A convention or hard rule changed without a matching update to `.claude/rules/*` or `skarbiec-plan/decisions.md`.

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
