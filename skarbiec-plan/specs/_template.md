---
title: <short feature name>
status: draft
tier: 1
branch: feat/<slug>
created: YYYY-MM-DD
---

<!-- Tier 1 = well-scoped, mechanical, low ambiguity. Tier 2 = changes the data model or event contracts, or AC must close a real behavioral gap — see workflow.md. -->
<!-- status moves draft -> approved (you) -> building -> done (ops fills Result). /build only accepts status: approved. -->
<!-- Definition of Ready = approved + every AC has a test/command + tier set + Risks/open questions empty. -->
<!-- Keep this file under ~200 lines: link to architecture.md/domain.md sections instead of restating them. -->
<!-- Out of scope is not optional — the fastest way to blow a Tier 1 budget is an unstated scope creep the implementer feels obligated to fix. -->
<!-- Every AC must be provable by a command or a named test the reviewer can actually run — "looks right" is not an AC. -->

## Goal

<!-- One sentence: what exists when this spec is done. -->

## Why

<!-- The problem or gap this closes. Link the architecture.md "Current vs target" row or ideas.md line this comes from, if any. -->

## Scope

### Backend

<!-- Service(s), slices, entities, events, migrations touched. -->

### Frontend

<!-- Routes, components, gen:api impact. -->

## Out of scope

<!-- Explicitly not touched by this spec, even if related. -->

## Design decisions

<!-- Points worth recording; reference ADRs by number. Draft a new ADR here if this spec establishes a hard rule. -->

## Data / API changes

| Entity / endpoint | Change | Migration? |
|---|---|---|
| | | |

## Acceptance criteria

<!-- Numbered; each one Given/When/Then, with a proof the reviewer can run. -->

1. Given ___, when ___, then ___. — proof: `<test name or command>`

## Verification

<!-- Commands to run, plus manual smoke steps in the browser if this touches the UI. -->

## Risks / open questions

<!-- Must be empty before status: approved. -->

## Result

<!-- Filled in by ops after Ship: PR URL, final status. -->
