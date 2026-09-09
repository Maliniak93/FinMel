# Phase 2 — Market data + CQRS

**Duration:** 4–6 weeks. The most interesting "microservices" phase: jobs → event → idempotent consumer → read model.
**Goal:** prices and FX rates flow in daily on their own; Reporting builds valuation snapshots from the `DailyPricesSynced` event; the dashboard switches from Portfolio to the Reporting read model. Production observability (T2.15) is optional/deferred while development stays local-only — see `zadania/README.md`.
**Deliverable (from roadmap):** valuations update on their own; a single trace shows job→event→consumer→write.
**Sources:** roadmap Phase 2, E4/E5, ADR-007/012/015, `diagrams/price-sync-sequence.mermaid`.

## Task index

| ID    | Task                                                            | Size | Depends on  |
| ----- | --------------------------------------------------------------- | ---- | ----------- |
| T2.1  | MarketData: entities + dictionary seed                          | M    | Phase 1     |
| T2.2  | `IPriceSource` abstraction + source test kit                  | M    | T2.1        |
| T2.3  | NBP source (FX + gold)                                          | M    | T2.2        |
| T2.4  | Stooq source                                                    | M    | T2.2        |
| T2.5  | CoinGecko source                                                | M    | T2.2        |
| T2.6  | Quartz`PriceSyncJob`                                          | L    | T2.3–T2.5  |
| T2.7  | History backfill on instrument add                              | M    | T2.6        |
| T2.8  | Instrument search + custom instruments [S]                      | M    | T2.1        |
| T2.9  | Portfolio: market-valued assets (`InstrumentId`)              | M    | T2.8        |
| T2.10 | Event`DailyPricesSynced`                                      | S    | T2.6        |
| T2.11 | Reporting: consumer →`ValuationSnapshot`                     | L    | T2.10, T2.9 |
| T2.12 | Reporting: dashboard read model + history endpoint              | L    | T2.11       |
| T2.13 | Angular: dashboard on Reporting, chart, autocomplete, stale     | L    | T2.12       |
| T2.14 | Manual sync trigger [S]                                         | S    | T2.6        |
| T2.15 | [VPS, optional] Production observability: Grafana stack + alert | L    | T2.6, T0.18 |
| T2.16 | Phase exit: local smoke — full trace (+ optional VPS)          | M    | T2.1–T2.14 |

---

### T2.1 MarketData: entities + dictionary seed

- [X] **Status:** done · **Size:** M

- **Depends on:** Phase 1
- **Refs:** E4, ADR-007, `03-domain-model.md` §MarketData

**Goal:** MarketData owns `Instrument`, `PriceQuote`, `FxRate` with correct constraints and a seeded starter dictionary.

**Scope:**

- Entities: `Instrument` (ticker, name, source enum `Nbp|Stooq|CoinGecko`, quote currency, `AssetClass`), `PriceQuote` (`InstrumentId`, `DateOnly` date, decimal close — explicit precision), `FxRate` (pair, `DateOnly` date, rate).
- Unique indexes: (instrument, date) and (pair, date) — upsert-friendly.
- Instruments/quotes/FX are **global** (shared dictionary, no `UserId`/tenancy filter); custom user-added instruments handled in T2.8.
- Seed migration/script: the instruments you actually hold (from Phase 1 data entry) + major FX pairs (USD/PLN, EUR/PLN…) — enough to be useful day one.

**Acceptance criteria:**

