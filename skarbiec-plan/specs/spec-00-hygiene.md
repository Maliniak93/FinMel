---
title: Build hygiene — drop the logging consumer, build-time OpenAPI, gen:api from files
status: draft
tier: 1
branch: feat/hygiene
created: 2026-09-06
---

<!-- Pilot run of the /design -> /build tooling (workflow.md). Kept deliberately small and mechanical. -->

## Goal

`UserRegisteredLoggingConsumer` is gone, all four services emit their OpenAPI document at `dotnet build` time instead of requiring a running Aspire stack, `gen:api` reads those files, and CI gains the `openapi-client` job that enforces the two stay in sync.

## Why

`archive` plan Część I (dead-code list) and Część III.5 (tooling table). `gen:api` today fetches `http://localhost:60684/api/<service>/openapi/v1.json` through the Gateway, so regenerating the client requires the whole stack up — slow, flaky in CI, and blocks the new `openapi-client` CI job this spec adds. `UserRegisteredLoggingConsumer` is explicitly marked temporary in its own doc comment. (Verified on the current tree, re-checked immediately before writing this spec: `.gitattributes`, `web/.nvmrc`, the CI `format`/`web` jobs and the dependabot MassTransit ignore are already in place — an earlier pass of this same verification caught `web/.nvmrc` and the CI/dependabot pieces mid-edit by concurrent tooling work; by the final check they were done, so none of that is re-scoped here. Only the `openapi-client` job is genuinely still missing.)

## Scope

### Backend

