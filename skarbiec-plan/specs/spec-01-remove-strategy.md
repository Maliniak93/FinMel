---
title: Remove the Strategy service
status: draft
tier: 1
branch: feat/remove-strategy
created: 2026-09-06
---

## Goal

`services/Strategy/` and every reference to it (solution, AppHost, Gateway, CI, dependabot, frontend) are gone; four services remain (Identity, Portfolio, MarketData, Reporting) + Gateway, per ADR-020.

## Why

ADR-020: Strategy was scaffolded as an empty skeleton (health check only — `Skarbiec.Strategy.Tests` has exactly `ArchitectureTests`, `ContainersCollection`, `HealthCheckTests`) in Phases 0–2. Every feature ever planned for it (target allocation, rebalancing, emergency fund, savings goals) is Phase-3+ scope needing exactly the valuation data Reporting already owns. Folding it in removes a service to run/trace/deploy with zero lost learning value ("few services, many patterns", ADR-001 unchanged).

## Scope

### Backend

- Delete `services/Strategy/` entirely (`Skarbiec.Strategy`, `Skarbiec.Strategy.Tests`, `Dockerfile`).
- `Skarbiec.slnx`: remove the `<Folder Name="/services/Strategy/">` block and its two `<Project>` entries.
- `Skarbiec.AppHost/AppHost.cs`: remove `var strategyDb = await AddServiceDatabase("strategy", "strategy_db");`, the `strategyService` project block (`AddProject<Projects.Skarbiec_Strategy>("strategy-service")...`), and the Gateway's `.WithReference(strategyService).WaitFor(strategyService)` pair.
- `gateway/Skarbiec.Gateway/appsettings.json`: remove the `strategy-openapi` and `strategy` route entries and the `strategy-cluster` cluster entry.
- `.github/workflows/ci.yml`: remove `strategy` from the `changes` job's `outputs` and from `dorny/paths-filter`'s `filters`; delete the whole `strategy:` job block.
- `.github/dependabot.yml`: remove the `directory: "/services/Strategy/Skarbiec.Strategy"` docker entry.

### Frontend

- Delete `web/src/app/api/strategy/` entirely (generated client — `client.gen.ts`, `sdk.gen.ts`, `types.gen.ts`, `core/`, `client/`, `index.ts`).
- `web/openapi-ts.config.ts`: remove `'strategy'` from the `services` const array (edit whichever input shape is current — see Design decisions #3).
- `web/src/app/core/api-clients.ts`: remove the `strategyClient` import and its entry in the `apiClients` array.

## Out of scope

Any Reporting insight feature (target allocation, rebalancing, emergency fund, goals) that this removal makes room for — that is `ideas.md`/future specs, not this one. ADR-021/022/023 (event-carried positions, gRPC withdrawal, currency catalog). Dropping the `strategy_db` Postgres role/database from a running local volume (see Verification — it is inert, not deleted by code).

## Design decisions

1. Confirmed by reading `gateway/Skarbiec.Gateway.Tests/GatewayRoutingTests.cs` and every file under `Infrastructure/` (`GatewayTestHost.cs`, `IdentityTestHost.cs`, `PortfolioTestHost.cs`) that no gateway test references Strategy in any way — no gateway test file needs editing, only `appsettings.json`.
2. Confirmed by reading `scripts/verify.mjs` (`discoverServiceNames()`) that it enumerates service test projects dynamically from the `services/` directory at run time — deleting the folder is sufficient; no `verify.mjs` edit is needed.
3. `web/openapi-ts.config.ts` may already be in spec-00's file-based shape by the time this spec is built (spec-00 is the pilot and typically lands first, though this spec has no hard dependency on it): edit whichever `services` array shape is present — the only change here is dropping the `'strategy'` string, regardless of how `input`/`output` are otherwise expressed. Same caution applies to `web/openapi/strategy.json` if spec-00's build-time docs directory already exists — delete that file too if present.
4. Confirmed `requests/*.http` has no `strategy.http` — nothing to remove there.
5. ADR-020 flips from 🕐 to ✅ in `skarbiec-plan/decisions.md` in this same change, dated with the ship date. Its historical prose ("Strategy's project, tests, Gateway route, AppHost registration, CI job and dependabot entry are removed (spec-01)") stays as written — it is a decision record, not live code, and is correctly excluded from the "no more mentions" grep below.
6. The AppHost's `strategy_user` Postgres role and `strategy_db` database, once provisioned into a running local volume, are not retroactively dropped by any code path this spec touches — there is no migration or startup logic that does that. It becomes an inert leftover until the volume is next reset (see Verification); this is accepted as harmless per ADR-019 (local-only, no real data).

## Data / API changes

| Entity / endpoint | Change | Migration? |
|---|---|---|
| `services/Strategy/*` | Deleted | No |
| Gateway `strategy`/`strategy-openapi` routes, `strategy-cluster` | Deleted | No |
| CI `strategy` job/filter/outputs | Deleted | No |
| Dependabot Strategy docker entry | Deleted | No |
| `web/src/app/api/strategy/` | Deleted | No |
| ADR-020 | 🕐 → ✅ | No |

## Acceptance criteria

1. Given `services/Strategy/` and its slnx entries deleted, when the solution builds, then it succeeds with no Strategy project. — proof: `dotnet build Skarbiec.slnx`
2. Given every reference removed from code and config, when the repo is grepped, then nothing remains (case-insensitive; the plan's own historical ADR-020 prose in `decisions.md` is intentionally not part of this scope). — proof: `grep -ril strategy services gateway Skarbiec.AppHost .github web/src/app/core web/openapi-ts.config.ts Skarbiec.slnx ; test $? -ne 0`
3. Given the Gateway config no longer routes to Strategy, when Gateway tests run, then they stay green. — proof: `dotnet test gateway/Skarbiec.Gateway.Tests/Skarbiec.Gateway.Tests.csproj`
4. Given the frontend no longer imports the strategy client, when the web project builds and typechecks, then it succeeds. — proof: `cd web && npm run typecheck && npm run build`
5. Given the full verification script, when run, then it is green. — proof: `node scripts/verify.mjs --all`
6. Given AppHost no longer references Strategy, when the stack starts, then the Aspire dashboard lists exactly Identity, Portfolio, MarketData, Reporting and the Gateway (no Strategy resource). — proof: manual — `dotnet run --project Skarbiec.AppHost`, inspect the dashboard resource list
7. Given ADR-020 is implemented, when `decisions.md` is read, then its status marker is ✅. — proof: `grep -n "^## ADR-020" skarbiec-plan/decisions.md`

## Verification

```bash
dotnet build Skarbiec.slnx
dotnet test gateway/Skarbiec.Gateway.Tests/Skarbiec.Gateway.Tests.csproj
node scripts/verify.mjs --all
```

Manual smoke: `dotnet run --project Skarbiec.AppHost`, confirm 4 services + Gateway reach "Running" with no Strategy resource in the dashboard.

`strategy_db`/`strategy_user` remain in the local Postgres volume after this ships (see Design decisions #6) — harmless; drop them the same way `runbooks/local-dev.md` documents for any other reset: `docker volume rm skarbiec.apphost-<hash>-postgres-data` (Aspire recreates every current per-service database/role on the next run, and simply never recreates Strategy's).

## Risks / open questions

_(none — required empty before `status: approved`)_

## Result

<!-- Filled in by ops after Ship. -->