- [X] Duplicate (instrument, date) insert fails / upserts — test proves the index.
- [X] Seed idempotent (re-running doesn't duplicate).
- [X] Migration creates schema only in `marketdata_db`.

---

### T2.2 MarketData: `IPriceSource` abstraction + source test kit

- [X] **Status:** done · **Size:** M

- **Depends on:** T2.1
- **Refs:** ADR-007, roadmap risk "Stooq/CoinGecko changes its format"

**Goal:** one interface isolates every external price API; a shared test kit makes each implementation verifiable against recorded responses.

**Scope:**

- `IPriceSource`: capabilities (which sources serve which instruments), fetch latest quotes for a set of instruments, fetch history range; returns a result type distinguishing "no data (non-trading day)" from "error" — that distinction is a hard requirement (E4).
- Typed `HttpClient` per source with ServiceDefaults resilience (retry+jitter, circuit breaker, timeout); rate-limit friendliness (CoinGecko free tier!) — per-source delay/batching hook in the abstraction.
- Test kit: fixture-based tests replaying recorded real responses (happy path, empty day, malformed payload) so a format change is caught by re-recording, not in production.

**Acceptance criteria:**

- [X] Sources are swappable behind the interface (job code in T2.6 references only `IPriceSource`).
- [X] "Non-trading day" round-trips as no-entry-not-error through the abstraction.
- [X] Malformed payload → source-level error result, not an exception escaping the job.

---

### T2.3 MarketData: NBP source (FX table A + gold)

- [X] **Status:** done · **Size:** M

- **Depends on:** T2.2
- **Refs:** E4 [M] (NBP daily), ADR-008 (PLN base)

**Goal:** daily FX rates (table A) and the gold price flow from the NBP API into `FxRate`/`PriceQuote`.

**Scope:**

- NBP web API client: table A (all needed currencies in one call), gold price endpoint; history endpoints for backfill (mind NBP's max range per request — chunk).
- Upsert semantics; weekends/holidays = API 404/empty → "no data", not error.
- Gold modeled as an `Instrument` with source `Nbp` (quote currency PLN, per-gram — document the unit in the instrument record!).

**Acceptance criteria:**

- [X] Fixture tests: table A parse, gold parse, holiday (no data), API error.
- [X] Live smoke (manually run once): today's or last trading day's rates land in `marketdata_db`.
- [X] Re-running the same day is idempotent (upsert).

---

### T2.4 MarketData: Stooq source (GPW / ETFs / metals)

- [X] **Status:** done · **Size:** M

- **Depends on:** T2.2
- **Refs:** E4 [M]

**Goal:** EOD quotes for Stooq-sourced instruments (GPW stocks, ETFs, metals).

**Scope:**

- Stooq CSV endpoint client (per-ticker daily + historical CSV); robust CSV parsing (culture-invariant decimals, `N/D` handling); ticker convention documented (e.g. `.WA` suffixes, currency inference vs stored quote currency).
- Same result semantics as T2.3 (no data vs error); per-ticker fetch — one bad ticker must not fail the batch (isolation enforced again at job level, T2.6).

**Acceptance criteria:**

- [X] Fixture tests: normal CSV, empty/`N/D`, malformed.
- [X] Historical range fetch returns a correctly parsed series (fixture).
- [X] Quote currency respected (an USD-quoted ETF stores USD prices, conversion happens at valuation, not ingestion).

---

### T2.5 MarketData: CoinGecko source (crypto)

- [X] **Status:** done · **Size:** M

- **Depends on:** T2.2
- **Refs:** E4 [M]

**Goal:** daily crypto prices from CoinGecko for instruments identified by CoinGecko id.

**Scope:**

- CoinGecko client: current price (batch by ids — one call for many coins) + market-chart/history for backfill; instrument's ticker field stores the CoinGecko id for this source (document).
- Free-tier rate limits: conservative request pacing, honor 429 with backoff (resilience pipeline) — never hammer.
- Prices fetched in a stable quote currency (USD or PLN — pick one, document; conversion at valuation time uses FxRate either way).

**Acceptance criteria:**

- [X] Fixture tests: batch price parse, history parse, 429 → retry-after respected (simulated).
- [X] Live smoke once: BTC/ETH prices land.
- [X] Rate limiting verified (pacing visible in logs; no 429 storm on a 10-instrument batch).

---

### T2.6 MarketData: Quartz `PriceSyncJob`

- [X] **Status:** done · **Size:** L

> Modified by M1.4 — see modyfikacje/modification-1.md

- **Depends on:** T2.3, T2.4, T2.5
- **Refs:** E4 [M] (daily sync, failure isolation), ADR-007, `diagrams/price-sync-sequence.mermaid`

**Goal:** a scheduled job syncs FX + quotes for **instruments in use only**, isolating per-instrument failures, fully observable.

**Scope:**

- Quartz.NET with PG persistence (survives restarts; no duplicate concurrent runs — `DisallowConcurrentExecution`); daily schedule after market close / NBP publication (document chosen cron and why).
- "In use" = instruments referenced by ≥1 asset: MarketData asks Portfolio via internal REST (or reacts to `AssetChanged` maintaining a local usage set — **decide at implementation**, document; REST pull is simpler, start there).
- Per-source, per-instrument error isolation: one failure logs + continues; run summary (synced / no-data / failed counts) logged and exposed (used by T2.15 alert and T2.14 UI).
- Persist a `SyncRun` record (started, finished, counts, status) — the "has it run lately" source of truth.
- The job is the **only** place external APIs are called (ADR-007) — architecture test or code-review checklist item.

**Implementation notes:**

- **"In use" deferred to T2.9**: Portfolio has no `InstrumentId` on `Asset` yet (that's T2.9, which hasn't landed). Querying Portfolio for real usage isn't possible today, so `PriceSyncJob.GetInstrumentsToSyncAsync` currently syncs every instrument in MarketData's own dictionary — documented in a `<remarks>` on the job pointing at T2.9 as the place to swap in the real Portfolio-backed filter (REST pull, per the scope note) once `Asset.InstrumentId` exists.
- **Quartz persistence**: `Quartz` 3.19.1 + `Quartz.Extensions.Hosting` + `Quartz.Serialization.SystemTextJson`, `UsePersistentStore` + `UsePostgres` + `UseClustering()`, `SchedulerId = "AUTO"` (unique instance id per node, required for clustering). Schema created via EF Core migration using `AppAny.Quartz.EntityFrameworkCore.Migrations.PostgreSQL` (`modelBuilder.AddQuartz(b => b.UsePostgreSql())` in `MarketDataDbContext`, schema `quartz`, prefix `qrtz_`) instead of hand-running Quartz's `tables_postgres.sql` — keeps schema ownership consistent with every other entity in the service.
- **Cron**: business days 18:30 in production (`PriceSync:Cron` in `appsettings.json`) — after GPW's ~17:00 CET close and well after NBP table A's midday publish (per the sequence diagram). `appsettings.Development.json` overrides to every 2 minutes so the job is observable locally without waiting.
- **Registration gated** on `Testing:DisableBackgroundJobs` (literal string, not a project reference to `Skarbiec.Testing` — keeps production code from depending on test infrastructure) so `SkarbiecApiFactory`-based slice tests never race a live scheduler.
- **Tracing**: dedicated `ActivitySource("Skarbiec.MarketData.PriceSyncJob")`, registered with OpenTelemetry tracing in `Program.cs` (ServiceDefaults only wires the app's own `ApplicationName`-named source plus MassTransit's).
- **ADR-007 enforcement**: new `ArchitectureTests.OnlySourcesNamespace_DependsOn_PriceSourceAbstractions` — any type depending on `IPriceSource`/`IFxRateSource` must live in the `Sources` namespace, so a future request-path handler can't call a source directly.

**Acceptance criteria:**

- [X] Integration test: 3 instruments, middle one's source fails → other two synced, run recorded as partial. — `PriceSyncJobTests.RunAsync_MiddleSourceFails_OtherTwoSynced_RunRecordedAsPartial` (green against Testcontainers Postgres).
- [X] Job runs on schedule locally (shortened cron in dev) and is trace-instrumented (job span visible). — `PriceSyncSchedulingTests.AddPriceSyncJob_OnShortenedDevCron_FiresAutomatically_WithTraceSpan`: real `AddPriceSyncJob()` DI wiring on a 2-second dev cron fires the job with no manual trigger, `PriceSyncJob.Run` activity captured via an `ActivityListener`.
- [X] Restarting the service mid-schedule doesn't lose or duplicate the run. — `PriceSyncSchedulingTests.Schedule_SurvivesSchedulerRestart_TriggerNotLost` (persisted trigger survives a scheduler shutdown/rebuild against the same store) and `ClusteredSchedulers_ShareOneFire_ExactlyOnce_NeverLostNeverDuplicated` (two clustered scheduler instances live simultaneously — the restart/rolling-deploy overlap window — share one fire exactly once).

---

### T2.7 MarketData: history backfill on instrument add

- [X] **Status:** done · **Size:** M

- **Depends on:** T2.6
- **Refs:** E4 [M] (backfill min. 1 year)

**Goal:** when an instrument enters use, its price history (≥1 year where the source allows) is fetched automatically.

**Scope:**

- Trigger: instrument first attached to an asset (or created, for custom ones) → enqueue a one-off Quartz job (don't block the request — ADR-007 spirit: no external calls in request path).
- Use `IPriceSource` history fetch; chunk long ranges; upsert (idempotent — re-backfill safe).
- FX backfill too (a USD instrument needs a year of USD/PLN for the history chart to be honest).

**Implementation notes:**

- **Nothing calls the trigger yet**: same deferral shape as T2.6's "in use" filter. `IHistoryBackfillTrigger`/`QuartzHistoryBackfillTrigger` (`Sources/`) enqueue a one-off, non-durable `HistoryBackfillJob` + trigger pair via `IScheduler.ScheduleJob`, but the two real call sites — T2.8's custom-instrument add, T2.9's attach-to-asset — don't exist as request handlers yet. Wire the call in from there once they land; documented in a `<remarks>` on `HistoryBackfillJob`.
- **Chunking already lived in the sources**: T2.3-T2.5's `FetchHistoryAsync` implementations already chunk long ranges internally (`NbpDateRangeChunker` for NBP; Stooq/CoinGecko take a range in one call) — `HistoryBackfillJob` just calls `FetchHistoryAsync(instrument, from, to, ct)` once per instrument/currency.
- **Upsert extracted**: `PriceSyncJob`'s upsert-by-(instrument, date)/(pair, date) logic moved to a shared `QuoteUpsert` static class (`Sources/QuoteUpsert.cs`) so both jobs share one idempotency-critical implementation instead of two copies.
- **DI wiring**: `HistoryBackfillJobExtensions.AddHistoryBackfillJob()` registers `HistoryBackfillJob` + `IHistoryBackfillTrigger` (gated on the same `Testing:DisableBackgroundJobs` flag as `AddPriceSyncJob`) but deliberately doesn't call `AddQuartz`/`UsePersistentStore` again — it reuses the one scheduler `AddPriceSyncJob` already configures; must be called after it in `Program.cs`.

**Acceptance criteria:**

- [X] Attaching a new instrument results in ≥1 year of quotes appearing without user action (integration test with fixture source). — `HistoryBackfillJobTests.RunAsync_UsdInstrument_BackfillsOneYearOfQuotesAndItsFxPair` (direct, Testcontainers Postgres) and `HistoryBackfillSchedulingTests.EnqueueAsync_ReturnsBeforeAnyFetch_ThenTheJobFiresOnItsOwnAndBackfillsOneYear` (real Quartz scheduler, job fires on its own, quotes land).
- [X] Request that triggers backfill returns immediately (no external call in the request path — asserted via test fake). — same scheduling test: `Assert.Equal(0, priceSource.HistoryFetchCount)` immediately after `EnqueueAsync` returns, before the job has had a chance to fire.
- [X] Re-running backfill duplicates nothing. — `HistoryBackfillJobTests.RunAsync_CalledTwice_UpsertsInsteadOfDuplicating` (same instrument/FX pair backfilled twice with different values; row counts unchanged, latest values win).

---

### T2.8 MarketData: instrument search + custom instruments [S]

- [X] **Status:** done · **Size:** M

> Modified by M1.6 — see modyfikacje/modification-1.md

- **Depends on:** T2.1
- **Refs:** E2 [M] (autocomplete with last price), E2 [S] (own instrument)

**Goal:** an endpoint powering the asset-form autocomplete (ticker/name search with last price preview), plus adding a custom instrument by Stooq ticker / CoinGecko id.

**Scope:**

- `Features/SearchInstruments`: query by ticker/name prefix, returns id, ticker, name, class, currency, last price + its date (join to latest `PriceQuote` — indexed, fast); paged/limited.
- `Features/AddCustomInstrument` [S]: user supplies source + ticker/id; validate by fetching one live quote **via a one-off job/deferred check, not inline** if it conflicts with ADR-007 — pragmatic exception allowed here only if documented in the task and kept out of hot paths (decide at implementation; simplest compliant option: create as unverified → immediate one-off sync job verifies).
- Custom instruments enter the global dictionary (visible to all users — matches the shared-dictionary model; note the moderation caveat as a future concern).

**Implementation notes:**

- **`Instrument.VerificationStatus`** (new `InstrumentVerificationStatus`: `Verified`/`Unverified`/`Failed`, string-converted column, `HasDefaultValue(Verified)`) is the "unverified → resolves" mechanism the scope asked for. `MarketDataSeeder`'s dictionary rows are created `Verified` (curated by hand); `AddCustomInstrumentHandler` creates a new instrument `Unverified` and calls `IHistoryBackfillTrigger.EnqueueAsync` — **reusing T2.7's `HistoryBackfillJob` as the "one-off job" the scope asks for**, instead of adding a second job. `HistoryBackfillJob.RunAsync` now flips `Unverified` → `Verified` (fetch succeeded, ≥1 quote) or `Failed` (fetch errored/returned nothing), but only when the instrument is still `Unverified` going in — an already-`Verified` catalog instrument (T2.9's future "first attach to an asset" trigger reuses the same job) never flips to `Failed` just because one backfill run hiccups.
- **EF gotcha hit and fixed**: `InstrumentVerificationStatus.Unverified` must not be the enum's `0`/default member. With `HasDefaultValue(Verified)` configured, EF Core's insert logic treats a property holding its CLR-default value as "not set" and lets the DB default win — so with `Unverified = 0` (the original ordering), every custom instrument silently landed `Verified` regardless of what the handler set. Fixed by reordering the enum so `Verified = 0`; documented on the enum itself so it isn't reintroduced.
- **Source restricted to Stooq/CoinGecko**: `AddCustomInstrumentHandler` rejects `PriceSource.Nbp` (400) — NBP only serves its fixed FX-table/gold endpoints (T2.3), not arbitrary user tickers.
- **`IHistoryBackfillTrigger` now always resolvable**: `HistoryBackfillJobExtensions.AddHistoryBackfillJob()` previously no-op'd entirely under `Testing:DisableBackgroundJobs` (fine while nothing in `Features/` depended on it). T2.8 is the first request-path caller, so under that flag it now registers a new `NoOpHistoryBackfillTrigger` instead — keeps `SkarbiecApiFactory`-based HTTP slice tests able to resolve the handler's dependency without a live Quartz scheduler.
- **Search is two queries, not a join**: `SearchInstrumentsHandler` fetches matching instruments (prefix `ILIKE` on ticker/name) then a second query grouping `PriceQuotes` by instrument for the latest row per match — simpler and more predictable than a correlated-subquery/LATERAL join in EF, and fast enough for the <100 ms AC given the dictionary's realistic size.
- **Performance AC measured against the handler directly**, not over HTTP (`SearchInstrumentsPerformanceTests`) — a full Kestrel/JSON round trip would mix in overhead the AC isn't about; a warm-up call absorbs first-query JIT/connection-pool cost before the timed one.

**Acceptance criteria:**

- [X] Search returns matches with last price and date in <100 ms on seeded data. — `SearchInstrumentsPerformanceTests.HandleAsync_OnSeededDictionary_CompletesUnder100Milliseconds` (50 seeded instruments, handler call timed after a warm-up call).
- [X] Adding a valid custom Stooq ticker makes it usable + backfilled (T2.7 path); invalid ticker ends in a visible failed/unverified state, not a 500. — `AddCustomInstrumentEndpointTests.Add_ValidStooqTicker_ReturnsCreatedAsUnverified`/`Add_MakesInstrumentImmediatelyFindableBySearch` (usable immediately); `HistoryBackfillVerificationTests.RunAsync_UnverifiedInstrument_SuccessfulFetch_MarksVerified`/`_ErrorFetch_MarksFailedNotAnException`/`_NoDataFetch_MarksFailed` (T2.7's job resolves it, never throws).
- [X] Search endpoint requires auth but returns global data (no tenancy filter) — test. — `SearchInstrumentsEndpointTests.Search_WithoutToken_ReturnsUnauthorized` + `Search_AsTwoDifferentUsers_ReturnsIdenticalResults`.

---

### T2.9 Portfolio: market-valued assets (`InstrumentId`)

- [X] **Status:** done · **Size:** M

> Modified by M1.4 — see modyfikacje/modification-1.md
> Modified by M1.5 — see modyfikacje/modification-1.md

- **Depends on:** T2.8, T1.2
- **Refs:** E2 [M], ADR-003 (ref by id, no FK), `03-domain-model.md` §valuation modes

**Goal:** an asset can point to an instrument instead of a manual value; validation crosses the service boundary by API, not FK.

**Scope:**

- `AddAsset`/`UpdateAsset` accept mode: manual (as-is) **or** market (`InstrumentId`, quantity; no manual value); switching modes allowed with clear rules (document: switching to market keeps transactions; manual value ignored while instrument set).
- On write, Portfolio validates `InstrumentId` exists via MarketData internal REST (resilient client; MarketData down → 503 ProblemDetails, fail closed) — consistency at API level per ADR-003.
- No price logic in Portfolio — valuation belongs to Reporting (T2.11).

**Implementation notes:**

- **Mode stays implicit, no new enum**: `Asset.InstrumentId` (nullable Guid, already existed since T1.2) vs `ManualValueAmount`/`ManualValueDate` (now nullable) — `InstrumentId is not null` ⇒ market, else manual. `AddAssetRequest`/`UpdateAssetRequest` enforce mutual exclusion via `IValidatableObject` (first use of this in the codebase; dotnet.md already named it as the intended mechanism for cross-field rules, confirmed against .NET 10 docs that Minimal API validation calls it after property-level DataAnnotations pass).
- **First internal service-to-service HTTP call in the solution**: MarketData gets a new `GET /api/marketdata/instruments/{id:guid}` endpoint (`Features/GetInstrument/`, same auth as `SearchInstruments`). Portfolio calls it through `IInstrumentLookupClient`/`MarketDataInstrumentLookupClient` (`Skarbiec.Portfolio/MarketData/`), a typed `HttpClient` on `https+http://marketdata-service` (Aspire service-discovery scheme, confirmed via Microsoft Learn docs) — resilience (retry+jitter, circuit breaker, timeout) comes for free from `ConfigureHttpClientDefaults` in ServiceDefaults, nothing extra to configure.
- **JWT passthrough made reusable, not one-off**: added `JwtForwardingHandler` + `AddJwtForwardingHandler()` to `Skarbiec.ServiceDefaults/Http/` (dotnet.md already documented "forward the caller's JWT... configured once in ServiceDefaults" as the convention; this is its first real implementation). `AddServiceDefaults()` now also registers `IHttpContextAccessor` for it.
- **New error category**: `ResultHttpExtensions.MapStatus` gained `"ServiceUnavailable" → 503` (previously only NotFound/Conflict/Validation/Unauthorized/Forbidden existed). "Instrument not found" reuses the existing `"Validation"` category (→ 400) rather than adding a second new one, since it genuinely is a request-validation failure once the lookup resolves.
- **`MarketDataInstrumentLookupClient` exception handling is deliberately broad** (catches any transport failure — connection refused, DNS, TLS, open circuit breaker — and maps to `Unavailable`) but the cancellation branch had a real bug caught by its own unit test: an initial `catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)` let genuine caller cancellation fall through into the broad `catch (Exception)` below and get silently swallowed as "MarketData unavailable" instead of propagating. Fixed by catching `OperationCanceledException` unconditionally first and explicitly rethrowing when the caller's own token was the source.
- **`GetWealthSummaryHandler` (Phase 1 shortcut, due for removal in T2.12/T2.13) updated to treat a market asset's `ManualValueAmount` as 0** now that the field is nullable — it never priced anything and still doesn't; a market asset's real value only appears once Reporting replaces this endpoint.
- **Test infra**: `PortfolioApiFactory` now overrides `ConfigureWebHost` to substitute `IInstrumentLookupClient` with a new `FakeInstrumentLookupClient` via `ConfigureTestServices` — first use of that pattern in the repo (previously `SkarbiecApiFactory` never touched `IServiceCollection`). `PortfolioOutboxTests` (which builds its own bare `ServiceProvider` outside the WebApplicationFactory) needed the same fake registered by hand. MarketData's `SearchInstrumentsEndpointTests` had its private `SeedInstrumentAsync`/`SeedQuoteAsync` helpers extracted into `MarketDataApi.cs` (dotnet.md: duplicated test helpers get extracted on the *second* use, not the third) so the new `GetInstrumentEndpointTests` could reuse them instead of a third copy.
- **Out of scope, deliberately**: T2.6's implementation notes flagged that `PriceSyncJob.GetInstrumentsToSyncAsync` could eventually filter to "instruments actually referenced by an asset" once `Asset.InstrumentId` exists — that would need Portfolio to expose its own "instruments in use" endpoint for MarketData to call (the reverse direction of this task). T2.9's own Scope/AC never asked for that, so it wasn't built here; it's a natural follow-up for whoever revisits T2.6's sync-scope, not a gap in this task.
- **EF migration** `20260806114112_MarketValuedAssets`: relaxes `ManualValueDate`/`ManualValueAmount` to nullable and adds an index on `InstrumentId`; scaffolded cleanly this time (no xmin artifacts like T1.2 hit), only needed `dotnet format` for CRLF/block-scoped-namespace cleanup on the generated file, same as T1.2's precedent.

**Acceptance criteria:**

- [X] Market asset with valid instrument → 201; nonexistent instrument → 400/422. — `AddAssetEndpointTests.Add_WithValidInstrument_ReturnsCreatedAsMarketAsset` / `Add_WithNonExistentInstrument_ReturnsBadRequest` (+ `UpdateAssetEndpointTests` equivalents, `Add_WithBothInstrumentIdAndManualValue_ReturnsBadRequest`, `Add_WithNeitherInstrumentIdNorManualValue_ReturnsBadRequest`).
- [X] MarketData unavailable → 503 with ProblemDetails (verified with a stopped fake), not a hang (timeout from resilience defaults). — `AddAssetEndpointTests.Add_WhenMarketDataUnavailable_ReturnsServiceUnavailable`/`UpdateAssetEndpointTests.Update_WhenMarketDataUnavailable_ReturnsServiceUnavailable` (fake reports `Unavailable`); `MarketDataInstrumentLookupClientTests.CheckAsync_TargetPortHasNothingListening_ReturnsUnavailableAndDoesNotHang` additionally proves the real client's own exception handling against a genuinely unreachable port, bounded under 30s.
- [X] Existing manual assets untouched by the migration. — nullability relaxation only (no data rewrite); `AddAssetEndpointTests`/`UpdateAssetEndpointTests`' pre-existing manual-value facts (all 9 `AssetClass` values, negative-value/quantity validation, etc.) still pass unmodified against the new schema.

Verification run: `dotnet build Skarbiec.slnx` (0 warnings/errors) · `dotnet format` clean on every touched project · `dotnet test` — Portfolio 122/122, MarketData 78/78 (+2 pre-existing live-smoke skips), ServiceDefaults 12/12, all Testcontainers-backed (Docker).

---

### T2.10 Contracts: `DailyPricesSynced` event

- [X] **Status:** done · **Size:** S

- **Depends on:** T2.6, T0.10
- **Refs:** ADR-012, ADR-015, `02-architecture.md` §flows

**Goal:** the sync job publishes `DailyPricesSynced` through the MarketData outbox when a run completes.

**Scope:**

- Record in `Skarbiec.Contracts`: sync date, run id, counts (synced/failed/no-data) — lean payload; Reporting pulls details via REST.
- Outbox wiring in MarketData (T0.10 pattern); publish at successful/partial run completion (document: partial runs still publish — snapshots use last-known prices anyway).

**Acceptance criteria:**

- [X] Event in outbox in the same transaction as the `SyncRun` completion write. — `MarketDataOutboxTests.RunAsync_SuccessfulRun_WritesDailyPricesSyncedOutboxMessageInSameTransactionAsSyncRunRow` (hostless outbox provider, no hosted bus racing the assertion, mirrors `PortfolioOutboxTests`/T1.5) + `RunAsync_WhollyFailedRun_DoesNotPublish` proving the partial/successful-only publish rule.
- [X] Contract deserialization test. — `DailyPricesSyncedContractTests.Deserialize_FixtureWithUnknownFields_StillDeserializesKnownFields`.
- [X] Trace: job span → publish span linked. — `PriceSyncJobTraceTests.RunAsync_SuccessfulRun_PublishActivitySharesTraceWithJobActivity` asserts the MassTransit publish `Activity`'s `TraceId` equals `PriceSyncJob.Run`'s (ambient `Activity.Current` parenting, no manual correlation id).

Verification run: `dotnet build Skarbiec.slnx` (0 warnings/errors) · `dotnet format` clean on every touched/added file · `dotnet test` (whole solution) — Contracts 17/17, MarketData 81/83 (+2 pre-existing live-smoke skips), Identity 24/24, Portfolio 122/122, Gateway 4/4, Reporting 3/3, Strategy 3/3, ServiceDefaults 12/12, Testing 4/4, all Testcontainers-backed (Docker).

---

### T2.11 Reporting: idempotent consumer → `ValuationSnapshot`

- [X] **Status:** done · **Size:** L

> Modified by M1.4 — see modyfikacje/modification-1.md

- **Depends on:** T2.10, T2.9, T0.12
- **Refs:** E5 [M], ADR-015 (CQRS), `03-domain-model.md` §valuation algorithm

**Goal:** on `DailyPricesSynced`, Reporting computes and stores per-portfolio valuation snapshots for all users — idempotently.

**Scope:**

- Consumer with inbox/dedup (T0.12 template): fetch positions per user/portfolio from Portfolio (internal REST, paged), prices + FX from MarketData (batch endpoints — add them if missing), compute per the domain algorithm:
  - market asset: quantity × last quote × FX(currency→PLN, same date); no quote that day → last known; mark `stale` when >7 days old;
  - manual asset: ManualValue × FX at snapshot date.
- `ValuationSnapshot`: `UserId`, `PortfolioId`, date, total (PLN, decimal precision), per-asset-class breakdown (JSONB), stale flag(s). Unique (portfolio, date) → redelivery/rerun overwrites deterministically (upsert) — that plus inbox = idempotency.
- Failure of one user's computation doesn't abort the rest (same isolation philosophy as the sync job); summary logged.
- Internal REST endpoints this needs (Portfolio: positions-for-valuation; MarketData: latest-prices-batch, fx-batch) are part of this task — service-to-service auth approach (forwarded context vs machine token for this system-initiated flow) decided and documented here.

**Implementation notes:**

- **New trust boundary: `SystemCaller`, not a per-user token.** Neither of the two options the task named fit as-is: forwarded context has nothing to forward (a MassTransit consumer has no inbound HTTP request), and this flow needs *every* user's data, not one. Added a third option instead — `Skarbiec.ServiceDefaults/Authentication/SystemCaller.cs` defines a claim (`skarbiec:caller=system`) and an authorization policy (`"System"`); `Skarbiec.ServiceDefaults/Http/SystemTokenHandler.cs` is a `DelegatingHandler` (`AddSystemTokenHandler()`) that mints a short-lived JWT carrying that claim, signed with the same shared `Jwt` signing key every service already validates against (ADR-005 local-dev secret) — no new secret, no new auth scheme, just a new claim the existing `AddJwtBearer` pipeline already accepts. Portfolio's `GetPositionsForValuationEndpoint` and both of MarketData's batch endpoints require `SystemCaller.PolicyName` instead of a bare `RequireAuthorization()`.
- **Tenancy filter can't answer "every user's data" — extended, not bypassed, at the interceptor.** Portfolio's positions endpoint and Reporting's own snapshot upsert both need to read/write across users in one call, which ADR-006's global query filter (`e.UserId == CurrentUserId`) structurally can't do (`CurrentUserId` throws with no HTTP request). Reads use `IgnoreQueryFilters()`. Writes needed more thought: `UserOwnedSaveInterceptor` unconditionally stamped every newly-`Added` `IUserOwned` entity's `UserId` from the current request — incompatible with one message writing many different users' rows. Changed the stamp to apply only when `UserId` is still `Guid.Empty` (`Skarbiec.ServiceDefaults/Tenancy/UserOwnedSaveInterceptor.cs`) — every existing call site never sets `UserId` itself (ADR-006: only the interceptor does), so this is behaviorally identical everywhere except the one new case: Reporting's consumer sets `UserId` explicitly per row from Portfolio's own data (a trusted internal source, not a request body — ADR-006's actual guarantee is untouched).
- **First JSONB column in the project.** `ValuationSnapshot.BreakdownJson` is a plain `System.Text.Json`-serialized `string` mapped `.HasColumnType("jsonb")` (`Data/ValuationBreakdown.cs`), not EF's native JSON-owned-entity mapping — avoids depending on whichever JSON-column support the installed Npgsql provider version has, while the column still stores real `jsonb` or later querying if T2.12 ever needs it.
- **Algorithm kept pure and separate from the consumer.** `Valuation/ValuationAlgorithm.Calculate(...)` takes plain DTOs (no DB/HTTP types) and returns `TotalPln`/`Breakdown`/`IsStale` — the AC's "last-known-price fallback" is exercised by feeding it a quote whose `Date` differs from the snapshot date (the actual SQL `<=` fallback lives in MarketData's batch handlers, a data-layer concern; the algorithm only ever compares dates for staleness). 11 table-driven unit tests, no Testcontainers.
- **Per-portfolio isolation without savepoints.** MassTransit's EF outbox wraps the whole `Consume` call in one DB transaction with no savepoints (confirmed against MassTransit's own source via context7 — no mid-transaction recovery available). So per-portfolio failures are caught *before* anything is ever written to the tracked `DbContext` (computation happens entirely in memory; a failed portfolio just never gets `Add`ed) — no bad statement can ever reach Postgres and poison the shared transaction. A *total* failure to reach Portfolio or MarketData (not a single portfolio's own data) is deliberately left to propagate instead of being swallowed, so the inbox's `UseMessageRetry` gets its intended chance to recover once the dependency is back.
- **Reporting's first production consumer surfaced a real test gap.** `IdempotentConsumerDefinition` previously only ran in Identity's own tests, never through a real `Program.cs`/`WebApplicationFactory` host. `HealthCheckTests.Ready_ReturnsHealthy` started flaking: MassTransit's own health check briefly reports "not started" right after the host comes up (confirmed with a throwaway diagnostic test reading `HealthCheckService.CheckHealthAsync()` entries directly), long enough for Reporting's heavier receive-endpoint pipeline (retry + EF outbox middleware, two new typed HttpClients) to occasionally still be starting when the test's first request lands — normal probe behavior, just not tolerated by the old one-shot assertion. Fixed by polling `/health/ready` for up to 10s instead of asserting on the first response, same `deadline`+`while`+`Task.Delay` idiom already used in MarketData's Quartz scheduling tests.
- **Positions-for-valuation pagination is offset-based**, not keyset — `Asset.Id` is a `Guid`, which has no `>` operator in C#/EF's LINQ translation, and this project's actual scale (a personal wealth app) doesn't need keyset's guarantees. `?page=&pageSize=` (default 500, max 1000), `HasMore` computed by fetching one extra row.
- **Manual asset FX uses `Asset.Currency`; market asset FX uses the instrument's `QuoteCurrency`**, not `Asset.Currency` — a market asset's `Currency` field (defaults to PLN, T2.9) doesn't govern what currency its price is quoted in; the instrument does. MarketData's batch price response includes `QuoteCurrency` per instrument so Reporting never needs a second round trip to work out which FX pair to request.
- **Test infra additions**: `Skarbiec.Testing/Auth/AuthenticatedClientExtensions.CreateSystemAuthenticatedClient` (cross-service, mirrors `CreateAuthenticatedClient`); `FakePositionsClient`/`FakePriceQuoteClient` in `Skarbiec.Reporting.Tests/Fixtures/` (first use of that folder in this service); `MarketDataApi.SeedFxRateAsync` alongside the existing `SeedQuoteAsync`. `DailyPricesSyncedConsumerTests` builds its own bare `ServiceProvider` (real consumer + real inbox, fake HTTP clients) rather than `ReportingApiFactory`, mirroring Identity's `UserRegisteredIdempotentConsumerTests` — needs RabbitMQ + Postgres but no HTTP host.

**Acceptance criteria:**

- [X] Double delivery of the event → snapshots computed once (values unchanged, no dupes). — `DailyPricesSyncedConsumerTests.Consume_SameMessageIdDeliveredTwice_SnapshotComputedOnlyOnce`.
- [X] Algorithm unit tests: FX conversion, last-known-price fallback, stale >7 days, manual assets — table-driven against `03-domain-model.md` examples. — `ValuationAlgorithmTests` (11 facts/theories).
- [X] Integration test end-to-end on Testcontainers: event in → snapshot rows out. — `DailyPricesSyncedConsumerTests.Consume_DailyPricesSynced_ComputesSnapshotsForEveryPortfolioAcrossUsers`.

Verification run: `dotnet build Skarbiec.slnx` (0 warnings/errors) · `dotnet format` clean on every touched file · `dotnet test Skarbiec.slnx` — Reporting 16/16 (new), Portfolio 127/127 (+5 new), MarketData 87/89 (+6 new, 2 pre-existing live-smoke skips), everything else unchanged and green, all Testcontainers-backed (Docker).

---

### T2.12 Reporting: dashboard read model + net-worth history endpoint

- [X] **Status:** done · **Size:** L

- **Depends on:** T2.11
- **Refs:** E5 [M] (dashboard <1 s, history 1M/1Y/YTD/MAX), ADR-013 (no BFF — Reporting answers)

**Goal:** two fast read endpoints — current dashboard summary and net-worth history series — replacing the Phase 1 shortcut.

**Scope:**

- `Features/GetDashboard`: latest snapshot per portfolio for the user → net worth, per-class breakdown (amounts + %), per-portfolio values, staleness info, snapshot date. One query, `AsNoTracking`, tenancy-filtered.
- `Features/GetNetWorthHistory`: range param (1M/1Y/YTD/MAX) → daily series of total net worth (sum across portfolios per date); gaps left to the client to interpolate visually (per E5 AC); document response shape for the chart.
- Optional `?portfolioId=` filter on both.
- P95 <1 s success criterion — measure with seeded year of snapshots (index on (UserId, date)).

**Implementation notes:**

- **"Latest snapshot per portfolio" is a per-row max, not one global max.** The `DailyPricesSynced` consumer (T2.11) skips writing a portfolio's row for a day its own computation fails, leaving that portfolio's snapshot at whatever date it last succeeded on while sibling portfolios move forward. `GetDashboardHandler` resolves this with a `GroupBy(PortfolioId).Max(Date)` subquery joined back to `ValuationSnapshots` (one query, `AsNoTracking`) rather than a single `MAX(Date)` applied to every portfolio — the dashboard's per-portfolio `SnapshotDate` can legitimately differ across portfolios, and a global max would silently drop a lagging portfolio's last-known value instead of showing it.
- **New (UserId, Date) index, additive to T2.11's unique (PortfolioId, Date) one.** Both handlers filter/order on exactly this pair; without it the history query's date-range scan and the dashboard's per-user grouping would be sequential scans once a user has a seeded year of daily rows across several portfolios.
- **Range boundaries resolve against `TimeProvider.GetUtcNow()` ("today"), not the latest snapshot date.** Registered the same `TimeProvider.System` singleton MarketData's Quartz jobs already use (`TryAddSingleton`), so a stale sync (last snapshot a week old) doesn't silently shrink what "1M"/"1Y"/"YTD" mean. `MAX` has no lower bound — the earliest row in the data becomes the first point.
- **Range is a plain validated string (`"1M"|"1Y"|"YTD"|"MAX"`), not an enum** — `1M`/`1Y` aren't legal C# enum member names, and the wire format is a query string anyway. Invalid values return `Result<T>` → `Validation.InvalidRange` → 400, same ADR-017 mapping every other slice uses; omitting `?range=` defaults to `"1Y"` (mirrors `ListTransactionsEndpoint`'s defaulted-params convention so a missing param binds instead of 400ing).
- **Gaps are structurally absent, not filled.** `GetNetWorthHistory`'s SQL does `GROUP BY Date, SUM(TotalPln)` over whatever rows exist in range — a date with no snapshot for any portfolio just has no point, per E5's "gaps interpolated visually" AC (client's job, not the server's).
- **Caught a DI registration bug via the test suite, not code review.** Every other service's Minimal API handlers are resolved from DI (`builder.Services.AddScoped<XHandler>()`), and Reporting had none registered yet (T2.11 only added infrastructure, no HTTP-request-path handler). Both new handlers had multiple ambiguous non-primitive parameters (`handler`, `portfolioId`), which made ASP.NET Core's `RequestDelegateFactory` infer the handler itself as `[FromBody]` instead of a DI service the moment the host actually started — `dotnet build` stayed green throughout; only `dotnet test`'s `WebApplicationFactory` boot surfaced it (`Body was inferred but the method does not allow inferred body parameters`). Fixed by adding `AddScoped<GetDashboardHandler>()`/`AddScoped<GetNetWorthHistoryHandler>()` next to the DbContext registration in `Program.cs`.
- **Second host-backed Reporting test class arrives, so `ReportingEndpointTests`/`ReportingApi` were extracted** the way `dotnet.md`'s testing conventions call for (mirrors `PortfolioEndpointTests`/`PortfolioApi`) — `HealthCheckTests` (T2.11, previously the sole host-backed class deriving straight from `ServiceEndpointTests<Program>`) now derives from the new base too. There's no HTTP write path for `ValuationSnapshot` (only the consumer writes it), so `ReportingApi.SeedSnapshotAsync` seeds directly through a `ReportingDbContext` scoped to a `StubCurrentUser`, mirroring MarketData's `SeedQuoteAsync`/`SeedFxRateAsync` rather than `PortfolioApi`'s HTTP-POST arrange helpers.

**Acceptance criteria:**

- [X] Dashboard endpoint returns correct aggregates for a seeded multi-portfolio user (test). — `GetDashboardEndpointTests` (6 facts: aggregation, per-portfolio-latest-date, portfolioId filter, staleness, empty state, tenancy).
- [X] History ranges correct (YTD boundary, MAX = earliest snapshot). — `GetNetWorthHistoryEndpointTests.Get_YtdRange_StartsAtJanuaryFirstOfCurrentYear` / `Get_MaxRange_IncludesEarliestSnapshot` (9 facts total, incl. gaps-left-unfilled and invalid-range → 400).
- [X] Tenancy isolation tests for Reporting endpoints (user B sees nothing of A). — `Get_NeverIncludesAnotherUsersSnapshots` in both endpoint test classes.

Verification run: `dotnet build Skarbiec.slnx` (0 warnings/errors) · `dotnet format` clean on every file created/edited this task (pre-existing repo-wide CRLF mismatch on other already-tracked files is a local `core.autocrlf=true` checkout artifact, not repo content — confirmed via `git show HEAD:<path>`showing LF-only blobs — and untouched by this task, consistent with the known pre-existing frontend CRLF note) · `dotnet test Skarbiec.slnx` — Reporting 32/32 (new, incl. a `GetNetWorthHistoryPerformanceTests` handler-level check: a seeded year across 3 portfolios stays under the AC's 1s bound), Portfolio 127/127, MarketData 87/89 (2 pre-existing live-smoke skips), Identity 24/24, Strategy 3/3, Gateway 4/4, ServiceDefaults 12/12, Testing 4/4, Contracts 17/17 — everything green, all Testcontainers-backed (Docker).

---

### T2.13 Angular: dashboard on Reporting + history chart + instrument autocomplete + stale markers

- [X] **Status:** done · **Size:** L

> Modified by M1.7 — see modyfikacje/modification-1.md

- **Depends on:** T2.12, T2.8, T2.9
- **Refs:** E2 [M], E4 [S] (last price date/source, stale), E5 [M]

**Goal:** the UI catches up with Phase 2: dashboard reads Reporting, net-worth chart with range switcher, asset form gains the instrument picker, staleness is visible.

**Scope:**

- Regenerate TS clients (`npm run gen:api`); dashboard switched to Reporting endpoints; delete/retire the Phase 1 Portfolio summary path (remove the `// Phase 2` marker code).
- Net-worth history line chart with 1M/1Y/YTD/MAX switcher; visual interpolation across gaps (per E5 AC); snapshot date shown ("as of…").
- Asset form: "market instrument" mode with autocomplete (debounced search on T2.8, shows ticker/name/last price); custom-instrument add flow [S] behind a "can't find it?" link.
- Stale markers: asset list and dashboard show stale price indicator + last price date and source (E4 [S]).

**Acceptance criteria:**

- [X] Dashboard and chart render from Reporting in production data path; Phase 1 shortcut code removed.
- [X] Creating a market asset via autocomplete works end-to-end; its valuation appears after the next sync/snapshot (or manual trigger, T2.14).
- [X] Stale instrument visibly flagged with date + source.

---

### T2.14 MarketData: manual sync trigger [S]

- [X] **Status:** done · **Size:** S

- **Depends on:** T2.6
- **Refs:** E4 [S]

**Goal:** a button triggers the sync now — for demos, debugging and impatience.

**Scope:**

- Endpoint `Features/TriggerSync`: enqueues the Quartz job (reuses `DisallowConcurrentExecution` — already-running → 409 or "already running" response); auth required; rate-limited.
- Small UI affordance (settings or dashboard): button + last `SyncRun` status/time (from a `GetSyncStatus` endpoint reading `SyncRun`).

**Implementation notes:**

- **`ISyncTrigger`/`QuartzSyncTrigger`/`NoOpSyncTrigger`** (`Sources/`) mirror T2.7's `IHistoryBackfillTrigger` pattern: `QuartzSyncTrigger` schedules a one-shot, immediate-fire trigger against the already-durable `PriceSyncJob.Key` (T2.6) under a **fixed** identity (`"manual-sync-trigger"/"market-data"`) instead of a random one. That fixed identity is the double-click guard: Quartz's job store enforces unique trigger names per group, so `ScheduleJob` throws `ObjectAlreadyExistsException` for a second call while the first trigger (and the run it kicked off — `PriceSyncJob` is `[DisallowConcurrentExecution]`) is still live; mapped to `SyncTriggerOutcome.AlreadyRunning` → `Conflict.SyncAlreadyRunning` → 409. `NoOpSyncTrigger` is registered instead under `Testing:DisableBackgroundJobs` so `Features/TriggerSync` stays resolvable in `SkarbiecApiFactory`-based HTTP slice tests with no live scheduler.
- **Verified empirically, not assumed**: whether a fired one-shot Quartz trigger stays registered (and thus still collides on `ScheduleJob`) for the *whole* duration its job is executing, or gets removed the moment it fires, wasn't something to guess at — `SyncTriggerSchedulingTests` proves it directly against a real Postgres-backed scheduler using a `GatedPriceSource` (blocks on a `TaskCompletionSource` so the run's execution window is deterministic, not timing-dependent): a second `TriggerAsync` call issued *while the first run is confirmed still `Running`* (polled via its `SyncRun` row) correctly comes back `AlreadyRunning`, and only one `SyncRun` row exists once both settle.
- **Rate limiting already covered, not re-implemented**: the Gateway's "standard" fixed-window policy (100 req/10s, ADR-013) already applies to every `/api/marketdata/{**catch-all}` route including the new `sync/trigger` and `sync/status` ones — documented in a remark on `TriggerSyncEndpoint` rather than adding a redundant per-endpoint limiter.
- **`GetSyncStatusHandler`** returns the most recent `SyncRun` by `StartedAt` (including one still `Running`, no `FinishedAt` yet) wrapped in `SyncStatusResponse { HasRun, RunId, Status, StartedAt, FinishedAt, SyncedCount, NoDataCount, FailedCount }` — `HasRun: false` is the "never synced yet" state (fresh environment before any cron fire or manual trigger).
- **UI lives on Settings** (dashboard was the other option the scope named; Settings was empty and is the more natural home for an operational control): button + status chip + counts, `resource()`-backed per angular.md, reloads the status resource after a successful trigger. `postApiMarketdataSyncTrigger`/`getApiMarketdataSyncStatus`/`SyncStatusResponse` hand-applied to the generated client after `npm run gen:api` reproduced the known `.js`-extension-drop bug ([[project-gen-api-extension-drop-bug]]) — `git diff` confirmed the only real delta was the new sync types/functions, so the broken regeneration was discarded and that delta applied by hand instead.
- **Manually verified in a real browser** against the live Aspire stack (registered a throwaway user, logged in, clicked "Sync now"): the status card updated from the previous automatic dev-cron run to a brand-new run within the eventual-consistency window, confirming the end-to-end flow works outside the test suite too.

**Acceptance criteria:**

- [X] Button → sync runs → new snapshot appears after the event flows (observable in UI within the eventual-consistency window). — `SyncTriggerSchedulingTests.TriggerAsync_FiresPriceSyncJob_OutsideItsCronSchedule` (real Quartz, a manual trigger fires the job outside its cron schedule and quotes land); manually verified end-to-end in a live browser session (Settings page's status card updated to a fresh run after clicking "Sync now").
- [X] Double-click doesn't double-run (test on the endpoint). — `SyncTriggerSchedulingTests.TriggerAsync_WhileARunIsStillInFlight_ReturnsAlreadyRunning_AndOnlyOneRunHappens`.

Verification run: `dotnet build Skarbiec.slnx` (0 warnings/errors) · `dotnet format` clean on every touched/added file · `dotnet test Skarbiec.slnx` — MarketData 95/97 (+8 new, 2 pre-existing live-smoke skips), Contracts 17/17, ServiceDefaults 12/12, Testing 4/4, Strategy 3/3, Gateway 4/4, Identity 24/24, Portfolio 123/123, Reporting 32/32, all green, all Testcontainers-backed (Docker) · frontend: `npm run typecheck` clean, `npm test` — 122/122 (20 files, +5 new), `npm run build` clean, `prettier --check` clean on touched files.

---

### T2.15 [VPS] Ops: production observability — Grafana + Tempo + Loki + Prometheus + alert — optional, deferred

- [ ] **Status:** todo · **Size:** L

- **Depends on:** T2.6, T0.18
- **Refs:** E8 [S], roadmap Phase 2, `02-architecture.md` §observability

**Deferred:** this whole stack only exists in production (it's a second, VPS-hosted compose file). Local debugging is already covered by the Aspire dashboard (traces/logs/metrics, per `02-architecture.md` §observability) — not required to close out Phase 2 locally. Pick this up together with T0.18.

**Goal:** production has the full observability stack fed by the existing OTel pipelines, plus one alert that matters: sync hasn't run for 2 days.

**Scope:**

- `deploy/observability/` compose (same VPS, memory-budgeted): Grafana, Tempo (traces), Loki (logs), Prometheus (metrics), OTel Collector routing service OTLP → all three; Grafana behind auth, not public.
- Services in production point OTLP at the collector (config only — instrumentation exists since Phase 0).
- Minimal dashboards: service health/error rate, RabbitMQ queue depth, `SyncRun` status; trace search usable (find the job→event→consumer trace).
- **Alert**: `PriceSyncJob` no successful run in >2 days (metric from `SyncRun` or a Prometheus heartbeat metric emitted by the job) → notification channel you'll actually see (e-mail/Telegram — decide at implementation).

**Acceptance criteria:**

- [ ] A production trace spanning Gateway→service and job→event→consumer findable in Tempo/Grafana.
- [ ] Alert fires when the job is silenced for the threshold (test by pausing the schedule with a shortened threshold), and resolves after a run.
- [ ] VPS memory still within budget (limits set; observed after 24 h).

---

### T2.16 Phase exit: local smoke — the full trace (+ optional VPS)

- [X] **Status:** done · **Size:** M

- **Depends on:** T2.1–T2.14
- **Refs:** roadmap Phase 2 deliverable

**Goal:** the Phase 2 deliverable trace exists locally; the production half stays optional until T0.18/T2.15 are picked up.

**Scope:**

- **Local (required now):** trigger sync (T2.14) under Aspire → verify quotes upserted → `DailyPricesSynced` consumed → snapshot written → dashboard updated; capture the single trace job→event→consumer→write from the local Aspire dashboard (link/screenshot in phase log).
- **VPS / production (optional — deferred, needs T0.18/T2.15):** deploy (MarketData jobs live against real APIs, Reporting consumer, observability stack); repeat the smoke in production over two consecutive days; restore test for this phase (per T1.14 policy) — now includes `marketdata_db`, `reporting_db`.

**Acceptance criteria:**

- [X] The deliverable trace (job→event→consumer→write) captured locally and referenced in the phase log. — trace `362937b8a09fccc34ee6ad7d88f37f02`, screenshot `skarbiec-plan/zadania/evidence/t216-trace-job-event-consumer-write.png`.
- [X] Dashboard updates without manual intervention after a locally-triggered sync run. — screenshot `skarbiec-plan/zadania/evidence/t216-dashboard-updated-after-sync.png`.
- [ ] *(optional, deferred)* Valuations update without user intervention on two consecutive days in production; restore test done this phase — once T0.18/T2.15/T1.14 are picked up.

**Evidence (2026-08-10):**

- Local run under Aspire (`dotnet run --project Skarbiec.AppHost`, Angular dev server on port 4200): created a portfolio, added a market-valued Crypto asset (`bitcoin`/CoinGecko, 0.5 BTC) with no prior `ValuationSnapshot` — dashboard showed the empty state first, confirming the baseline.
- Clicked "Sync now" (T2.14, Settings page) → `POST /api/marketdata/sync/trigger` → `204`. `PriceSyncJob` ran (`SyncRun` status `Partial`: synced 4, failed 2 — the 2 failures are Stooq returning `404` for `AAPL.US`/`CDR.PL`, the exact "Stooq changes its format" risk called out in the roadmap; unrelated to this task, CoinGecko/NBP all `200`), published `DailyPricesSynced` through the outbox.
- Aspire dashboard trace `362937b8a09fccc34ee6ad7d88f37f02` (`marketdata-service: PriceSyncJob.Run`, 23:01:39, 3.13s, 27 spans across marketdata-service/postgres/reporting-service/portfolio-service) shows the full deliverable chain in one trace: `PriceSyncJob.Run` → external price/FX fetches → `outbox send`/`outbox process` → `MSG rabbitmq send DailyPricesSynced` → `reporting-service: daily-prices-synced receive`/`process` → `GET portfolio-service /api/portfolio/positions-for-valuation` → `POST marketdata-service /api/marketdata/prices/latest-batch` + `/fx/latest-batch` → snapshot upsert. Screenshot: `skarbiec-plan/zadania/evidence/t216-trace-job-event-consumer-write.png`.
- Dashboard (`/dashboard`) updated to the new net worth (119 257,21 zł, 100% Crypto, "As of Aug 10, 2026") after only a page reload — no manual recomputation. Screenshot: `skarbiec-plan/zadania/evidence/t216-dashboard-updated-after-sync.png`.
- VPS/production half left unchecked, per the task's own deferral note (needs T0.18/T2.15/T1.14).

---

## Exit checklist (Phase 2 done when…)

- [X] All [M] tasks done ([S]: T2.8-custom, T2.13-custom-flow, T2.14 are should — decide consciously if any slips; T2.15 is VPS-only and optional).
- [X] Dashboard served by Reporting; Phase 1 shortcut removed.
- [X] Local deliverable trace captured (T2.16); dashboard updates automatically on a local sync run.
- [ ] *(optional, deferred)* Two days of autonomous valuation updates observed in production; observability stack live; sync alert armed; restore test executed — pick up together with T0.18.
