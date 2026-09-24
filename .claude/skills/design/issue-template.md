<!-- Body of a spec issue. Tier, Kind, Branch and Status live in project fields and skip-tests is a label, so none of them is repeated here. Delete every HTML comment before publishing. -->
<!-- Keep it under ~200 lines: link to architecture.md/domain.md sections instead of restating them. -->

## Goal

<!-- One sentence: what exists when this spec is done. -->

## Current behaviour

<!-- Change/fix only (delete the section for new behaviour): what the code does today, from what Explore actually found — the slices, entities, events, components and tests that own it. Then one line: what it should do instead. -->

## Why

<!-- The problem or gap this closes. Link the architecture.md "Current vs target" row or ideas.md line this comes from, if any. -->

## Scope

### Backend

<!-- Service(s), slices, entities, events, migrations touched. -->

### Frontend

<!-- Routes, components, gen:api impact. -->

## Out of scope

<!-- Explicitly not touched by this spec, even if related. Not optional. -->

## Design decisions

<!-- Points worth recording; reference ADRs by number. -->

## Data / API changes

| Entity / endpoint | Change | Migration? |
|---|---|---|
| | | |

## Acceptance criteria

<!-- Checkboxes so the project card shows progress. Each one Given/When/Then with a proof the reviewer can run. -->

- [ ] **AC-1** Given ___, when ___, then ___. — proof: `<test name or command>`

## Verification

<!-- Commands to run, plus manual smoke steps in the browser if this touches the UI. With skip-tests: "No new tests: the gate is <commands> plus the existing suites". -->
