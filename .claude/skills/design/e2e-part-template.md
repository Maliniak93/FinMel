<!-- Body of an epic's e2e part: always the LAST part, created with `create --e2e --parent <epic> --tier 1 --kind new`. Fill the <> placeholders only; the four ACs are fixed. It has no `## Test manualny` of its own: the plan lives in the epic and is what this part automates. Delete every HTML comment before publishing. -->

## Goal

The whole `## Test manualny` of epic #<epic> runs as Playwright tests (`e2e/tests/<epic-slug>.spec.ts`) against the isolated `skarbiec-e2e` stack, and the whole e2e suite is green.

## Why

See the epic: after its parts merge, nothing has used the feature the way the user will. This part automates the plan 1:1, refreshes it against what was built and runs the full suite as a regression check.

## Depends on

- #<previous part> — the last feature part of the epic: its change must be on master before the plan can be automated.

## Scope

### e2e

- `e2e/tests/<epic-slug>.spec.ts`: one test per scenario of the epic plan, one `test.step('Krok <n>: …')` per plan step, asserting the plan's expected values. A step that cannot be automated is marked `(tylko manualnie)` in the plan.
- The epic's `## Test manualny` refreshed against the built feature (written by the e2e agent, replaced on the epic when the part ships).
- No production file, no harness change: a stable app failure blocks the run and is fixed with `/fix`.

## Code map

- `e2e/tests/fixtures.ts` — the `user` / `signedInPage` fixtures to build on.
- `e2e/tests/smoke.spec.ts` — the precedent: Polish UI, role and label selectors, `toPass` for eventually consistent values.
- `e2e/market-data/README.md` — the fixed data set the expected amounts come from.
- `e2e/tests/<epic-slug>.spec.ts` (new) — this part's tests.

## Out of scope

- Any change outside `e2e/` and the epic's plan: app code, services, the stub, the data set, the harness.
- Scenarios beyond the epic's `## Test manualny`.
- e2e in CI.

## Acceptance criteria

- [ ] **AC-1** Given the epic's `## Test manualny`, when the part is built, then every plan step is automated as a `Krok <n>` step in `e2e/tests/<epic-slug>.spec.ts` or marked `(tylko manualnie)`. — proof: `grep -n "Krok" e2e/tests/<epic-slug>.spec.ts` and the refreshed plan in `skarbiec-plan/issues/<epic>.manual.md`
- [ ] **AC-2** Given the isolated stack, when `node scripts/e2e.mjs run` runs, then the whole suite is green. — proof: `node scripts/e2e.mjs run` (`.git/e2e-result.json`: `"ok": true`)
- [ ] **AC-3** Given the epic, when the part ships, then its `## Test manualny` is replaced by the refreshed plan. — proof: the `Test manualny zaktualizowany w #<epic>` line of the run report
- [ ] **AC-4** Given the part's diff, when it is reviewed, then no file outside `e2e/` changed. — proof: `node scripts/review-diff.mjs --stat`

## Verification

`node scripts/verify.mjs --projects e2e` and `node scripts/e2e.mjs run` (Docker running). No new unit tests: the e2e tests are the deliverable.

<!-- OWN-PLAN VARIANT: for an e2e part of a coverage epic. Use this block instead of the one above; delete everything above this comment and every HTML comment before publishing. Same `--e2e --parent <epic> --tier 1 --kind new`. The part has its own numbered `## Test manualny` (prepare then sets `ownPlan`), AC-1 and AC-3 point at the part's own plan. -->

## Goal

The `## Test manualny` of this part runs as Playwright tests (`e2e/tests/<area>.spec.ts`) against the isolated `skarbiec-e2e` stack, and the whole e2e suite is green.

## Why

See the epic: one own-plan part per area, so a failure blocks only its area.

## Depends on

- #<previous part> — its change (or the app state it covers) must be on master first.

## Scope

### e2e

- `e2e/tests/<area>.spec.ts`: one test per scenario of this part's plan, one `test.step('Krok <n>: …')` per plan step, asserting the plan's expected values. A step that cannot be automated is marked `(tylko manualnie)` in the plan.
- This part's `## Test manualny` refreshed against the built app (written by the e2e agent, replaced on this issue when the part ships).
- No production file, no harness change: a stable app failure blocks the run and is fixed with `/fix`.

## Code map

- `e2e/tests/fixtures.ts` — the `user` / `signedInPage` fixtures to build on.
- `e2e/tests/smoke.spec.ts` — the precedent: Polish UI, role and label selectors, `toPass` for eventually consistent values.
- `e2e/market-data/README.md` — the fixed data set the expected amounts come from.
- `e2e/tests/<area>.spec.ts` (new) — this part's tests.

## Out of scope

- Any change outside `e2e/` and this part's plan: app code, services, the stub, the data set, the harness.
- Scenarios beyond this part's `## Test manualny`.
- e2e in CI.

## Acceptance criteria

- [ ] **AC-1** Given this part's `## Test manualny`, when the part is built, then every plan step is automated as a `Krok <n>` step in `e2e/tests/<area>.spec.ts` or marked `(tylko manualnie)`. — proof: `grep -n "Krok" e2e/tests/<area>.spec.ts` and the refreshed plan in `skarbiec-plan/issues/<n>.manual.md`
- [ ] **AC-2** Given the isolated stack, when `node scripts/e2e.mjs run` runs, then the whole suite is green. — proof: `node scripts/e2e.mjs run` (`.git/e2e-result.json`: `"ok": true`)
- [ ] **AC-3** Given this part, when it ships, then its own `## Test manualny` is replaced by the refreshed plan. — proof: the `Test manualny zaktualizowany w #<n>` line of the run report
- [ ] **AC-4** Given the part's diff, when it is reviewed, then no file outside `e2e/` changed. — proof: `node scripts/review-diff.mjs --stat`

## Test manualny

1. <numbered steps in Polish; routes, screens and actions that exist in the app>

## Verification

`node scripts/verify.mjs --projects e2e` and `node scripts/e2e.mjs run` (Docker running). No new unit tests: the e2e tests are the deliverable.
