# Task breakdown per phase

Granular, executable tasks for each roadmap phase. Source of truth for *what* to build stays in `../01`–`06`; these files only break it down. If a task contradicts an ADR, the ADR wins — fix the task.

## Files

| File | Phase | Duration (roadmap) |
|---|---|---|
| `phase-0-platform.md` | Platform: monorepo, Aspire, Identity, messaging, Gateway, CI/deploy | 4–5 weeks (hard limit 5) |
| `phase-1-mvp.md` | MVP: wealth entered manually (Portfolio + Angular) | 5–7 weeks |
| `phase-2-market-data.md` | Market data + CQRS (MarketData, Reporting, observability) | 4–6 weeks |
| `phase-3-strategy.md` | Strategy: allocation, rebalancing, emergency fund, goals → **v1.0** | 4–5 weeks |
| `phase-4-hardening.md` | Notifications, CSV import/export, annual report, hardening | 3–4 weeks |
| `phase-5-later.md` | No commitments — outline only | — |

## Task format

```markdown
### T<phase>.<n> <Area>: <title>

- [ ] **Status:** todo · **Size:** M
- **Depends on:** T0.2, T0.3        ← task IDs that must be done first
- **Refs:** E1, ADR-005, ADR-006    ← backlog epics / ADRs this implements

**Goal:** one sentence — what exists when this is done.

**Scope:**
- concrete steps, files, decisions

**Acceptance criteria:**
- [ ] verifiable outcomes (HTTP codes, tests green, trace visible…)
```

## Definition of done (every slice, from T0.14 onward)

- **Architecture test:** a NetArchTest guardrail per service asserting every user-owned entity implements `IUserOwned` (tenancy-filtered services only — decision documented per service in `ArchitectureTests.cs`) and that no request record binds `UserId` from the body (ADR-006).
- **Tenancy isolation test:** reuse the `Skarbiec.Testing.Tenancy.TenancyIsolationTests<TProgram>` template (T0.14) per resource type — user A creates, user B: GET/PUT/DELETE → 404, LIST → empty.

## Conventions

- **Status:** `todo` → `in progress` → `done` (edit the text and tick the checkbox). Abandoned scope: `dropped` + one line why.
- **Size** (rough, at ~8–10 h/week): **S** ≈ 1–2 h · **M** ≈ 3–5 h (half a day) · **L** ≈ 6–10 h (1–2 evenings). A task growing past L should be split.
- **IDs are stable** — never renumber; append new tasks at the end of the relevant section.
- **Priorities:** tasks are [M]ust by default; tasks realizing a [S]hould/[C]ould backlog story are marked in the title.
- **Order** follows the roadmap tip: build a *path through the system* first (end-to-end through the Gateway down to the database), feature breadth second.
- **Every phase ends with a deployment** — each file closes with an exit checklist including deploy + smoke test. **Currently relaxed:** development is local-only (Aspire) for the time being, so each phase instead closes with a **local smoke test** (same flow, run under Aspire + `npm start`) as the required gate; the VPS/production half is optional and deferred.
- **`[VPS]` tag:** tasks whose scope is docker-compose/VPS provisioning, or that require an actual production deployment (T0.18, T1.14, T2.15, and the VPS half of every phase-exit task), are marked `[VPS]` and are optional/deferred while work stays local-only. Nothing later in the plan hard-depends on them anymore — pick them all up together whenever VPS deployment is actually prioritized.
