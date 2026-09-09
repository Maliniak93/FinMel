# Skarbiec — planning documentation

Personal wealth-management web app and a deliberate microservices learning project (.NET 10 + Angular 22 + PostgreSQL + RabbitMQ). This folder is tracked in the repo and is the single current source of truth for product, architecture, decisions and workflow — superseded material lives in `archive/`, which agents don't read.

## How we work

1. **`/design <idea>`** — interview, writes `specs/<slug>.md` from `specs/_template.md`, stops at `status: draft`. You review it and flip it to `status: approved`.
2. **`/build <spec> [--tier 1|2]`** — runs Tests → Implement → Verify → Review → Ship on an approved spec, opens a PR against `master`. **You merge it** — no agent ever merges.
3. **`/fix <bug>`** — reproduces the bug, finds the root cause, writes `specs/fix-<slug>.md` with a reproduction test as its acceptance criterion, and after your approval runs the same pipeline as `/build`.
4. **`/check`** — quick repo-wide verification (`node scripts/verify.mjs`) with a five-line summary.
5. **`/ops <task>`** — CI, dependabot, runner, branch cleanup.

Full detail, agent roles and the pipeline diagram: `workflow.md`.

## Status

| | |
|---|---|
| **Done** | Old-plan Phases 0–2 (2026-07-27 → 2026-08-10): Identity, Portfolio, MarketData, a Strategy skeleton, and Reporting on `master`. Modification set M1 (M1.3–M1.10) on branch `praca_2026-08-12`: explicit `AssetValuationMode` (three modes), `SupportedCurrencies` PLN/EUR/USD, ticker verification (ADR-018), FX sync for every supported currency, a rebuilt asset dialog, dark theme. |
| **Next** | Merge `praca_2026-08-12` → `master`, then `/build` spec-00 … spec-06 in dependency order: `spec-00`/`spec-01` first (independent), then `spec-02`, then `spec-03`/`spec-04` (both depend on `spec-02`), `spec-05` independently, `spec-06` last. See `architecture.md` → "Current vs target" for what each spec closes. |
| **Open loops** | `praca_2026-08-12` not yet merged to `master` · an `M1.11` branch is work-in-progress and not folded into `praca_2026-08-12` · worktree branch `worktree-marketdata-currency-redesign` (currency catalog + `FxSyncJob`) is superseded by `spec-04` — safe to delete once that spec merges · dependabot PRs proposing MassTransit v9 should be closed, not merged, per ADR-012 (v8 is pinned; v9 is commercially licensed) |

## Documents

| File | Content |
|---|---|
| `product.md` | vision, problem, users, features (delivered / next / later), MVP success criteria, out of scope |
| `architecture.md` | services, communication rules, events, current-vs-target, diagrams (C4, container, 4 sequences) |
| `domain.md` | per-service data model, one ER diagram per service, valuation algorithm, invariants, simplifications |
| `decisions.md` | ADR-001…024 |
| `workflow.md` | agent roles, the `/design` → `/build` pipeline, Definition of Ready/Done, tiers, budgets, git conventions |
| `ideas.md` | future features and platform work, one line each, grouped and tiered |
| `runbooks/local-dev.md` | running the stack, seeds, database resets, manual sync, `.http` files |
| `runbooks/ci.md` | self-hosted runner, path-filtered jobs, dependabot rules |
| `runbooks/troubleshooting.md` | known failure modes and their fixes |
| `specs/_template.md` | the spec template `/design` fills in |
| `specs/spec-NN-*.md` | bring-up specs 00–06 (drafted 2026-09-06, approve each before `/build`) and later feature specs from `/design` |
| `archive/` | superseded planning docs — history only, not read by agents |