- Delete `services/Identity/Skarbiec.Identity/Messaging/UserRegisteredLoggingConsumer.cs` and its registration + comment block in `services/Identity/Skarbiec.Identity/Program.cs` (the `configureConsumers: x => x.AddConsumer<UserRegisteredLoggingConsumer>()` call and the 8-line comment above it). The `UserRegistered` event and every outbox/idempotency/poison/delivery test stay untouched (see Design decisions #1).
- Add `Skarbiec.ServiceDefaults/OpenApi/OpenApiBuildTime.cs`: `public static class OpenApiBuildTime { public static bool IsActive { get; } = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider"; }`.
- `Directory.Packages.props`: add `<PackageVersion Include="Microsoft.Extensions.ApiDescription.Server" Version="10.0.11" />`.
- Identity, Portfolio, MarketData, Reporting `.csproj` (NOT Strategy — it is removed by spec-01; if it still exists when this runs, leave it untouched): add the package reference (`PrivateAssets="all"`, matches the `Microsoft.EntityFrameworkCore.Design` pattern already in every csproj) plus:
  ```xml
  <OpenApiGenerateDocumentsOnBuild>true</OpenApiGenerateDocumentsOnBuild>
  <OpenApiDocumentsDirectory>$(MSBuildThisFileDirectory)../../../web/openapi</OpenApiDocumentsDirectory>
  <OpenApiGenerateDocumentsOptions>--file-name &lt;service&gt;</OpenApiGenerateDocumentsOptions>
  ```
  (`--file-name identity`/`portfolio`/`marketdata`/`reporting` per service — the default would otherwise be `Skarbiec.<Service>.json`.) Path is relative to each project file (`services/<S>/Skarbiec.<S>/`); three `../` reaches the repo root.
- Guard each service's `Program.cs` per the table in Design decisions #2.

### Frontend

- `web/openapi-ts.config.ts`: `input` becomes a local file (`../web/openapi/${service}.json`-relative path, e.g. `./openapi/${service}.json` from `web/`'s own cwd) instead of the Gateway URL; drop the `gatewayUrl`/`SKARBIEC_GATEWAY_URL` const from this file (that env var stays for `smoke:api` only — untouched). `output` becomes `{ path: \`src/app/api/${service}\`, module: { extension: '.js' } }` (Design decisions #7).
- `web/README.md`: update "Regenerating the API clients" — `gen:api` no longer needs the stack running; `SKARBIEC_GATEWAY_URL` is only for `smoke:api` now.
- Regenerate the client once from the new build-time files and commit the result.

### CI / tooling

- New CI job `openapi-client` (self-hosted, `needs: changes`, gated the same way `format` already is — any service, contracts, ServiceDefaults, Testing-lib or web output `== 'true'`): `setup-dotnet` + `setup-node` (`node-version-file: web/.nvmrc`) → `dotnet build Skarbiec.slnx` → `cd web && npm ci && npm run gen:api` → `git diff --exit-code -- web/src/app/api web/openapi`.

## Out of scope

Strategy service wiring (spec-01 deletes it outright). Event/consumer redesign, `AssetChanged`/`TransactionRecorded` removal (spec-02+). Dependabot's Strategy docker entry (spec-01's job, not this one's). The CI `format`/`web` jobs, the dependabot MassTransit ignore, and `web/.nvmrc` are already in place on the current tree (re-verified immediately before writing this spec) — not touched here.

## Design decisions

1. **Zero test files change.** Grepped `services/Identity` for `UserRegisteredLoggingConsumer`: only `Program.cs` and the class's own file reference it — no test does. Read all five `UserRegistered*` test classes: `UserRegisteredOutboxTests`/`UserRegisteredOutboxDurabilityTests`/`UserRegisteredIdempotentConsumerTests`/`UserRegisteredPoisonMessageTests` each build their own throwaway consumer via `HostlessOutboxProvider.Build`/a hand-rolled `ServiceCollection` (never touch Identity's real bus registration); `UserRegisteredDeliveryTests` uses the fully independent `Messaging/TestConsumerHost` + `UserRegisteredTestConsumer` pair, which opens its own bus against the shared RabbitMQ container. None depends on `UserRegisteredLoggingConsumer`.
2. **Per-service guard table** (skip when `OpenApiBuildTime.IsActive`; endpoints/handlers always stay mapped since the doc generator only introspects routes, and a handler is merely resolved from DI per request):

   | Service | Skipped when build-time | Always runs |
   |---|---|---|
   | Identity | `AddNpgsqlDbContext<IdentityDbContext>`, `AddRabbitMqMessaging`, `AddIdentityCore/.AddEntityFrameworkStores/.AddSignInManager`, the dev-only `MigrateAsync` block | `AddServiceDefaults`, `AddServiceOpenApi`, `AddValidation`, all `AddScoped<XHandler>`, `AccessTokenGenerator`, endpoint mappings |
   | Portfolio | `AddDbContext<PortfolioDbContext>`, `AddRabbitMqMessaging`, `AddHttpClient<IInstrumentLookupClient,...>`, `MigrateAsync` block | handler registrations, endpoint mappings |
   | MarketData | `AddNpgsqlDbContext<MarketDataDbContext>`, `AddRabbitMqMessaging`, `AddNbpSources/AddStooqSource/AddCoinGeckoSource/AddPriceSyncJob/AddHistoryBackfillJob`, the OTel `AddSource(...)` tracing block, `MigrateAsync`+seed block | handler registrations, endpoint mappings |
   | Reporting | `AddDbContext<ReportingDbContext>`, `AddRabbitMqMessaging`, both `AddHttpClient` calls, `MigrateAsync` block | `TimeProvider.System` singleton, handler registrations, endpoint mappings |

   `AddServiceDefaults()` itself is left unguarded (matches Microsoft's own documented example) — it wires OTel/health-checks/JWT, none of which needs a live dependency.
3. `Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider"` is Microsoft's own documented pattern for gating build-time-only startup code (verified via `microsoft_docs_fetch` against the current aspnetcore-openapi doc), not a guess.
4. `Microsoft.Extensions.ApiDescription.Server` pinned to `10.0.11` — same patch line as the existing `Microsoft.AspNetCore.OpenApi`/`Microsoft.AspNetCore.Mvc.Testing` pins, and confirmed current on nuget.org.
5. `--file-name <service>` is required: with the default document name (`v1`), the generator would otherwise name the file after the project (`Skarbiec.Identity.json`), not the short service name `gen:api` expects.
6. `web/openapi-ts.config.ts` reading local files sidesteps ADR-013 entirely for tooling purposes — it never talks to the Gateway at generation time; `baseUrl: false` stays unchanged (runtime clients still must not default to a service's own port).
7. hey-api `.js`-extension fix: `output.module.extension: '.js'` (confirmed against hey-api's own current docs — `output.module.extension`, not a generic "import-extension" flag) — replaces relying on generator default behavior, which is what previously dropped extensions non-deterministically (per prior session's gotcha log).

## Data / API changes

| Entity / endpoint | Change | Migration? |
|---|---|---|
| `Skarbiec.Identity.Messaging.UserRegisteredLoggingConsumer` | Deleted | No |
| `web/src/app/api/*` | Regenerated from build-time OpenAPI docs; same routes/schemas, no shape change expected | No |
| `.github/workflows/ci.yml` | + `openapi-client` job | No |

## Acceptance criteria

1. Given the consumer and its registration removed, when Identity's messaging tests run, then all five still pass unmodified. — proof: `dotnet test services/Identity/Skarbiec.Identity.Tests/Skarbiec.Identity.Tests.csproj --filter "FullyQualifiedName~UserRegistered"`
2. Given the four csproj changes, when `dotnet build Skarbiec.slnx` runs with the Aspire stack stopped, then the four documents are (re)written. — proof: `docker ps --filter "name=skarbiec" -q` empty, then `dotnet build Skarbiec.slnx && test -f web/openapi/identity.json && test -f web/openapi/portfolio.json && test -f web/openapi/marketdata.json && test -f web/openapi/reporting.json`
3. Given the guard table, when that same build runs, then no service throws for a missing DB/broker/HTTP dependency (a throw fails the `GetDocument.Insider` step and fails the build). — proof: AC-2's `dotnet build` exit code `0`
4. Given the new config, when the stack is stopped and `npm run gen:api` runs, then it succeeds with no network call to the Gateway. — proof: `cd web && npm run gen:api` (stack stopped, as in AC-2)
5. Given the regenerated client, when typechecked, then it passes and every relative import keeps its `.js` extension. — proof: `cd web && npm run typecheck && grep -rn "from '\.\/[^']*[^s]'" src/app/api/identity/*.gen.ts ; test $? -ne 0`
6. Given the regenerated client is committed, when diffed, then it is clean. — proof: `cd web && npm run gen:api && git diff --exit-code -- web/src/app/api`
7. Given the guard changes normal (non-build-time) startup, when each service's own test suite runs, then its host still boots and serves correctly. — proof: `Skarbiec.Portfolio.Tests.HealthCheckTests.Ready_ReturnsHealthy`, `Skarbiec.MarketData.Tests.HealthCheckTests.Ready_ReturnsHealthy`, `Skarbiec.Reporting.Tests.HealthCheckTests.Ready_ReturnsHealthy`, `Skarbiec.Identity.Tests.RegisterEndpointTests.Register_WithValidRequest_ReturnsCreatedAndStoresHashedPassword`
8. Given everything above, when the full verification script runs, then it is green. — proof: `node scripts/verify.mjs --all`
9. Given the new CI job, when the workflow file is inspected, then it is present and gated like `format`. — proof: `grep -n "^  openapi-client:" .github/workflows/ci.yml`

## Verification

```bash
dotnet build Skarbiec.slnx
dotnet test services/Identity/Skarbiec.Identity.Tests/Skarbiec.Identity.Tests.csproj
cd web && npm ci && npm run gen:api && npm run typecheck && git diff --exit-code -- web/src/app/api
node scripts/verify.mjs --all
```

Manual smoke: `dotnet run --project Skarbiec.AppHost` — Aspire dashboard shows every service (including Strategy, if spec-01 hasn't merged yet) reaching "Running"/healthy, and `GET https://localhost:<gateway-port>/api/identity/openapi/v1.json` still 200s (runtime `MapServiceOpenApi` is unaffected by the build-time guard).

## Risks / open questions

_(none — required empty before `status: approved`)_

## Result

<!-- Filled in by ops after Ship. -->
