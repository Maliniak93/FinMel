---
name: new-adr
description: Append a new architecture decision record to skarbiec-plan/06-adr-decisions.md.
argument-hint: <title or decision to record>
disable-model-invocation: true
---

Record an architectural decision in `skarbiec-plan/06-adr-decisions.md`.

## When an ADR is required (not optional)

- Splitting or adding a service (ADR-001 forbids it otherwise).
- New infrastructure: Redis/cache, saga/process manager, service discovery, K8s before Phase 5 (all rejected in ADR-016 — reversing needs a new ADR).
- Violating any "Hard architecture rules" item in CLAUDE.md.
- Changing a previously accepted ADR (supersede it, don't edit it).

## Steps

1. Read `skarbiec-plan/06-adr-decisions.md`; determine the next number (ADR-017+).
2. If `$ARGUMENTS` doesn't state the decision and its rationale, ask one short question.
3. Append an entry **in English** (like everything in the repo), matching the existing short format:

   ```markdown
   ## ADR-0NN <✅|🕐|❌> <Title>
   **Context:** <why the decision is needed>
   **Decision:** <what was decided>
   **Consequences:** <costs and effects, consciously accepted>
   ```

   Status: ✅ accepted / 🕐 pending / ❌ rejected. Omit **Context** only when trivially obvious (some existing entries do).
4. If the ADR supersedes an earlier one, add a note to the old entry: `Superseded by ADR-0NN.` — never rewrite its content.
5. If the decision changes a hard rule or convention, update `CLAUDE.md` / the relevant rule in `.claude/rules/` in the same change.
