# Phase 3 — Strategy (→ v1.0)

**Duration:** 4–5 weeks. Ends with **version 1.0**.
**Goal:** the application advises: target allocation with drift detection, amount-based rebalancing (including the "where should I deposit X PLN" mode), emergency fund coverage, savings goals. One route (Strategy→Portfolio) moves to gRPC as a deliberate exercise.
**Deliverable (from roadmap):** the application advises. Version 1.0.
**Sources:** roadmap Phase 3, E6/E7, ADR-015 (gRPC on one route), `03-domain-model.md` §Strategy.

## Task index

| ID | Task | Size | Depends on |
|---|---|---|---|
| T3.1 | `TargetAllocation` CRUD | M | Phase 2 |
| T3.2 | Allocation deviations | L | T3.1, T3.5 |
| T3.3 | Rebalancing suggestions (sell/buy amounts) | L | T3.2 |
| T3.4 | "Depositing X PLN — where?" mode | M | T3.3 |
| T3.5 | gRPC Strategy→Portfolio | L | Phase 2 |
| T3.6 | `EmergencyFund` + coverage | M | T3.5 |
| T3.7 | `SavingsGoal` + required contribution | M | Phase 2 |
| T3.8 | Angular: allocation editor + deviations | L | T3.1, T3.2 |
| T3.9 | Angular: rebalancing panel | M | T3.3, T3.4 |
| T3.10 | Angular: emergency fund + goals | M | T3.6, T3.7 |
| T3.11 | Tenancy isolation tests in Strategy | S | T3.1–T3.7 |
| T3.12 | Phase exit: local smoke v1.0 (+ optional VPS deploy) | S | T3.1–T3.11 |

All Phase 3 features are backlog priority **[S]** (v1.0), except where noted.

---

### T3.1 Strategy: `TargetAllocation` CRUD [S]

- [ ] **Status:** todo · **Size:** M
- **Depends on:** Phase 2
- **Refs:** E6 [S], `03-domain-model.md` §Strategy, invariant "sums to 100"

**Goal:** users define a target allocation — per portfolio or for total wealth — as asset-class percentages with tolerance bands.

**Scope:**
- Slices: `CreateTargetAllocation`, `UpdateTargetAllocation`, `GetTargetAllocation`, `DeleteTargetAllocation`.
- Entity: scope (specific `PortfolioId` — a Guid ref, no FK — or `Total`), positions: `AssetClass` → `TargetPercent` + `ToleranceBand` (± percentage points, e.g. 60% ±5 pp).
- Invariants: positions sum to exactly 100 (validator; mind decimal comparison), no duplicate asset class, percent > 0, band ≥ 0; one allocation per scope per user (unique) — updating replaces positions atomically.
- Tenancy: `UserId` from claims, filter + interceptor as everywhere (T0.13 plumbing).

**Acceptance criteria:**
- [ ] Sum ≠ 100 → 400 ProblemDetails naming the actual sum.
- [ ] One-per-scope enforced (second create → 409).
- [ ] Integration tests for CRUD + invariants.

---

### T3.2 Strategy: allocation deviations [S]

- [ ] **Status:** todo · **Size:** L
- **Depends on:** T3.1, T3.5
- **Refs:** E6 [S] (deviations, color outside band), ADR-015

**Goal:** for a given allocation scope, Strategy computes the current per-class allocation and its deviation from target, flagging positions outside the tolerance band.

**Scope:**
- Slice `GetAllocationDeviations`: current positions via the gRPC client (T3.5) from Portfolio + current prices/FX from MarketData (REST) → current value per asset class → current % → deviation vs target (pp) → `withinBand` flag. Computed on demand, not persisted (per domain model).
- Classes present in the portfolio but absent from the target (and vice versa) handled explicitly (treated as target 0% / current 0% — document).
- Response includes the valuation timestamp/prices-as-of date (honesty about eventual consistency).
- Pure computation isolated in a testable function (input: positions+prices+target; output: deviations) — no I/O in the math.

