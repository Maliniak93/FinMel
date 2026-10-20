---
paths:
  - "e2e/**"
  - "scripts/e2e.mjs"
  - "deploy/compose/compose.e2e.yaml"
---

# e2e conventions (Playwright, ADR-034)

Browser tests live in `e2e/` and run only through `node scripts/e2e.mjs run`. Modes, traces and the data set: `e2e/README.md`, `e2e/market-data/README.md`.

## The stack

- A second compose project `skarbiec-e2e` (`deploy/compose/compose.yaml` + `compose.e2e.yaml`): images `skarbiec-e2e/<svc>` built from the working tree, web `:18080`, market stub `:18081`, dashboard `:18898`, Postgres and RabbitMQ unpublished on tmpfs, `down --volumes` after every run.
- **Agent exception:** an agent may start and stop this project, and only through `node scripts/e2e.mjs`. The user's `skarbiec-local` stack, `compose.mjs` and Aspire stay the user's; `e2e.mjs` never retags `skarbiec-local/*`. In a build run only the `e2e` agent of an epic's e2e part starts it; the implementer and the test-writer never do. `node scripts/e2e.mjs check` is the read-only gate `ship.mjs` uses: a green whole-suite run (no `--grep`) of the exact working tree.
- MarketData runs unchanged and reads the stub through `Yahoo:BaseUrl`, `Nbp:BaseUrl` and `BondCatalog:PageUrl`. The Gateway limits are raised through `RateLimiting:*:PermitLimit`. Both only in the e2e override.

## Writing tests

- Chromium only, `workers: 1`, no parallelism: tests share one database.
- Every test gets a fresh user (`user` fixture, unique e-mail), so tests never depend on each other or on leftovers. Language is `pl` (`skarbiec-lang`, set before the first page load); assert Polish text and `pl-PL` money (`10 000,00 zł`, match the spaces with `\s`).
- Locate by role or label. A `data-testid` goes into the app only when no stable role or label exists.
- Assertions wait: web-first `expect`, or `expect(async () => …).toPass()` for an eventually consistent value. **`waitForTimeout` is never used** (`grep -rn waitForTimeout e2e/` must stay empty).
- A value that comes from a Reporting projection (dashboard, portfolio totals, valuations) is asserted with `expectProjected(refetch, assertion)` from `e2e/tests/projection.ts` (30 s, re-fetches before every attempt). A `toPass` / `expect.poll` on such a value whose body does not re-fetch (reload, or leave the screen and come back) is forbidden: it polls a stale screen.
- One `test.step` per user action, so a failure names the step.
- Expected amounts come from `e2e/market-data/` and are never recomputed in a test.
- A change to a provider payload shape updates the stub and the data set README in the same change.

## Failure classes (`.git/e2e-result.json`, `class`)

| class | exit | meaning | action |
| --- | --- | --- | --- |
| `pass` | 0 | all tests green (a flaky test is listed, not failed) | none |
| `fail` | 1 | a test failed after its retries; `failed[]` names the test, the failing step and the trace | fix the app or the test; read `e2e/test-results/stack.log` for the backend; before blocking as `app`, the e2e agent runs the 30 s `expectProjected` self-check |
| `environment` | 3 | Docker unreachable, a port taken, stack not healthy in 10 minutes, no Playwright output, or the 45-minute deadline | not a code defect: fix the machine and rerun; never edit tests for it |

## Hygiene

- `verify.mjs` checks only `e2e/` types and formatting (`npm run typecheck`, `npm run format:check` in `e2e/`); it never starts Playwright or the stack.
- `e2e/test-results/` and `e2e/playwright-report/` are gitignored output; never commit them.
