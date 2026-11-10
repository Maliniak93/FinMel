---
name: review-checklist
description: The blocking findings of a review, one section per Area - the reviewer applies the sections its delegation names.
user-invocable: false
---

# Review checklist
The blocking findings of a review, by Area. The reviewer agent prompt owns how to gather evidence, the checks every
review makes, severity, what not to raise and the output; the delegation names the sections below to apply.
`Common` applies to every change.

### Common
- A spec with the `skip-tests` label but changed real behavior — the skip is only for deletion, config, docs or a pure move. **Behavior shipped under that skip is blocking.**
- A convention or hard rule changed without a matching update to `.claude/rules/*` or `skarbiec-plan/decisions.md`.
- A `## Test manualny` plan — kept from the spec or rewritten in `skarbiec-plan/issues/<n>.manual.md` (`manualTest`) — that names a route, screen or action absent from the code. Wording problems in it are minor.
- A migration that destroys protected data (see `decisions.md` ADR-043) without the ticket's `## Utrata danych` section with a `Zgoda:` line, or without a `## Przeniesienie danych` entry whose `proof:` test exists; an existing migration edited, deleted or renamed.
- A change that deletes computed data (Reporting snapshots and the like) with no automatic rebuild after deployment, or with no AC and test proving that rebuild.

### Backend
- A hard-rule violation: throwing (instead of returning `Result`/`Result<T>`) for an expected failure path; a Service/Repository layer, or MediatR; `UserId` sourced from the request body or route instead of JWT claims, or a user-owned entity without the global query filter; an event published outside the MassTransit outbox, or a consumer that isn't idempotent; a consumer calling the event's publisher back over REST instead of using the state the event already carries; cross-database access or a foreign key between services; `float`/`double` used for money instead of `decimal`/`Money`; a new call to an external price/FX API from anywhere other than a MarketData job or `ITickerVerifier`.
- A new user-owned resource with no tenancy isolation test.
- A model change with no migration, or a migration that doesn't match the model and entity configuration it's supposed to represent.
- The API surface changed but the generated TS client (`web/src/app/api`) wasn't regenerated, or its diff holds more than the real schema delta; a new endpoint without `requests/<service>.http` updated.

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
An e2e part carries the `e2e` label: no test-writer or implementer ran, and the e2e agent wrote `e2e/tests/<epic-slug>.spec.ts` from the epic's `## Test manualny`. Read the epic body (`skarbiec-plan/issues/<epic>.md`) and the plan the agent returned in `manualTest`. An own-plan part (`ownPlan`) is checked against its own spec's `## Test manualny`, in the `e2e/tests/*.spec.ts` file its Goal names, and its refreshed plan is `skarbiec-plan/issues/<n>.manual.md` for the part's own issue, not the epic's. Read `.git/e2e-result.json` for the run — never start the stack.
- A plan step that is neither automated as a `test.step('Krok <n>: …')` nor marked `(tylko manualnie)`.
- An assertion whose expected value differs from the plan.
- Any changed file outside `e2e/` (the gitignored plan file aside): no production file changes.
- A new `test.skip`, `test.fixme` or `waitForTimeout`; an existing e2e test weakened or deleted.
- A new `toPass` / `expect.poll` on a Reporting projection value whose body does not re-fetch (reload, or leave the screen and come back; `expectProjected` does it).
- An e2e test that does not conform to `.claude/rules/e2e.md`.
- A **repair rebuild** (the prompt names a diagnosis file) starts from the previous run's tests, saved as `skarbiec-plan/issues/<n>.e2e.patch`: compare the test diff with it. A changed expected value or an assertion taken from the plan is blocking; only how a step waits, re-fetches or selects may differ.

### Areas without a test-writer
- Scripts, Claude, Docs, CI/Infra and E2E have no test-writer. A changed production file under `services/`, `web/` or `contracts/` in such a change is `blocking`: the ticket has the wrong Area, or it must be split.