**Acceptance criteria:**
- [ ] Table-driven unit tests on the pure function: in-band, out-of-band, missing classes, zero-value portfolio.
- [ ] Integration test with fake Portfolio/MarketData responses.
- [ ] MarketData/Portfolio unavailable → 503 ProblemDetails (resilience defaults, fail closed).

---

### T3.3 Strategy: rebalancing suggestions [S]

- [ ] **Status:** todo · **Size:** L
- **Depends on:** T3.2
- **Refs:** E6 [S] ("sell X for N PLN, buy Y for M PLN"; disclaimer), `01-vision-and-scope.md` §out of scope (no advice)

**Goal:** amount-based suggestions that would bring the allocation back to target: "sell class X for N PLN, buy class Y for M PLN".

**Scope:**
- Slice `GetRebalancingSuggestions`: from deviations (T3.2 internals reused), compute per-class PLN amounts to reach target; suggestions at **asset-class level** (not individual instruments — keep v1 honest and simple; document).
- Rounding policy (nearest 100 PLN or configurable threshold — decide, document); suppress noise suggestions below a minimum amount.
- Every response carries the disclaimer text (information, not investment advice — vision §out of scope); wording stored server-side so it's consistent everywhere.
- Not persisted (per domain model — computed on demand).

**Acceptance criteria:**
- [ ] Unit tests: amounts sum to ~0 (sells fund buys), below-threshold suppression, already-balanced → empty list.
- [ ] Disclaimer present in the payload (asserted).
- [ ] Suggestions consistent with T3.2 deviations for the same inputs.

---

### T3.4 Strategy: "I'm depositing X PLN — where should it go?" [S]

- [ ] **Status:** todo · **Size:** M
- **Depends on:** T3.3
- **Refs:** E6 [S] (rebalancing with contributions, no selling), `03-domain-model.md` (prefer contributions — lower tax cost)

**Goal:** given a contribution amount, suggest a buy-only split that moves the allocation toward target without selling anything.

**Scope:**
- Slice `GetContributionPlan` (input: amount, scope): allocate the new money to underweight classes first (water-filling toward target), never negative amounts; if the contribution can't fully fix the drift, get as close as possible and say so.
- Edge cases: amount larger than needed to balance → remainder split per target weights; empty portfolio → split entirely per target.
- Same disclaimer and rounding policy as T3.3 (shared code).

**Acceptance criteria:**
- [ ] Unit tests: exact-fix amount, insufficient amount (partial fix, most-underweight first), oversized amount, empty portfolio.
- [ ] No negative (sell) positions ever in the output (property-style assertion).
- [ ] Sum of the split == input amount (after rounding policy, difference ≤ rounding unit).

---

### T3.5 Platform: gRPC Strategy→Portfolio (exercise)

- [ ] **Status:** todo · **Size:** L
- **Depends on:** Phase 2
- **Refs:** E9 [S] (gRPC on this route), ADR-015, `02-architecture.md` §communication

**Goal:** the Strategy→Portfolio positions query runs over gRPC; every other route stays REST — a deliberate, contained learning exercise.

**Scope:**
- `positions.proto` (contract: get positions-for-valuation per user/portfolio — mirrors what Reporting uses over REST; **note**: `decimal` has no proto scalar — model money as string or units+nanos, decide and document in the proto comments).
- Proto file location shared in the monorepo (e.g. under `contracts/` — same discipline as `Skarbiec.Contracts`: edited in place while ADR-019 holds; field numbers may be reused since there is no deployed consumer).
- gRPC server in Portfolio (Grpc.AspNetCore alongside Minimal APIs), auth on the call (same JWT/service-auth approach as T2.11 decided), OTel instrumentation (grpc client/server spans in traces).
- Typed gRPC client in Strategy with resilience (deadline, retry policy where safe).
- Aspire + compose wiring (HTTP/2; TLS in production via the internal network — document how).
- REST fallback **not** built — this route is gRPC now (keep the exercise honest); Reporting keeps REST (per plan).

