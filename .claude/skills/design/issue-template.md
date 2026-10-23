<!-- Body of a spec issue. Tier, Kind, Branch and Status live in project fields and skip-tests is a label, so none of them is repeated here. Delete every HTML comment before publishing. -->
<!-- Keep it under ~200 lines: link to architecture.md/domain.md sections instead of restating them. -->

## Goal

<!-- One sentence: what exists when this spec is done. -->

## Current behaviour

<!-- Change/fix only (delete the section for new behaviour): what the code does today, from what Explore actually found — the slices, entities, events, components and tests that own it. Then one line: what it should do instead. -->

## Why

<!-- The problem or gap this closes. Link the architecture.md "Current vs target" row or ideas.md line this comes from, if any. -->

## Depends on

<!-- Only when this spec cannot build before another open issue merges: one line each, `- #128 — what it needs from it`. `prepare` refuses to build while any listed issue is open. Delete the section otherwise. -->

## Scope

### Backend

<!-- Service(s), slices, entities, events, migrations touched. -->

### Frontend

<!-- Routes, components, gen:api impact. -->

## Code map

<!-- What Explore found, so no build agent has to rediscover it: one line each, `path:start-end — why` or `path — Member()`. Name the precedent slice/component to copy, the test class and the Fixtures/<Service>Api helpers to extend (by member name), and the files expected to change. 5–15 lines; paths that exist today, plus new files marked (new). -->

## Out of scope

<!-- Explicitly not touched by this spec, even if related. Not optional. -->

## Design decisions

<!-- Points worth recording; reference ADRs by number. -->

## Data / API changes

| Entity / endpoint | Change | Migration? |
|---|---|---|
| | | |

<!-- Only when a migration touches protected data (ADR-043); otherwise delete both sections. Przeniesienie danych: one line per moved item, `- Portfolio.Assets.Quantity … proof: Class.Method`, the method being a migration test on a database holding data from the previous version. Utrata danych: only with the user's explicit yes in /design — one line per destroyed item (what is lost, why it cannot be avoided), then `Zgoda: <date> — <the user's answer>`. Deleting computed data (Reporting snapshots and the like) needs neither: an AC and a test for the automatic rebuild after deployment. -->

## Przeniesienie danych

## Utrata danych

## Acceptance criteria

<!-- Checkboxes so the project card shows progress. Each one Given/When/Then with a proof the reviewer can run. -->

- [ ] **AC-1** Given ___, when ___, then ___. — proof: `<test name or command>`

## Test manualny

<!-- The steps a person runs by hand after the merge. Skip the section (delete it) when the change is not visible in the app: skip-tests, cleanup, workflow specs. About 5–15 steps. Describe behaviour, not exact labels, unless the label is a criterion. Standard preparation: skarbiec-plan/workflow.md → Manual test plan. Shape: a numbered **Przygotowanie** block (the standard preparation plus what this change adds), then numbered **Kroki** on concrete PLN amounts, dates, tickers and routes, each ending in `→ <expected result>`. Write the steps in Polish. -->

## Verification

<!-- Commands to run and the proof each AC names. The manual steps for a person are in ## Test manualny above. With skip-tests: "No new tests: the gate is <commands> plus the existing suites". -->
