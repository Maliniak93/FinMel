---
title: Squash each service's migrations to a single InitialCreate
status: draft
tier: 1
branch: feat/squash-migrations
created: 2026-09-06
---

<!-- Depends on spec-02, spec-03, spec-04 and spec-05 being merged first — see Design decisions #4. -->

## Goal

Each of Identity, Portfolio, MarketData and Reporting has exactly one `InitialCreate` migration reflecting its post-redesign schema, replacing today's task-sequence history.

## Why

ADR-019 explicitly allows this once the schema stabilizes: "when the history gets in the way, a service's migrations may be deleted and regenerated as a single `InitialCreate`". Today's counts (verified by listing `Migrations/` directly, not by trusting the plan's older estimate): Identity 3, Portfolio 7, MarketData 5, Reporting 3. Some exist only as scaffolding artifacts — Portfolio's `AddTransactionAndAssetConcurrencyToken` has empty `Up()`/`Down()` bodies solely to make the model snapshot pick up the `xmin` shadow property. The project is local-only with no real data (ADR-019), so this history has zero preservation value.

## Scope

### Backend

For each of Identity, Portfolio, MarketData, Reporting:

1. Delete the service's `Migrations/` folder entirely.
2. `dotnet ef migrations add InitialCreate --project services/<S>/Skarbiec.<S> --startup-project services/<S>/Skarbiec.<S>` (each service already has a `*DbContextFactory.cs` design-time factory — confirmed present for all four).
3. `dotnet format` immediately after (dotnet.md convention — generated migrations aren't formatted).

Service-specific follow-up:

- **Portfolio only**: hand-remove the `xmin` column the scaffolder injects into the `CreateTable` calls for `Assets` and `Transactions` inside the new `InitialCreate.cs`'s `Up()`. Postgres rejects any DDL naming a column `xmin` (reserved system column) — this is the same failure the original `AddTransactionAndAssetConcurrencyToken.cs` migration exists to route around (its bodies are empty for exactly this reason). Leave `InitialCreate.Designer.cs` and `PortfolioDbContextModelSnapshot.cs` untouched — their `.HasColumnName("xmin")` lines are shadow-property metadata, not DDL, and EF needs them to keep reading the column back correctly.
- **MarketData only**: verify the new `InitialCreate` still creates the Quartz ADO job-store schema (11 `qrtz_*` tables, e.g. `qrtz_job_details`, `qrtz_triggers`) alongside `Instruments`/`PriceQuotes`/`FxRates`/`SyncRuns` — `MarketDataDbContext.OnModelCreating` calls `modelBuilder.AddQuartz(quartz => quartz.UsePostgreSql())` unconditionally, so this should require no manual fix, only confirmation.
- `skarbiec-plan/runbooks/local-dev.md`: extend "Resetting local databases" with a one-line note that this change is exactly the "destructive or squashed EF migration" case the section already covers — link to it, do not duplicate the drop commands.

### Frontend

None.

## Out of scope

Strategy (deleted by spec-01; has no migrations by the time this runs). Any schema change beyond what spec-02…05 already landed — this is a pure history squash, not a redesign; if the generated `InitialCreate` differs from the current model in any way other than shape-preserving history collapse, that is a bug in this spec's execution, not a feature to also fix here.

## Design decisions

1. Run the four services independently — they don't share migration history, so one service's regeneration failing doesn't block the others.
2. **xmin is Portfolio-only.** Confirmed by reading `PortfolioDbContext.cs` (both `Asset` and `Transaction` declare the shadow `Version` property mapped to `xmin`) and grepping the other three services' `Data/*.cs` for `IsConcurrencyToken`/`xmin` (zero hits) — Identity, MarketData and Reporting need no hand-edit after `migrations add`.
3. **Precise xmin proof.** The plain (non-`.Designer.cs`) `InitialCreate.cs` file must contain zero occurrences of `xmin`; `.Designer.cs` and `PortfolioDbContextModelSnapshot.cs` legitimately keep the `HasColumnName("xmin")` metadata and are excluded from that check by filename, not by judgment call.
4. **Ordering dependency.** This spec assumes spec-02 through spec-05 are already merged, so the squashed `InitialCreate` captures their final shapes (Reporting's `Position`/`AssetValuation`, MarketData's `Currency`/`InstrumentUsage`, Identity without `BaseCurrency`, etc.) rather than today's pre-redesign model. Running it earlier would still produce a valid single migration, just one that needs squashing again once 02–05 land — the approved plan's task table orders it last for exactly this reason.
5. No data-migration/backfill path is written (ADR-019 stands). The only compatibility surface is operational: every local Postgres volume must be dropped and recreated after this ships, which is why the runbook update is in scope but a data-preserving path is explicitly not.

## Data / API changes

| Entity / endpoint | Change | Migration? |
|---|---|---|
| `services/Identity/Skarbiec.Identity/Migrations/*` | Squashed to one `InitialCreate` | Yes (replaces entire history) |
| `services/Portfolio/Skarbiec.Portfolio/Migrations/*` | Squashed to one `InitialCreate` (xmin hand-fix) | Yes |
| `services/MarketData/Skarbiec.MarketData/Migrations/*` | Squashed to one `InitialCreate` | Yes |
| `services/Reporting/Skarbiec.Reporting/Migrations/*` | Squashed to one `InitialCreate` | Yes |

## Acceptance criteria

1. Given each service's history is squashed, when its `Migrations/` folder is listed, then exactly 3 files remain (`*_InitialCreate.cs`, `*_InitialCreate.Designer.cs`, `*DbContextModelSnapshot.cs`). — proof: `for d in services/*/Skarbiec.*/Migrations; do n=$(ls "$d" | wc -l); echo "$d:$n"; [ "$n" -eq 3 ] || exit 1; done`
2. Given Portfolio's hand-fix, when the plain migration file is grepped, then it contains no `xmin`. — proof: `grep -c xmin services/Portfolio/Skarbiec.Portfolio/Migrations/*_InitialCreate.cs` returns `0`
3. Given MarketData's Quartz registration is unconditional, when the new migration is grepped, then the ADO schema is present. — proof: `grep -c "qrtz_job_details" services/MarketData/Skarbiec.MarketData/Migrations/*_InitialCreate.cs` returns `1` or more
4. Given Testcontainers always creates a fresh database per test run, when each service's suite runs against the new single migration, then it stays green (exercising the squashed migration end to end). — proof: `Skarbiec.Identity.Tests.RegisterEndpointTests.Register_WithValidRequest_ReturnsCreatedAndStoresHashedPassword`, `Skarbiec.Portfolio.Tests.HealthCheckTests.Ready_ReturnsHealthy`, `Skarbiec.MarketData.Tests.HealthCheckTests.Ready_ReturnsHealthy`, `Skarbiec.Reporting.Tests.HealthCheckTests.Ready_ReturnsHealthy`
5. Given `dotnet format` ran after every `migrations add`, when checked, then formatting is clean. — proof: `dotnet format Skarbiec.slnx --verify-no-changes`
6. Given the full verification script, when run, then it is green. — proof: `node scripts/verify.mjs --all`
7. Given the runbook update, when Aspire starts against a freshly dropped volume, then all four services and the Gateway come up healthy. — proof: manual — drop the volumes per `runbooks/local-dev.md`, then `dotnet run --project Skarbiec.AppHost`, inspect the dashboard

## Verification

```bash
dotnet format Skarbiec.slnx --verify-no-changes
dotnet build Skarbiec.slnx
node scripts/verify.mjs --all
```

Manual smoke: `docker volume rm <postgres-volume> <rabbitmq-volume>` (names via `docker volume ls`, per `runbooks/local-dev.md`), then `dotnet run --project Skarbiec.AppHost` — confirm every service migrates cleanly against the recreated, empty databases and the dashboard shows all resources healthy.

## Risks / open questions

_(none — required empty before `status: approved`)_

## Result

<!-- Filled in by ops after Ship. -->