**Acceptance criteria:**
- [ ] Strategy fetches positions over gRPC locally under Aspire and in production compose.
- [ ] Trace shows the gRPC client→server spans within a deviations request.
- [ ] Deadline exceeded / Portfolio down → 503 ProblemDetails from Strategy (not a hang).
- [ ] Money precision survives a round-trip (test with awkward decimals, e.g. 0.123456789).

---

### T3.6 Strategy: `EmergencyFund` + coverage metric [S]

- [ ] **Status:** todo · **Size:** M
- **Depends on:** T3.5
- **Refs:** E7 [S], `03-domain-model.md` §EmergencyFund

**Goal:** users configure the emergency fund (monthly expenses × target months, designated assets) and see % coverage.

**Scope:**
- Slices: `ConfigureEmergencyFund` (upsert — one per user), `GetEmergencyFundStatus`.
- Entity: `UserId`, monthly expenses (Money PLN), target months (e.g. 6), designated asset ids (join table `emergency_fund_asset`; asset ids are Portfolio refs — no FK).
- Status computation: current value of designated assets (positions via gRPC T3.5, prices via MarketData) ÷ (expenses × months) → % coverage; list stale/missing designated assets (asset deleted in Portfolio → flag it, don't crash — cross-service consistency is API-level, ADR-003).
- Validation: expenses > 0, months ≥ 1, designated assets exist at configuration time (validated via Portfolio call).

**Acceptance criteria:**
- [ ] Coverage math unit-tested (0%, partial, >100%).
- [ ] A designated asset later deleted in Portfolio → status flags it and excludes it (integration test with fake).
- [ ] Upsert semantics: reconfiguring replaces cleanly.

---

### T3.7 Strategy: `SavingsGoal` + required monthly contribution [S]

- [ ] **Status:** todo · **Size:** M
- **Depends on:** Phase 2
- **Refs:** E7 [S], `03-domain-model.md` §SavingsGoal

**Goal:** users create savings goals (amount, deadline, linked portfolio/assets) and see progress % plus the required monthly contribution at an assumed rate of return.

**Scope:**
- Slices: `CreateSavingsGoal`, `UpdateSavingsGoal`, `ListSavingsGoals`, `DeleteSavingsGoal`, `GetSavingsGoalStatus`.
- Entity: name, target amount (Money), deadline (`DateOnly`), linked portfolio or asset ids (refs, no FK), assumed annual return rate (user-provided, default 0%).
- Status: current linked value (same valuation path as T3.6) → progress %; required monthly contribution from the annuity formula (future value of current amount + monthly contributions at rate r over remaining months = target) — pure function, unit-tested; deadline passed → state "overdue", no division-by-zero.
- Validation: target > 0, deadline in the future (on create), rate within sane bounds (e.g. −20%…+30%/yr — document).

**Acceptance criteria:**
- [ ] Annuity math verified against hand-computed cases (r = 0 edge case included).
- [ ] Deadline in the past on create → 400; goal turning overdue over time handled (status test).
- [ ] Tenancy + CRUD integration tests.

---

### T3.8 Angular: allocation editor + deviations view [S]

- [ ] **Status:** todo · **Size:** L
- **Depends on:** T3.1, T3.2, T1.8
- **Refs:** E6 [S]

**Goal:** UI to define target allocations and see current vs target with band-aware coloring.

**Scope:**
- Regenerate TS clients. Allocation editor: scope picker (total / portfolio), rows of asset class + target % + tolerance band; live sum indicator (must hit 100 to enable save — server remains authoritative); percent inputs with sensible steps.
- Deviations view: per class — target %, current %, deviation in pp; color state: within band / outside band (E6 AC); "prices as of" timestamp shown; bar or bullet chart (simplest honest visual).
- Empty states: no allocation yet → CTA to create one.

**Acceptance criteria:**
- [ ] Creating/editing an allocation round-trips; sum≠100 blocked client-side and, if forced, rendered from the server 400.
- [ ] Out-of-band classes visibly colored; timestamp visible.
- [ ] Works for both scopes (total + single portfolio).

---

### T3.9 Angular: rebalancing panel (both modes) [S]

- [ ] **Status:** todo · **Size:** M
- **Depends on:** T3.3, T3.4, T3.8
- **Refs:** E6 [S]

**Goal:** one panel showing sell/buy rebalancing suggestions and the contribution-mode planner.

**Scope:**
- Tab/toggle: "Rebalance" (list of sell X → buy Y amounts from T3.3) and "I'm depositing…" (amount input → split from T3.4).
- Disclaimer rendered prominently (from the server payload — not hardcoded in the UI).
- Already-balanced → positive empty state ("nothing to do"); amounts formatted as PLN.

**Acceptance criteria:**
- [ ] Both modes render real backend data; deposit input recalculates on change (debounced).
- [ ] Disclaimer visible in both modes.
- [ ] Balanced portfolio shows the empty state, not zeros.

---

### T3.10 Angular: emergency fund widget + goals [S]

- [ ] **Status:** todo · **Size:** M
- **Depends on:** T3.6, T3.7, T3.8
- **Refs:** E7 [S] (coverage on the dashboard)

**Goal:** emergency fund configuration + dashboard coverage widget; savings goals list with progress.

**Scope:**
- Emergency fund settings page: expenses, target months, designated-assets multiselect (from user's assets); dashboard widget: % coverage gauge/bar + amount vs target (E7 AC: coverage on dashboard).
- Goals page: list (name, progress bar, target, deadline, required monthly contribution), create/edit form; overdue goals visually distinct.
- Flagged/removed designated assets surfaced ("an asset in your fund no longer exists").

**Acceptance criteria:**
- [ ] Coverage widget on the dashboard matches `GetEmergencyFundStatus`.
- [ ] Goal create → progress and required contribution shown; overdue state rendered.
- [ ] Designated-asset drift (deleted asset) message appears when simulated.

---

### T3.11 Quality: tenancy isolation tests in Strategy

- [ ] **Status:** todo · **Size:** S
- **Depends on:** T3.1–T3.7, T0.14
- **Refs:** ADR-006

**Goal:** the standard isolation matrix over Strategy's resources.

**Scope:**
- T0.14 template over: target allocations, emergency fund, savings goals — user B: GET/PUT/DELETE → 404, LIST → empty.
- Extra: deviations/rebalancing endpoints scoped to the caller's data only (B requesting with A's portfolio id in scope → 404).

**Acceptance criteria:**
- [ ] Isolation matrix green in CI; part of Strategy's DoD.

---

### T3.12 Phase exit: local smoke v1.0 (+ optional VPS deploy)

- [ ] **Status:** todo · **Size:** S
- **Depends on:** T3.1–T3.11
- **Refs:** roadmap Phase 3 deliverable

**Goal:** version 1.0's feature set is demonstrably complete locally; deploying and tagging it in production stays optional until T0.18 is picked up.

**Scope:**
- **Local (required now):** smoke locally under Aspire: define allocation → see deviations → get rebalancing suggestions → contribution mode → configure emergency fund → create a goal; verify the gRPC route works locally (trace in the Aspire dashboard). Short v1.0 note in the phase log: what the app can do now vs `01-vision-and-scope.md` §Version 1.0 (CSV import remains — Phase 4).
- **VPS / production (optional — deferred, needs T0.18):** deploy; tag the release `v1.0.0`; repeat the smoke in production (trace); restore test for this phase (per T1.14 policy, now includes `strategy_db`).

**Acceptance criteria:**
- [ ] Local smoke green (allocation → deviations → rebalancing → contribution mode → emergency fund → goal).
- [ ] gRPC Strategy→Portfolio confirmed via a local trace.
- [ ] *(optional, deferred)* Production smoke green; `v1.0.0` tagged; restore test done this phase — once T0.18 lands.

---

## Exit checklist (Phase 3 done when…)

- [ ] All tasks done (whole phase is v1.0 scope).
- [ ] Local smoke green; v1.0 feature set complete locally (T3.12).
- [ ] Tenancy isolation green in Strategy.
- [ ] Disclaimer present on every advisory surface.
- [ ] *(optional, deferred)* v1.0 deployed and tagged in production; restore test executed — pick up together with T0.18.
