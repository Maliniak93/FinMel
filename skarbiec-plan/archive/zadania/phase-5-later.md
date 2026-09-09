# Phase 5 — Later (no commitments)

Deliberately **not** broken into granular tasks — the roadmap marks this phase as commitment-free, and detailed planning now would be waste (scope will change once v1.0 lives in production for a while). Each area below has: scope sketch, an **entry condition** ("when is it even worth starting"), first steps, and references. When an area is picked up, write its granular task file then (`phase-5-<area>.md`) using the format from `README.md`.

Ideas that appear during Phases 0–4 land **here**, not in the current sprint (roadmap anti-scope-creep rule).

---

## 1. Migration compose → k3s

**Scope:** move production from docker compose to k3s on the same VPS (or a second one): manifests/Helm-ish structure for 7 services + PG + RabbitMQ + observability, secrets management, liveness/readiness wired to the existing health endpoints, rolling updates, ingress replacing the TLS proxy.
**Entry condition (ADR-014):** only once compose starts to hurt — deploy friction, no zero-downtime updates, config sprawl. Not before; k8s for its own sake is exactly what ADR-016 rejected. Presupposes VPS/compose deployment (T0.18, currently deferred/optional — see `04-roadmap.md`) actually exists and has been run for a while first.
**First steps:** k3s on a scratch VM; migrate one stateless service (Gateway or Notifications) end-to-end including CI image delivery; write an ADR updating ADR-014 with the actual migration plan.
**Refs:** ADR-014, ADR-016, E9 [C], `deploy/`.

## 2. Liabilities (loans, mortgage)

**Scope:** `Liability` symmetric to `Asset` in Portfolio (principal, schedule, interest rate, currency); net worth = assets − liabilities across Reporting snapshots, dashboard and history chart; Strategy metrics unaffected except where net worth is an input.
**Entry condition:** you actually hold a loan/mortgage worth tracking — the domain model says the schema is ready for it, so this is mostly additive.
**First steps:** domain-model addendum (entity + how snapshots subtract it), one vertical slice in Portfolio, snapshot algorithm extension in Reporting, dashboard sign handling (negative bars).
**Refs:** `03-domain-model.md` §future, `01-vision-and-scope.md`.

## 3. PIT-38 / FIFO

**Scope:** yearly tax helper: FIFO lot matching over sell transactions, cost basis in PLN using NBP rate of the day preceding each transaction (Polish tax rule), realized P/L per instrument, a PIT-38-shaped summary. Report, not advice — same disclaimer stance as rebalancing.
**Entry condition:** first tax year with taxable sales recorded in the app; transactions already store everything needed (ADR-009 was designed for this).
**First steps:** verify transaction data captures the tax-relevant fields for historic entries (fees, currencies, exact dates); FIFO engine as a pure, heavily-tested function in Reporting or a dedicated slice; a fixture year cross-checked against a real brokerage tax report.
**Refs:** ADR-009, `03-domain-model.md` §future, E5.

## 4. Household

**Scope:** a sharing level above `UserId`: household with members and roles, shared portfolios visible to members, per-member vs household dashboards.
**Entry condition:** a real second user in the same household wants shared visibility — don't build multi-tenancy v2 for a hypothetical.
**First steps:** ADR (this changes the tenancy model — the single most security-sensitive change possible in this system); design how the global query filter extends (UserId → membership set) in the one place it lives (T0.13 plumbing pays off here); isolation test matrix grows a dimension (member vs non-member vs role).
**Refs:** ADR-006, `03-domain-model.md` §future, `01-vision-and-scope.md` §users.

## 5. PWA / mobile

**Scope:** installable PWA: manifest, service worker, sensible offline behavior (read-only cached dashboard; no offline writes — financial data + sync conflicts are not worth it), responsive pass over the mobile-hostile views (transaction table, import wizard).
**Entry condition:** you reach for the app on a phone regularly and the browser experience annoys you.
**First steps:** Lighthouse PWA audit; Angular service-worker package on the existing app; pick the 3 most-used views for the responsive pass.
**Refs:** `01-vision-and-scope.md` §later.

## 6. Open banking (automatic account balances)

**Scope:** automatic cash/deposit balances via a PSD2 aggregator (direct bank APIs are not realistic for a solo hobby project); mapping bank accounts to Skarbiec assets; consent lifecycle.
**Entry condition:** explicitly parked — high cost and regulatory overhead for modest benefit (manual cash entry is fast). Revisit only if an aggregator offers a genuinely accessible personal/dev tier.
**First steps (if ever):** aggregator market scan (pricing, PL bank coverage, sandbox); an ADR weighing build-vs-skip with real numbers; a read-only spike against the sandbox feeding one Cash asset.
**Refs:** `01-vision-and-scope.md` §later (deliberately postponed).
