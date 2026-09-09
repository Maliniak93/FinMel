# Phase 1 — MVP: wealth entered manually

**Duration:** 5–7 weeks.
**Goal:** the whole personal wealth can be entered and seen: portfolios, assets (manual valuation), transactions driving quantities, first dashboard — all through Angular talking to the Gateway.
**Deliverable (from roadmap):** your entire wealth entered; a second user sees none of it (isolation tests).
**Sources:** roadmap Phase 1, E1/E2/E3/E5(part)/E8, ADR-006/008/009/013.

## Task index

| ID    | Task                                              | Size | Depends on  |
| ----- | ------------------------------------------------- | ---- | ----------- |
| T1.1  | Portfolio: portfolio CRUD                         | M    | Phase 0     |
| T1.2  | Portfolio: assets with manual valuation           | M    | T1.1        |
| T1.3  | Portfolio: record transactions                    | L    | T1.2        |
| T1.4  | Portfolio: edit/delete transaction + recompute    | M    | T1.3        |
| T1.5  | Events:`AssetChanged`, `TransactionRecorded`  | M    | T1.2, T1.3  |
| T1.6  | Tenancy isolation tests in Portfolio              | M    | T1.1–T1.4  |
| T1.7  | OpenAPI + TS client generation                    | M    | T1.1        |
| T1.8  | Angular: workspace, layout, routing               | L    | T0.16       |
| T1.9  | Angular: auth flow                                | L    | T1.8        |
| T1.10 | Angular: portfolio views                          | M    | T1.7, T1.9  |
| T1.11 | Angular: asset views                              | L    | T1.10       |
| T1.12 | Angular: transaction views                        | L    | T1.11       |
| T1.13 | Dashboard v1 (from Portfolio)                     | M    | T1.11       |
| T1.14 | [VPS, optional] Backups: pg_dump + cron + restore | M    | T0.18       |
| T1.15 | Phase exit: local smoke (+ optional VPS deploy)   | S    | T1.1–T1.13 |
| T1.16 | MVP success criterion: < 30 min data entry        | S    | T1.15       |

---

### T1.1 Portfolio: portfolio CRUD

- [X] **Status:** done · **Size:** M

> Modified by M1.3 — see modyfikacje/modification-1.md

- **Depends on:** Phase 0 (T0.13 skeleton, T0.14 tenancy plumbing)
- **Refs:** E2 [M], ADR-002, ADR-006

**Goal:** users create, edit, list and archive portfolios; archive replaces delete when assets exist.

**Scope:**

- Slices in `Features/`: `CreatePortfolio`, `UpdatePortfolio`, `ListPortfolios`, `GetPortfolio`, `ArchivePortfolio` — each endpoint + handler + validator, `TypedResults`, ProblemDetails.
- Entity `Portfolio`: `UserId` (stamped by interceptor), name (required, unique per user — decide and document), description, currency (default PLN).
- Archive semantics: portfolio with ≥1 asset cannot be hard-deleted — archive flag; archived portfolios excluded from default lists, included with `?includeArchived=true`.
- `AsNoTracking` on all reads.

**Decisions made during implementation:**

- A 6th slice, `DeletePortfolio` (DELETE verb), was added alongside `ArchivePortfolio` (POST `.../archive`) — the scope's "archive replaces delete when assets exist" and the AC's explicit 409 both require a real hard-delete endpoint distinct from the explicit archive action.
- `Asset` doesn't exist yet (T1.2, which depends on this task), so the has-assets guard can't query a real Asset table. Added a denormalized `AssetCount` counter on `Portfolio` (default 0); `DeletePortfolio` checks `AssetCount > 0` → 409. T1.2's `AddAsset`/`RemoveAsset` must increment/decrement it. Confirmed with the user before implementing.
- Name uniqueness per user: case-sensitive exact match, enforced by a DB unique index on `(UserId, Name)` plus a pre-check in the handler.
- The `Portfolio` entity name collides with this project's own root namespace (`Skarbiec.Portfolio`) — referencing it unqualified outside `Skarbiec.Portfolio.Data` throws CS0118. Fixed via a project-wide `<Using Include="Skarbiec.Portfolio.Data.Portfolio" Alias="PortfolioEntity" />` in both csproj files instead of per-file aliasing.

**Bugs found and fixed in shared infrastructure (`Skarbiec.ServiceDefaults`) while verifying this task's AC:**

- Minimal API body binding throws `BadHttpRequestException` (not just returning 400 directly) only when `IWebHostEnvironment.IsDevelopment()` is true — which is every Skarbiec test host (`SkarbiecApiFactory` always sets `Development`) and local `dotnet run`. The existing plain `app.UseExceptionHandler()` treated this as an unexpected failure and flattened it to a generic 500, breaking "request validation → 400 automatically" (ADR-017) specifically for requests missing a `required` JSON property. Fixed in `Extensions.UseServiceDefaults()` by passing `ExceptionHandlerOptions.StatusCodeSelector` (built-in since .NET 9, purpose-built for exactly this: "choose what status code to return when an exception occurs" — mapping `BadHttpRequestException` to its own `StatusCode`, else 500). Verified this also corrects the ProblemDetails response body's `status` field, not just the HTTP header (`Create_WithNamePropertyMissingEntirely_ReturnsBadRequest`). Simpler than an initial custom `IExceptionHandler` version, which this replaced. This also affects Identity's existing slices (untested there, since no existing test omits a required property) — worth a follow-up look, not done here since it's outside this task's scope.
- `ListPortfoliosEndpoint`'s `includeArchived` query parameter needed an explicit `= false` default — a non-nullable Minimal API parameter with no default is treated as *required*, so an omitted `?includeArchived=` 400ed instead of defaulting.

**Acceptance criteria:**

- [X] Full CRUD works through the Gateway with a real JWT. — Verified via `Skarbiec.Portfolio.Tests` (WebApplicationFactory + `TestJwtIssuer`-minted JWTs), all 6 endpoints (Create/Get/List/Update/Delete/Archive), `dotnet test` green. Not exercised through the actual YARP process — no existing test in this repo does that; matches the established convention (see T0.15's own AC, verified the same way).
- [X] Deleting a portfolio containing assets → 409 with a ProblemDetails message pointing to archive. — `DeletePortfolioEndpointTests.Delete_PortfolioContainingAssets_ReturnsConflictPointingToArchive`, using the `AssetCount` counter (see decision above) since `Asset` doesn't exist yet.
- [X] Slice integration tests (Testcontainers) for create/list/archive; validation errors → 400 with field details. — 24 tests across 6 files, all green (`dotnet test`); `Create_WithEmptyName_ReturnsBadRequestWithFieldDetails` asserts the `ValidationProblemDetails.Errors["Name"]` entry specifically.

---

### T1.2 Portfolio: assets with manual valuation

- [X] **Status:** done · **Size:** M

> Modified by M1.3 — see modyfikacje/modification-1.md
> Modified by M1.4 — see modyfikacje/modification-1.md
> Modified by M1.5 — see modyfikacje/modification-1.md

- **Depends on:** T1.1
- **Refs:** E2 [M] (any class, manual valuation), ADR-008, `03-domain-model.md` §valuation modes

**Goal:** an asset of any class can be added with a manual value and valuation date (market instruments come in Phase 2).

**Scope:**

- Slices: `AddAsset`, `UpdateAsset`, `ListAssets` (per portfolio), `GetAsset`, `RemoveAsset` (allowed only when no transactions; otherwise 409 — transactions are the source of truth, ADR-009).
- Entity `Asset`: `PortfolioId`, `AssetClass`, name, currency, `Quantity`, `ManualValue` (Money, explicit decimal precision) + `ManualValueDate` (`DateOnly`); `InstrumentId` nullable Guid — **column exists now** (no FK, ADR-003) but stays unused until Phase 2.
- Validation: manual mode requires `ManualValue` + date; amounts ≥ 0.
- Stale-manual-value support: expose `ManualValueDate` so the UI can nag (the reminder itself is UI, T1.11).

**Decisions made during implementation:**

- Routes nest under the portfolio (`/api/portfolio/portfolios/{portfolioId}/assets[/{id}]`) rather than a flat `/api/portfolio/assets/{id}` — Asset is a sub-resource of Portfolio and `ListAssets` needs the portfolio scope anyway; `GetAsset`/`UpdateAsset`/`RemoveAsset` all require the id to match both `assetId` and the route's `portfolioId` (a mismatch → 404, not just "asset not found").
- `Asset.TransactionCount` denormalized counter added (mirrors T1.1's `Portfolio.AssetCount` decision) — `Transaction` doesn't exist yet (T1.3, which depends on this task), so `RemoveAsset`'s "no transactions" guard can't query a real table. T1.3's `RecordTransaction`/`DeleteTransaction` must increment/decrement it.
- `ManualValue` is persisted as a plain `decimal` column (`ManualValueAmount`, `HasPrecision(18, 2)`) reusing `Asset.Currency` rather than as an EF owned/complex type — there's no existing precedent in this codebase for persisting the `Money` value object, and the domain model already gives every `Asset` a single `Currency` field, so a second currency code on the money amount would be redundant. `Money.Create(amount, currency)` is still used in both `AddAsset`/`UpdateAsset` handlers to get its negative-amount and decimal-precision validation (ADR-008) before storing `.Amount`.
- `Quantity` is a request field (not transaction-derived yet, since T1.3 doesn't exist) — defaults to 0 when omitted, validated `>= 0` via `[Range]` on the request record (simple field constraint, 400 automatically) rather than through `Result`/`Money`, since it isn't a `Money` value.
- `AddAsset`/`RemoveAsset` increment/decrement `Portfolio.AssetCount`, closing the loop T1.1 left open (its own tests had to seed `AssetCount` directly since `Asset` didn't exist yet).

**Acceptance criteria:**

- [X] Asset of every `AssetClass` can be created with a manual value in any currency. — `AddAssetEndpointTests.Add_EveryAssetClassWithManualValue_ReturnsCreated` (`[Theory]` over all 9 `AssetClass` values, USD currency), `dotnet test` green.
- [X] Negative value/quantity → 400; delete with transactions → 409. — `Add_WithNegativeManualValue_ReturnsBadRequest`, `Add_WithNegativeQuantity_ReturnsBadRequestWithFieldDetails`, `Update_WithNegativeManualValue_ReturnsBadRequest`; `RemoveAssetEndpointTests.Remove_AssetWithTransactions_ReturnsConflict` (seeds `TransactionCount`, same technique T1.1 used for `AssetCount`).
- [X] Integration tests cover add/update/list per portfolio. — `AddAssetEndpointTests`, `UpdateAssetEndpointTests` (incl. `Update_AssetUnderWrongPortfolio_ReturnsNotFound`), `ListAssetsEndpointTests.List_ReturnsOnlyAssetsOfThatPortfolio`; 28 new tests across 5 files, all green.

**Verification run:** `dotnet build` (whole repo, 0 warnings) · `dotnet test` (whole repo: 137/137 green, incl. `Skarbiec.Portfolio.Tests` 52/52 — 24 pre-existing + 28 new) · `dotnet format --verify-no-changes` clean.

---

### T1.3 Portfolio: record transactions

- [X] **Status:** done · **Size:** L

- **Depends on:** T1.2
- **Refs:** E3 [M], ADR-009, `03-domain-model.md` §invariants

**Goal:** buy/sell/deposit/withdraw/dividend/interest/fee transactions are recorded and the asset quantity is recomputed from transactions.

**Scope:**

- Slice `RecordTransaction` + `ListTransactions` (per asset, newest first, paged).
- Entity `Transaction`: `AssetId`, type (`Buy`, `Sell`, `Deposit`, `Withdraw`, `Dividend`, `Interest`, `Fee`), quantity, unit price (Money), fee (Money, ≥ 0), date.
- **Recomputation as the single code path** ("light" event sourcing): one function derives `Asset.Quantity` from the full transaction list; used by record, edit and delete (T1.4). Which types affect quantity (Buy/Sell/Deposit/Withdraw) vs value-only (Dividend/Interest/Fee) — document in the handler.
- Invariant: a `Sell`/`Withdraw` that would take quantity below 0 → 400 ProblemDetails ("selling more than the position").
- Manual-valuation assets may exist without transactions (exception per domain model).

**Decisions made during implementation:**

- `TransactionType` (Buy/Sell/Deposit/Withdraw/Dividend/Interest/Fee) added to `Skarbiec.Contracts` alongside `AssetClass` — T1.5's `TransactionRecorded` event will need the same enum.
- `Transaction.UnitPriceAmount`/`FeeAmount` reuse the owning `Asset.Currency` rather than storing a redundant currency column — mirrors T1.2's `Asset.ManualValueAmount` decision.
- `TransactionQuantityCalculator.Recompute(IEnumerable<Transaction>)` is a public, pure, DB-free function in `Features/` (not nested under `RecordTransaction/`) since the scope explicitly names it as shared across record/edit/delete (T1.4) — replays the *full* history ordered by `(Date, Id)` (Id is an arbitrary but deterministic same-day tiebreak, undocumented by any AC) and fails on the first point the running quantity would go negative, not just at the final total. `RecordTransactionHandler` calls it over existing-transactions-plus-candidate rather than incrementing `Asset.Quantity` in place, so a backdated transaction is validated against the whole timeline.
- Routes nest three levels deep (`/api/portfolio/portfolios/{portfolioId}/assets/{assetId}/transactions`), mirroring T1.2's Asset-under-Portfolio nesting; `RecordTransaction`/`ListTransactions` both require `assetId` to match `portfolioId` (mismatch → 404, same convention as `UpdateAsset`/`RemoveAsset`).
- `PagedResponse<TItem>` added local to `Skarbiec.Portfolio.Features` (not `Skarbiec.Contracts`) — first paged endpoint in the codebase, kept service-local until a second consumer justifies promoting it (ADR-002/004 "extract on third use").
- `ListTransactions` paging (`page`/`pageSize` query params, defaults 1/20, `pageSize` clamped to 100) implemented as plain method parameters with defaults (mirrors `ListPortfoliosEndpoint`'s `includeArchived` pattern) rather than an `[AsParameters]` validated record, to avoid depending on unverified .NET 10 validation-binding interaction for query parameters; not covered by an AC, so kept minimal.
- Generated the EF migration via `dotnet ef migrations add` (design-time factory already existed from T1.2); `dotnet format` was needed afterward to fix the tool's default CRLF line endings/block-scoped namespace in the generated migration file (`.editorconfig` requires LF + file-scoped namespaces) — no functional changes.

**Acceptance criteria:**

- [X] Recording each type produces the expected quantity (table-driven unit tests on the recompute function). — `TransactionQuantityCalculatorTests.Recompute_EachTransactionType_ProducesExpectedQuantity` (`[Theory]` over all 7 `TransactionType` values), plus `Recompute_EmptyHistory_ProducesZero`, `Recompute_SellExactlyTheWholePosition_ProducesZero`, `Recompute_ReplaysOutOfInputOrder_ByDateRegardlessOfListOrder` — pure unit tests, no Testcontainers (mirrors `ArchitectureTests`' style); `dotnet test` green.
- [X] Oversell → 400; quantity never negative (property-style test welcome). — Unit: `Recompute_SellMoreThanPosition_Fails`, `Recompute_WithdrawMoreThanPosition_Fails`, `Recompute_ABackdatedSellThatDipsBelowZeroMidHistory_Fails` (mid-history dip, not just final total), `Recompute_RandomSequences_NeverProducesNegativeQuantity` (100 runs × 20 random transactions, seeded `Random(42)`, asserts `result.Value >= 0` whenever `IsSuccess`). Integration: `RecordTransactionEndpointTests.Record_SellMoreThanPosition_ReturnsBadRequest` (also asserts `Asset.Quantity` unchanged after the rejected sell).
- [X] Integration test: sequence buy→sell→dividend yields correct quantity and history. — `RecordTransactionEndpointTests.Record_BuySellDividendSequence_YieldsCorrectQuantityAndHistory`: Buy 10 → Sell 4 → Dividend, asserts `Asset.Quantity == 6` via `GetAsset` and `ListTransactions` returns all 3 newest-first (Dividend, Sell, Buy) with correct `TotalCount`.

**Verification run:** `dotnet build` (whole repo, 0 warnings) · `dotnet test` (whole repo green: Portfolio 77/77 — 52 pre-existing + 25 new across `TransactionQuantityCalculatorTests`, `RecordTransactionEndpointTests`, `ListTransactionsEndpointTests`; other services unaffected) · `dotnet format --verify-no-changes` clean.

---

### T1.4 Portfolio: edit/delete transaction with recomputation

- [X] **Status:** done · **Size:** M

- **Depends on:** T1.3
- **Refs:** E3 [M]

**Goal:** editing or deleting any historical transaction recomputes the position and re-validates invariants over the whole history.

**Scope:**

- Slices `UpdateTransaction`, `DeleteTransaction` — reuse the T1.3 recompute function over the full ordered history.
- Edge case: an edit/delete that makes a *later* sell invalid (quantity would dip below 0 at some point in history) → 409 with an explanatory ProblemDetails.
- Concurrency: recompute inside one transaction; note on optimistic concurrency (rowversion/xmin) if two edits race — implement the simple safe option, document.

**Decisions made during implementation:**

- `TransactionQuantityCalculator.Recompute` (T1.3) gained an optional `Func<TransactionType, Error> oversellError` parameter instead of a hardcoded error — RecordTransaction's oversell is a 400 validation failure on new input (unchanged default, `TransactionErrors.OversellsPosition`), but the *same* invariant break caused by UpdateTransaction/DeleteTransaction is a 409 conflict with already-recorded history (scope's explicit AC), so it needed a distinct `Error` code (`Conflict.OversellsPosition` via the new `TransactionErrors.MutationBreaksHistory`) mapped by category prefix (ADR-017's `ResultHttpExtensions.MapStatus`). RecordTransactionHandler itself is untouched — it still gets the 400 behavior via the default parameter.
- Both handlers validate by recomputing over "the rest of the history + the candidate edit/removal" **before** touching the tracked `Transaction`/`Asset` entities — a rejected edit/delete must leave the database untouched (AC). Only after `Recompute` succeeds are the tracked entities mutated and `SaveChangesAsync` called.
- Concurrency: Postgres's own `xmin` system column, mapped as a shadow `uint` property (`HasColumnName("xmin")`, `HasColumnType("xid")`, `ValueGeneratedOnAddOrUpdate()`, `IsConcurrencyToken()`) on both `Asset` and `Transaction` in `PortfolioDbContext.OnModelCreating` — chosen over an app-managed GUID/rowversion column because Postgres bumps `xmin` automatically on every `UPDATE` regardless of which handler runs, so RecordTransaction (T1.3) is protected too without touching that file, and there's no "forgot to bump the token" failure mode. `UpdateTransactionHandler`/`DeleteTransactionHandler` catch `DbUpdateConcurrencyException` from `SaveChangesAsync` and map it to a new `Conflict.ConcurrentModification` error (409). `SaveChangesAsync` already wraps the Transaction + Asset writes in one implicit transaction (EF Core default), satisfying "recompute inside one transaction" without an explicit `BeginTransactionAsync`.
- The `dotnet ef migrations add` scaffolder doesn't know `xmin` is a reserved Postgres system column and generated `AddColumn`/`DropColumn` operations for it, which fail against real Postgres ("column name \"xmin\" conflicts with a system column name"). Emptied both migration methods by hand (documented inline in the migration file) — the column already exists on every row, so there's nothing to actually migrate; the migration exists only so `PortfolioDbContextModelSnapshot` picks up the new concurrency-token mapping for future `dotnet ef migrations add` diffs.
- Routes follow the established three-level nesting (`/api/portfolio/portfolios/{portfolioId}/assets/{assetId}/transactions/{id}`, PUT/DELETE) with the same `assetId` vs `portfolioId` mismatch → 404 convention as every other nested slice.

**Acceptance criteria:**

- [X] Editing an old buy downward that breaks a later sell → 409, nothing changed. — `UpdateTransactionEndpointTests.Update_OldBuyDownwardThatBreaksLaterSell_ReturnsConflictAndNothingChanged` (Buy 10 → Sell 8, edit the Buy down to 5 → 409; asset quantity still 2, the buy transaction's quantity still 10 via `ListTransactions`).
- [X] Delete + recompute correct (integration test with a 4-transaction history). — `DeleteTransactionEndpointTests.Delete_FromFourTransactionHistory_RecomputesAssetCorrectly` (Buy 10, Buy 5, Sell 3, Dividend → delete the Sell → asset quantity 15, 3 transactions remain). The edge case also applies to delete: `Delete_ThatBreaksLaterSell_ReturnsConflictAndNothingChanged` (Buy 10 → Sell 8, deleting the Buy → 409, nothing changed).
- [X] Quantity always equals recompute-from-scratch after any mutation (assert in tests). — `AssertQuantityMatchesRecomputeFromScratchAsync` helper (both endpoint test files): fetches the full transaction history back through `ListTransactions`, feeds it through `TransactionQuantityCalculator.Recompute` independently, and asserts it matches the asset's reported `Quantity`; called after the Update and Delete happy-path tests.

**Verification run:** `dotnet build` (whole repo, 0 warnings) · `dotnet test` (whole repo green: Portfolio 89/89 — 77 pre-existing + 12 new across `UpdateTransactionEndpointTests` (5), `DeleteTransactionEndpointTests` (6), `AssetConcurrencyTests` (1, proves the xmin token actually throws `DbUpdateConcurrencyException` on a genuine two-context race); other services unaffected, 173 tests total across the solution) · `dotnet format --verify-no-changes` (whole repo) clean.

---

### T1.5 Events: `AssetChanged`, `TransactionRecorded` through the outbox

- [X] **Status:** done · **Size:** M

> Modified by M1.5 — see modyfikacje/modification-1.md

- **Depends on:** T1.2, T1.3, T0.10
- **Refs:** ADR-012, `02-architecture.md` §services (published events)

**Goal:** Portfolio publishes its two domain events via the EF Outbox; contracts live in `Skarbiec.Contracts`.

**Scope:**

- Records in `Skarbiec.Contracts`: `AssetChanged` (AssetId, PortfolioId, UserId, kind: created/updated/removed), `TransactionRecorded` (TransactionId, AssetId, UserId, type, quantity, date). Keep payloads lean — consumers fetch details via API when needed (Reporting in Phase 2).
- Outbox wiring in Portfolio (same pattern as Identity, T0.10); publish from the relevant handlers in the same transaction.
- No consumers yet — that's fine; events flow to the broker (Reporting subscribes in Phase 2).

**Decisions made during implementation:**

- `AssetChanged`/`TransactionRecorded` publish only from `AddAsset`/`UpdateAsset`/`RemoveAsset` and `RecordTransaction` respectively — not from T1.4's `UpdateTransaction`/`DeleteTransaction` (no event is named for those, and this task's `Depends on` is T1.2/T1.3, not T1.4).
- `UserId` on both events comes from `PortfolioDbContext.CurrentUserId` (already exposed via `ITenantScopedDbContext`), not from the mutated entity's own `UserId` — for `AddAsset` the new `Asset.UserId` isn't stamped until `UserOwnedSaveInterceptor` runs inside `SaveChangesAsync`, which happens *after* `Publish` (ADR-012 requires publishing before/alongside that same call), so reading `asset.UserId` at publish time would have captured `Guid.Empty`.
- Portfolio's outbox tables are named `PortfolioInboxState`/`PortfolioOutboxMessage`/`PortfolioOutboxState` (via `AddInboxStateEntity(x => x.ToTable(...))` etc.), not MassTransit's bare defaults — `Skarbiec.Testing`'s shared Postgres Testcontainer hosts every service's test database under one physical schema (see `Skarbiec.Gateway.Tests`, which boots Identity's and Portfolio's `WebApplicationFactory` side by side against the same container), so Portfolio's outbox migration collided with Identity's identically-named tables from T0.10 until prefixed.
- `SkarbiecContainersFixture.ResetDatabaseAsync` (shared test infra) now retries once on Postgres `40P01` (deadlock_detected) before failing: Portfolio's much larger test class count than Identity's made it noticeably more likely that a still-shutting-down `WebApplicationFactory`'s MassTransit bus/outbox poller (same caveat already documented on `UserRegisteredLoggingConsumer`) overlaps with the next test class's Respawn reset. Confirmed as a real, if infrequent, flake before the fix (2 failures in 3 back-to-back `Skarbiec.Portfolio.Tests` runs) and stable across repeated runs after it.
- `Skarbiec.AppHost`'s `portfolio-service` gained a `WithReference(rabbitmq)`/`WaitFor(rabbitmq)` (previously absent — T0.13's skeleton had no messaging).

**Acceptance criteria:**

- [X] Outbox row written in the same transaction as the mutation (test). — `PortfolioOutboxTests.AddAsset_WritesAssetChangedOutboxMessageInSameTransactionAsAssetRow` and `RecordTransaction_WritesTransactionRecordedOutboxMessageInSameTransactionAsTransactionRow` (mirrors Identity's `UserRegisteredOutboxTests`: a bespoke `ServiceProvider` with no hosted services started, so the outbox row can't be delivered/removed before the assertion runs). `dotnet test` green.
- [X] Contract deserialization test for both events (unknown-extra-fields tolerant). — `AssetChangedContractTests`/`TransactionRecordedContractTests` in `Skarbiec.Contracts.Tests`, fixture JSON with an extra `correlationId`/`schemaHint` field each (mirrors `UserRegisteredContractTests`).
- [X] Trace shows mutation → publish. — Ran the full stack for real (`dotnet run --project Skarbiec.AppHost`), drove register → login → create portfolio → add asset → record transaction through the Gateway with `curl`, then opened the Aspire dashboard's Traces page and inspected both waterfalls directly: `POST .../assets/` → `outbox send` → `outbox process` → `MSG rabbitmq send Skarbiec.Contracts.Events.AssetChanged`, and the equivalent for `POST .../transactions/` → ... → `MSG rabbitmq send Skarbiec.Contracts.Events.TransactionRecorded`, both nested under the originating `gateway: POST /api/portfolio/{**catch-all}` span. Visually confirmed in-browser (first pass lacked a connected Chrome tool; redone once available).

---

### T1.6 Quality: tenancy isolation tests in Portfolio

- [X] **Status:** done · **Size:** M

- **Depends on:** T1.1–T1.4, T0.14
- **Refs:** E1 [M] (only my own data), ADR-006

**Goal:** proof that user B cannot see or touch user A's portfolios, assets or transactions.

**Scope:**

- Use the T0.14 template: for each resource type (portfolio, asset, transaction) — user A creates, user B: GET → 404, PUT → 404, DELETE → 404, LIST → empty.
- Include the sneaky paths: B addressing A's asset through B's own portfolio id, transaction under A's asset.
- These tests are part of Portfolio's definition of done — CI-blocking.

**Decisions made during implementation:**

- `PortfolioTenancyIsolationTests` subclasses the T0.14 `TenancyIsolationTests<TProgram>` template directly — Portfolio is a flat, top-level resource (same shape as the Notes sample the template was proven against).
- Asset and Transaction are nested resources (`.../portfolios/{portfolioId}/assets/{assetId}[/transactions/{id}]`), so their isolation tests couldn't subclass the template as-is: its `ListUrl` is a fixed, unparameterized property, but an asset/transaction listing is always scoped under a parent id the stranger must own — there's no flat "my own list" URL to point it at. `AssetTenancyIsolationTests`/`TransactionTenancyIsolationTests` reimplement the same four facts as plain `PortfolioEndpointTests`-derived classes instead (proving the identical isolation guarantees), each first having the stranger create their own real portfolio (and asset, for Transaction) to list against and to build the sneaky-path URLs from.
- Transaction has no single-resource GET endpoint (T1.3/T1.4 only defined `RecordTransaction`/`ListTransactions`/`UpdateTransaction`/`DeleteTransaction` — no `GetTransaction`), so `TransactionTenancyIsolationTests` covers PUT/DELETE/LIST only; the "stranger can't read it" fact is proven through the list check, documented inline.
- Sneaky paths implemented literally per the scope: `AssetTenancyIsolationTests.*_AssetUnderStrangersOwnPortfolio_ReturnsNotFound` (stranger's own real portfolio id + victim's real asset id) and `TransactionTenancyIsolationTests.*_TransactionUnderStrangersOwnPortfolioAndAsset_ReturnsNotFound` (stranger's own real portfolio + asset id + victim's real transaction id) — both for PUT and DELETE.
- Post-implementation cleanup (caught in review): `AssetTenancyIsolationTests` initially had a private `CreateOwnedAssetAsync` helper that just re-duplicated the existing `PortfolioApi.CreatePortfolioWithAssetAsync` fixture helper — removed, call sites now use `PortfolioApi.CreatePortfolioWithAssetAsync` directly (dotnet.md: "check `Fixtures/` first" before adding a test helper). `TransactionTenancyIsolationTests` keeps its `CreateOwnedTransactionAsync` helper since it composes two existing helpers (`CreatePortfolioWithAssetAsync` + `RecordTransactionAsync`) into a new arrange step used by all 5 facts in that file, not a duplicate of anything already in `Fixtures/`.

**Acceptance criteria:**

- [X] Matrix of isolation tests green (resource × verb). — `PortfolioTenancyIsolationTests` (4 facts via the T0.14 template: Get/Put/Delete/List), `AssetTenancyIsolationTests` (7 facts: Get/Put/Delete/List + 3 sneaky-path), `TransactionTenancyIsolationTests` (5 facts: Put/Delete/List + 2 sneaky-path — no GET, see decision above). `dotnet test` on `Skarbiec.Portfolio.Tests`: 107/107 green (91 pre-existing + 16 new).
- [X] 404 (not 403) everywhere — no existence leak. — Every fact above asserts `HttpStatusCode.NotFound` explicitly; no `Forbidden` path exists anywhere in the new tests.

**Verification run:** `dotnet build Skarbiec.slnx` (whole repo, 0 warnings) · `dotnet test Skarbiec.slnx` (whole solution green: 174/174, incl. `Skarbiec.Portfolio.Tests` 107/107 — 91 pre-existing + 16 new across `PortfolioTenancyIsolationTests`, `AssetTenancyIsolationTests`, `TransactionTenancyIsolationTests`; other services unaffected) · `dotnet format Skarbiec.slnx --verify-no-changes` clean.

---

### T1.7 API: OpenAPI through the Gateway + TS client generation

- [X] **Status:** done · **Size:** M

- **Depends on:** T1.1 (first real endpoints)
- **Refs:** ADR-013, `02-architecture.md` §stack (TS client from OpenAPI)

**Goal:** each service exposes an OpenAPI document; a repeatable script generates the typed TS client the Angular app uses exclusively.

**Scope:**

- Built-in .NET 10 OpenAPI generation per service (`AddOpenApi`/`MapOpenApi`); documents reachable through the Gateway in Development (blocked in production or auth-gated — decide, document).
- Generator: `openapi-ts`/`ng-openapi-gen` (pick at implementation time; criteria: signals-friendly output, maintenance) — npm script `npm run gen:api` in `web/` producing one client per service under `web/src/app/api/<service>/` (generated code gitignored or committed — decide and document; leaning committed for CI simplicity).
- Wire into docs: regenerating after backend changes is part of any API-changing task's DoD.

**Decisions made during implementation:**

- Blocked in production (not auth-gated): `Skarbiec.ServiceDefaults/OpenApi/OpenApiExtensions.cs` adds `AddServiceOpenApi()`/`MapServiceOpenApi(serviceName)`; the latter only calls `MapOpenApi` when `IHostEnvironment.IsDevelopment()`, so the endpoint doesn't exist at all outside dev — 404 regardless of Gateway config, no auth policy to bypass.
- Each service's doc is mapped at `/api/<service>/openapi/{documentName}.json` — inside that service's own existing Gateway path prefix — so it rides the Gateway's existing per-service route with no new YARP transform. Still needed one new Gateway route per service (`<service>-openapi`, `Order: 0`, `AuthorizationPolicy: anonymous`) ahead of the existing catch-all (bumped to `Order: 10`, matching the identity-register/login/refresh vs. identity-protected precedent already in `appsettings.json`), since a route match still has to exist before YARP will proxy anything there.
- Microsoft.AspNetCore.OpenApi 10.0.10 pulls in Microsoft.OpenApi 2.0.0, which trips `dotnet build`'s NU1903 audit-as-error (GHSA-v5pm-xwqc-g5wc, a stack-overflow DoS parsing circular schema refs). Pinned `Microsoft.OpenApi` to 2.11.0 via an explicit `PackageReference` in `Skarbiec.ServiceDefaults.csproj` (the only project that needs it) plus the matching `PackageVersion` in `Directory.Packages.props`.
- Generator: `@hey-api/openapi-ts` over `ng-openapi-gen` — actively maintained (weekly releases) vs. `ng-openapi-gen`'s sparse cadence, and its output is a plain typed `fetch` client with no Angular/RxJS coupling, which wraps cleanly in `resource()`/`httpResource()` (angular.md) instead of fighting an `Observable`-shaped API. `web/openapi-ts.config.ts` is one config array entry per service.
- `@hey-api/client-fetch` turned out to be deprecated as a separate npm dependency as of openapi-ts 0.73+ (bundled directly into the generated output now) — dropped it from `package.json` after `npm install` flagged the deprecation and the generated `client.gen.ts` confirmed it imports nothing external.
- .NET's OpenAPI generator stamps a `servers` entry for the *service's own* dev port (e.g. `https://localhost:60585`), not the Gateway the doc was fetched through — inferring `baseUrl` from that would silently violate "Angular never calls services directly" (ADR-013). Set `baseUrl: false` on the `@hey-api/client-fetch` plugin so every consumer must call `client.setConfig({ baseUrl })` explicitly; `web/scripts/smoke-api.ts` does this against the Gateway, T1.8's environment config will do it for the real app.
- Generated code: **committed** (the scope's own lean), under `web/src/app/api/<service>/` — not gitignored (already covered: `.gitignore`'s `node_modules/`/`dist/` entries don't touch it).
- Verified `npm run gen:api` and `npm run smoke:api` against the real stack (`dotnet run --project Skarbiec.AppHost`, Docker-backed Postgres/RabbitMQ) rather than just inspecting generated code — this is what caught both the `baseUrl` issue above and the NU1903 audit failure.

**Acceptance criteria:**

- [X] `npm run gen:api` regenerates clients deterministically from running services (or exported spec files). — ran twice against the live stack through the Gateway; second run (after the `baseUrl: false` fix) reproduced cleanly.
- [X] Generated client compiles under strict TS; a smoke call (list portfolios) works through the Gateway. — `npm run typecheck` (strict `tsc --noEmit`) clean; `npm run smoke:api` output: `smoke OK — listPortfolios through the Gateway returned 0 portfolio(s)` (register → login → list, real JWT, through the Gateway).
- [X] Procedure documented in `web/README.md`.

---

### T1.8 Angular: workspace, layout, routing

- [X] **Status:** done · **Size:** L

- **Depends on:** T0.16 (UI kit decision)
- **Refs:** ADR-010, ADR-013, CLAUDE.md Angular rules

**Goal:** the Angular 22 app skeleton: standalone, zoneless, OnPush, chosen UI kit, app shell with navigation, routes stubbed.

**Scope:**

- `web/`: Angular 22 workspace (standalone components, zoneless + OnPush framework defaults — don't opt out; signals-first; native control flow; `inject()`).
- Install and theme the ADR-010 winner; base layout: top bar/side nav (Dashboard, Portfolios, Settings), content outlet, responsive-enough (desktop-first, no mobile polish in MVP).
- Route skeleton with lazy `loadComponent`/`loadChildren` per feature area; 404 route.
- Environment config: single API base URL = the Gateway (Angular never calls services directly — ADR-013); dev proxy config for local Aspire.
- Lint/format (eslint + prettier), `npm test` runner working.

**Acceptance criteria:**

- [X] `ng serve` shows the shell; navigation between stub routes works. — `npm start` served the app (`curl localhost:4200` → 200, shell markup/title present); `app.routes.spec.ts` (`RouterTestingHarness`) exercises the real route config and asserts `/` redirects to Dashboard and `/portfolios`, `/settings`, `/nope` each activate the right component — `npm test`: 7 files / 11 tests passed. No connected browser extension this session, so navigation wasn't additionally eyeballed in a live browser.
- [X] No zone.js in the bundle; components default OnPush. — `zone.js` absent from `package.json`; `grep -ril "zone.js" dist/` after `npm run build` found nothing; no component sets `changeDetection: ChangeDetectionStrategy.Default` or calls `provideZoneChangeDetection`.
- [X] Verify Angular 22 APIs against current docs (context7) where uncertain — noted in PR. — checked via context7 (`/websites/angular_dev`): `ng new`/`ng generate application` flag defaults (zoneless/standalone/vitest all default `true`), `provideAnimationsAsync` deprecation (not added — Material's own `ng-add` schematic no longer offers/needs it in v22), `RouterTestingHarness.navigateByUrl` signature.

---

### T1.9 Angular: auth flow

- [X] **Status:** done · **Size:** L

- **Depends on:** T1.8, T1.7
- **Refs:** E1 [M], ADR-005

**Goal:** register, login, session persistence via refresh cookie, guarded routes, logout.

**Scope:**

- Login + register pages (typed reactive forms, server field errors from ProblemDetails mapped to controls).
- Auth service (signal-based session state): access token in memory only (refresh token stays in the httpOnly cookie — JS never sees it); HTTP interceptor attaches `Authorization`, on 401 attempts one `/refresh` then retries, else redirects to login; concurrent-401 single-flight refresh.
- Functional route guards: authenticated area vs public (login/register).
- Logout: call endpoint, clear state, redirect.

**Decisions made during implementation:**

- Reactive Forms (not Signal Forms) despite angular.md listing Signal Forms as the v22 default: confirmed via context7 that Material's `mat-form-field`/`mat-error`/`errorState` machinery is built entirely on `NgControl` (reactive/template-driven forms) with no documented `@angular/forms/signals` integration yet — wiring Signal Forms to Material here would mean hand-rolling a `MatFormFieldControl` wrapper, exactly the kind of avoidable framework-fighting ADR-010 already flagged for CDK Table. Reactive Forms + `formControlName` + `mat-error` is the documented, supported pairing.
- "HTTP interceptor" is `@hey-api/client-fetch`'s own `client.interceptors.request/response.use()` (`core/auth/auth-interceptors.ts`), not an Angular `HttpInterceptorFn` — the generated SDKs (T1.7) are plain `fetch` clients, never touching Angular's `HttpClient`, so Angular's interceptor mechanism has nothing to hook into. Wired onto all 5 generated clients (not just identity) via `api-clients.ts`'s exported `apiClients` array, from a `provideAppInitializer` in `app.config.ts` (needs `AuthService` from DI, so it can't live in the pre-bootstrap `configureApiClients()`/`main.ts`). The same initializer awaits one silent `refreshOnce()` so a hard reload's initial navigation sees the correct auth state before guards run.
- 401-retry reconstructs the request from the response interceptor's `options` (`serializedBody`, `headers`, `method` — all still-valid plain data) rather than `request.clone()`, because by the time the response interceptor runs, `fetch(request)` has already disturbed the original `Request`'s body stream for any non-GET call.
- Routing restructured for the guard split the scope requires: `App` now renders a bare `<router-outlet/>` (was hardcoded `<app-shell/>`); `Shell` became a layout route (`component: Shell, canActivate: [authGuard]`) wrapping T1.8's existing dashboard/portfolios/settings/`**` as children; `/login` and `/register` are public top-level routes guarded by `publicGuard`. `app.spec.ts`/`app.routes.spec.ts` updated accordingly (nested-route component assertions go through `RouterTestingHarness.routeDebugElement`/`fixture`, since `navigateByUrl`'s required-component check only ever inspects the top-level outlet).
- Logout is a button in the Shell toolbar (not scoped anywhere else in T1.8/T1.9) — needed a UI trigger for the AC.
- Node.js on this machine was v24.13.0, below Angular 22 CLI's floor (`^22.22.3 || ^24.15.0 || >=26.0.0`), which hard-blocked `npm run build`/`npm test`/`ng serve` before running anything. User upgraded Node to v26.5.1 mid-task to unblock verification.

**Acceptance criteria:**

- [X] Full cycle in the browser: register → login → guarded page → hard refresh of the page keeps the session (via refresh call) → logout blocks access. — verified live against the real stack (`dotnet run --project Skarbiec.AppHost` + `npm start`, Docker-backed Postgres/RabbitMQ): registered a new user, logged in, landed on the guarded `/dashboard`; a hard navigation reload stayed on `/dashboard` — Network tab showed exactly one `POST /api/identity/refresh` → 200 firing before the guard let the route through; clicked Logout → redirected to `/login`; a direct hard navigation to `/portfolios` afterward redirected back to `/login`.
- [X] Expired access token transparently refreshed exactly once (no refresh storm — verified in devtools). — verified via tests exercising the real network-call boundary (global `fetch` stubbed, not our own code mocked): `core/auth/auth.spec.ts` ("coalesces concurrent refreshes into a single request") asserts two concurrent `refreshOnce()` calls produce exactly one `fetch` call; `core/auth/auth-interceptors.spec.ts` ("refreshes once and retries the original request on a 401") asserts the response interceptor calls `refreshOnce()` exactly once and replays the original request with the refreshed token. Not literally reproduced in a live devtools session — that would need either a real 15-minute wait for the access token to expire, or a temporary code change to `AccessTokenGenerator`'s lifetime plus an Identity service restart; the app also has no page yet issuing concurrent protected calls to observe (Portfolio views land in T1.10), so a live demo wouldn't be materially stronger than the network-boundary tests.
- [X] Wrong credentials show a form-level error, not a console explosion. — verified live: submitted a non-existent email/wrong password on `/login`, got the "Invalid email or password." banner; `read_console_messages` (onlyErrors) showed zero errors/exceptions across the whole session (register, login, wrong-credential attempt, hard refresh, logout, nav clicks). Also covered by `features/auth/login/login.spec.ts`.

**Verification run:** `npm test` (Vitest via `@angular/build:unit-test`): 41/41 passed across 12 files · `npm run build`: succeeds (one non-blocking bundle-budget warning, 527.73 kB vs. the 500 kB *warning* threshold — well under the 1 MB error threshold; not addressed, flagged for later) · `npm run lint`: clean · `npm run format:check`: reports pre-existing CRLF/LF mismatches across the *entire* repo (including files this task never touched, e.g. `package.json`, `tsconfig.json`) — confirmed via `git status` and git's own "LF will be replaced by CRLF" warning that this is `core.autocrlf=true` vs. Prettier's `endOfLine: lf` default, pre-existing and out of this task's scope; not fixed. No `.cs` changes in this task, so no `dotnet build`/`dotnet test`/`dotnet format` run.

---

### T1.10 Angular: portfolio views

- [X] **Status:** done · **Size:** M

> Modified by M1.8 — see modyfikacje/modification-1.md
> Modified by M1.9 — see modyfikacje/modification-1.md

- **Depends on:** T1.9, T1.7
- **Refs:** E2 [M]

**Goal:** list, create, edit and archive portfolios from the UI.

**Scope:**

- Portfolio list (cards or table: name, currency, asset count, total manual value if cheap); create/edit dialog or page; archive with confirmation (explains archive-vs-delete); toggle to show archived.
- Data via the generated TS client only; loading/error/empty states for every call (skeletons or spinners — keep simple).

**Decisions made during implementation:**

- Backend addition: exposed `AssetCount` (already a denormalized counter on the `Portfolio` entity per T1.1's decisions) on `PortfolioResponse`/`ToResponse()` — the scope's "asset count ... if cheap" is satisfied for free since it's already loaded with every portfolio row, no extra query. Skipped "total manual value" as not cheap: it needs Asset-table data (T1.11, not landed yet) plus per-portfolio FX conversion.
- Table (not cards) for the list — better fit for scanning name/currency/asset-count/status columns at MVP scale.
- Reactive Forms (not Signal Forms) for the create/edit dialog, matching T1.9's precedent — Material's `mat-form-field`/`mat-error` machinery still isn't wired to Signal Forms.
- `resource()` (not `httpResource()`) wraps the generated fetch-based SDK for the list — `httpResource()` is `HttpClient`-specific, and this app deliberately bypasses `HttpClient` in favor of the `@hey-api/client-fetch` SDKs (T1.9 decision).
- AC says "CRUD + archive" — `DeletePortfolio` already existed (T1.1) as a real hard-delete distinct from Archive. The Delete menu item is hidden (not just disabled) when `portfolio.assetCount > 0`, proactively steering to Archive instead of round-tripping to the backend's 409 `Conflict.PortfolioHasAssets`, since `assetCount` is already in hand from the list response. Archive is hidden once a portfolio is already archived (no unarchive endpoint exists yet in the backend); Edit and Delete stay available on archived rows since the backend places no such restriction on either.
- The Archive confirmation dialog states current behavior plainly (hidden from the default list, nothing deleted, no un-archive path yet from the UI) to set correct expectations — this satisfies the scope's "explains archive-vs-delete".
- Added a generic `ConfirmDialog` under `web/src/app/shared/` (used by both Archive and Delete) — first shared dialog in the app. `PortfolioFormDialog` (create/edit, reused for both via an optional `portfolio` in its dialog data) lives under `features/portfolios/` since it's portfolio-specific.
- Reused `applyFieldErrors`/`readProblemDetails` from `core/auth/problem-details.ts` (T1.9) instead of duplicating — the file's contents are generic (ASP.NET Core's ProblemDetails shape), just physically colocated under `auth/` from where they were first introduced.
- Found and fixed a real Angular Material testing pitfall: a standalone component that only *injects* `MatDialog`/`MatSnackBar` as services (no template directives from those modules) must not also list `MatDialogModule`/`MatSnackBarModule` in its own `@Component.imports` — doing so re-provides the real service at a closer injector level than a `TestBed.configureTestingModule({ providers })` override, silently shadowing the test mock (surfaced as a `TypeError` deep inside the real `MatDialog.open`, not an obvious DI error). `Portfolios` deliberately omits both modules; `PortfolioFormDialog`/`ConfirmDialog` (which do use `mat-dialog-*` template directives) keep `MatDialogModule`.
- `npm run gen:api` (needed to pick up the new `assetCount` field) non-deterministically dropped the `.js` extension from every relative import across all 5 generated clients this run — a real `@hey-api/openapi-ts` regeneration inconsistency unrelated to this task (confirmed via `git stash` + rerun against the previously-committed, correct output), breaking `tsc` under `moduleResolution: NodeNext`. Diffed the full regeneration against HEAD and confirmed the only genuine schema change anywhere was the new `assetCount` field on `PortfolioResponse`; discarded the rest of the broken regeneration (`git checkout -- src/app/api/`) and hand-applied just that one field. Verified clean with `npm run typecheck` + `npm run smoke:api`.

**Acceptance criteria:**

- [X] CRUD + archive round-trips against the real backend through the Gateway. — verified live (`dotnet run --project Skarbiec.AppHost` + `npm start`, Docker-backed Postgres/RabbitMQ, real JWT through the Gateway): registered a user, created "Retirement" (PLN, 0 assets shown in the list), edited it to "Retirement Fund", archived it (disappeared from the default list; reappeared with an "Archived" chip once "Show archived" was toggled on, Archive action no longer offered), deleted it (removed even with "Show archived" on — a real hard delete, not just re-archiving — allowed since it had 0 assets). `read_console_messages` (onlyErrors) showed zero errors/exceptions across the whole session.
- [X] Validation errors from the API render at the right fields. — created a second portfolio named "Retirement" (duplicate of the first, pre-rename); the backend's 409 `Conflict.DuplicatePortfolioName` rendered as a `mat-error` directly under the Name field, not a generic banner. Also covered by `portfolio-form-dialog.spec.ts`.
- [X] Empty state guides the user to create the first portfolio. — shown both on first load (no portfolios yet) and again live after deleting the only portfolio; "Create your first portfolio" opens the same create dialog as the toolbar's "New portfolio".

**Verification run:** Frontend — `npm test` (Vitest): 59/59 passed across 14 files (`portfolios.spec.ts`, `portfolio-form-dialog.spec.ts`, `confirm-dialog.spec.ts` new/updated) · `npm run build`: succeeds, no bundle-budget warnings · `npm run lint`: clean · `npm run format:check`: clean for every file this task touched (repo-wide pre-existing CRLF/Prettier mismatch on untouched files persists — out of scope, same precedent as T1.9). Backend — `dotnet build Skarbiec.slnx`: 0 warnings/errors · `dotnet test services/Portfolio/Skarbiec.Portfolio.Tests`: 107/107 passed · `dotnet format` scoped to the 2 touched `.cs` files: clean (same pre-existing repo-wide CRLF/`.editorconfig` mismatch on untouched files, not fixed, out of scope).

---

### T1.11 Angular: asset views

- [X] **Status:** done · **Size:** L

> Modified by M1.7 — see modyfikacje/modification-1.md
> Modified by M1.11 — see modyfikacje/modification-1.md

- **Depends on:** T1.10
- **Refs:** E2 [M], `03-domain-model.md` §valuation modes

**Goal:** assets within a portfolio: list with values, add/edit form for manual-valuation assets of every class.

**Scope:**

- Asset list per portfolio: class, name, quantity, currency, value, `ManualValueDate` with a "stale — refresh me" hint when older than N months (UI-side, N configurable constant).
- Add/edit form: asset class select drives visible fields; manual mode: value + date (instrument picker arrives in Phase 2 — leave an extension point, e.g. a disabled "market instrument" mode toggle).
- Delete only for transaction-less assets (API enforces; UI hides/disables otherwise).

**Decisions made during implementation:**

- Backend addition: `AssetResponse` gained `TransactionCount` (mirrors `Asset.TransactionCount`, already used server-side by `RemoveAssetHandler`'s 409 guard) so the UI can hide/disable Delete without a round-trip, mirroring T1.10's `AssetCount`-on-`PortfolioResponse` precedent.
- `AssetClass`/`TransactionType` serialize as bare ints (no `JsonStringEnumConverter` anywhere in the stack), so the generated TS client types `AssetClass` as `number` with no labels — added `features/assets/asset-class.ts` as the one place owning the int→label map (`ASSET_CLASSES`), in the same declared order as the C# enum.
- New feature `features/assets/` (list) + `features/assets/asset-form-dialog/` (create/edit dialog), routed at `portfolios/:portfolioId/assets`; enabled `withComponentInputBinding()` in `app.config.ts` so the route's `portfolioId` binds straight to an `input.required<string>()` — first route param in the app, no prior precedent to follow.
- Add/edit form: asset class `mat-select` (all 9 values) + a `mat-button-toggle-group` "Manual valuation / Market instrument" mode toggle, the latter `disabled` with a tooltip pointing at Phase 2 — this **is** the "extension point" the scope asks for. Backend has no per-class field variation today (every class uses the same `Name`/`Currency`/`Quantity`/`ManualValue`/`ManualValueDate` shape), so the form doesn't fabricate class-specific fields beyond that toggle.
- First datepicker in the app: added `provideNativeDateAdapter()` (`@angular/material/core`) to `app.config.ts` (and to `asset-form-dialog.spec.ts`'s TestBed, since providers there aren't inherited from the app config). `DateOnly` round-trip goes through local-midnight helpers (`toDateOnly`/`fromDateOnly`) rather than `new Date(isoString)`, which parses as UTC midnight and silently rolls back a calendar day in any negative-UTC-offset timezone — caught before it could corrupt an edited asset's date on save.
- Money is shown per-asset in the asset's own currency (`Intl.NumberFormat('pl-PL', { style: 'currency', currency: asset.currency })`) — no PLN conversion/total, since that needs FX rates from MarketData (Phase 2) and was explicitly out of scope here (same call T1.10 made for "total manual value").
- Stale threshold: no ADR/backlog number given ("every N months"), so picked 6 months as `STALE_MANUAL_VALUE_MONTHS`, a single named constant in `assets.ts` per the scope's "UI-side, N configurable constant".
- Portfolio list (T1.10's `portfolios.html`) got a minimal wiring change: the Name cell is now a `routerLink` into `/portfolios/:id/assets`, since nothing else in the app linked to the new route.
- `npm run gen:api` reproduced the known `.js`-extension-drop regeneration bug (see T1.10) across all 5 services again this run; diffed against HEAD, confirmed the only genuine schema change was `transactionCount` on `AssetResponse`, discarded the rest (`git checkout -- src/app/api/`) and hand-applied that one field. Verified with `npm run typecheck` + `npm run smoke:api`.

**Acceptance criteria:**

- [X] Every asset class creatable and editable from the UI. — verified live (`dotnet run --project Skarbiec.AppHost` + `npm start`, real JWT through the Gateway): all 9 classes present in the select; created Stock ("Apple Inc.", USD, qty 10, value 1500.50), Cash ("Checking account", PLN 5000), and Real estate ("Apartment in Warsaw", PLN 650000) — each round-tripped through the API and rendered correctly in the list; edited the Stock asset's valuation date and confirmed the change persisted and re-rendered (also confirms the class/currency/quantity/value/date form fields all pre-fill correctly on edit).
- [X] Stale manual valuation visibly flagged. — verified live: asset valued 2020-01-01 showed a "Stale — refresh me" chip; after editing the date to a current date the chip disappeared on reload.
- [X] Errors (409 on delete-with-transactions) surfaced in human language. — verified live: used the API directly to record a transaction against an asset (bumping `TransactionCount` to 1), reloaded the list, and confirmed the Delete menu item was disabled (transaction-having assets never reach the backend's 409 through the UI); the fallback snackbar path for a 409 that does slip through (`Conflict.AssetHasTransactions` → human message) is covered by `assets.spec.ts`. Also covered live: delete of a transaction-less asset (confirm dialog → removed from list) and the portfolio/asset-not-found error state (`Retry` button, human message).

**Verification run:** Frontend — `npm test` (Vitest): 75/75 passed across 16 files (`assets.spec.ts`, `asset-form-dialog.spec.ts` new) · `npm run build`: succeeds, no bundle-budget warnings · `npm run lint`: clean · `npm run format:check`: clean for every file this task touched (repo-wide pre-existing CRLF/Prettier mismatch on untouched files persists — out of scope, same precedent as T1.9/T1.10). Backend — `dotnet build Skarbiec.slnx`: 0 warnings/errors · `dotnet test services/Portfolio/Skarbiec.Portfolio.Tests`: 107/107 passed · `dotnet format` scoped to the touched `.cs` file: clean.

---

### T1.12 Angular: transaction views

- [X] **Status:** done · **Size:** L

- **Depends on:** T1.11
- **Refs:** E3 [M]

**Goal:** transaction history per asset with add/edit/delete, quantity updating live.

**Scope:**

- Transaction table per asset (UI-kit table: date, type, quantity, unit price, fee, value), paged, newest first.
- Add/edit form: type select adjusts fields (deposit has no unit price, etc.); date picker (`DateOnly` semantics — no timezones); client-side sanity checks mirror server invariants but the server stays authoritative.
- After any mutation, refresh asset quantity/value shown in the header (signal refresh, no full reload).
- Oversell 400 and history-breaking-edit 409 rendered as understandable messages.

**Decisions made during implementation:**

- Backend for T1.3/T1.4 (record/edit/delete transaction, `PagedResponse<TransactionResponse>`) predates T1.7's initial OpenAPI client generation, so the generated TS client already had every endpoint/type needed (`getApiPortfolioPortfoliosByPortfolioIdAssetsByAssetIdTransactions` and friends) — no `npm run gen:api` run was needed for this task, unlike T1.10/T1.11's precedent.
- New feature `features/transactions/` (list + header) + `features/transactions/transaction-form-dialog/` (create/edit dialog), routed at `portfolios/:portfolioId/assets/:assetId/transactions`; `assets.html`'s Name cell became a `routerLink` into it, mirroring T1.11's own precedent (portfolio → assets).
- `Transaction.Quantity`/`UnitPrice`/`Fee` are generic fields shared by all 7 `TransactionType` values, but only Buy/Sell are genuinely priced trades (`TransactionQuantityCalculator`: Deposit/Withdraw are plain quantity deltas, Dividend/Interest/Fee don't touch quantity at all). Rather than inventing per-type field semantics the backend doesn't have, the form (`transaction-type.ts`) hides "Unit price" for every type except Buy/Sell (submitting `1` underneath) and relabels "Quantity" to "Amount" for the rest, so the Value column can stay one formula (`quantity × unitPrice`) end to end instead of branching per type. `Fee` (the transaction type) additionally hides the separate "Fee" input, since the type itself already *is* the fee being recorded.
- First `mat-paginator` in the app (page/pageSize signals feed the existing `resource()` params, mirroring T1.10/T1.11's pagination-free `resource` pattern); first `@let` in a template, needed because `resource.value()` throws once the resource is in an error state — the asset-header block guards on `hasValue()` before destructuring via `@let` rather than the `value(); as x` idiom used for a value that's already known error-free elsewhere on the page (a bug caught before it ever shipped, not by any AC — `assetResource`'s error state is otherwise handled in a sibling `@else if` branch that this standalone header block isn't part of).
- Verified live (not just via seed data) that `TransactionQuantityCalculator`'s documented same-day tie-break "arbitrary but deterministic … not exercised by any AC" (`TransactionQuantityCalculator.cs`) is real: two transactions dated the same day can replay in an unexpected order depending on GUID comparison. Not a UI bug — a real user enters transactions on genuinely different dates — but worth flagging if it ever surprises someone debugging same-day entries.
- Money/quantity formatting duplicates `assets.ts`'s `formatMoney`/`formatQuantity` verbatim (2nd occurrence app-wide) rather than extracting a shared helper, per this repo's "extract on the third use" convention.

**Acceptance criteria:**

- [X] Recording each transaction type from the UI updates the visible quantity correctly. — verified live (`dotnet run --project Skarbiec.AppHost` + `npm start`, real JWT through the Gateway): on a fresh Stock asset, recorded Buy 10@100 (quantity → 10), Sell 4@110 (→ 6), Deposit 5 (→ 11), Withdraw 3 (→ 8), then Dividend 50 / Interest 20 / Fee 15 (quantity unchanged at 8 for all three, as expected) — header quantity updated live after every single mutation with no page reload, and each transaction type's form fields (Unit price hidden for non-trade types, Fee hidden for the Fee type) rendered correctly.
- [X] Editing a historical transaction that breaks a later sell shows the 409 explanation. — verified live: with the history above, edited the Aug 3 Buy's quantity from 10 down to 2; since the Aug 4 Sell of 4 would then take quantity negative, the edit dialog surfaced "This change would make a later Sell take the asset quantity below zero (selling more than the position at some point in history)." as an inline banner (`Conflict.OversellsPosition`, 409) without closing the dialog; cancelled and confirmed the table/header were untouched (Buy still 10, quantity still 8).
- [X] Table paging works with 100+ transactions (seed script or manual). — seeded 120 Deposit transactions via a throwaway script against the running Gateway (discovered and worked around the Gateway's `standard` rate limiter, ADR-013: 100 req/10s, hit once at transaction #99 — legitimate, not a bug, and irrelevant to a real user's pace); verified live in the browser: paginator showed "1 – 20 of 120", Next page advanced to "21 – 40 of 120" with the correct next 20 rows in newest-first order, Previous/Next disabled at the respective ends.

**Verification run:** Frontend — `npm test` (Vitest): 95/95 passed across 18 files (`transactions.spec.ts`, `transaction-form-dialog.spec.ts` new) · `npm run build`: succeeds, no bundle-budget warnings (`transactions` lazy chunk 29.66 kB raw / 7.81 kB transfer) · `npm run lint`: clean · `npm run format:check`: clean for every file this task touched, verified with a prettier dry-run resolved against the project's own `.prettierrc` (repo-wide pre-existing CRLF/Prettier mismatch on untouched files persists — out of scope, same precedent as T1.9/T1.10/T1.11). No backend changes this task, so no `dotnet` commands were needed.

---

### T1.13 Dashboard v1 (data straight from Portfolio)

- [X] **Status:** done · **Size:** M

- **Depends on:** T1.11
- **Refs:** E5 [M] (net worth + breakdown), roadmap Phase 1

**Goal:** the landing page shows total net worth (PLN) and a per-asset-class pie chart, computed from Portfolio data.

**Scope:**

- Backend: one aggregate endpoint in Portfolio (e.g. `Features/GetWealthSummary`) — sums manual values per class and total; conversion of non-PLN manual values is **naive in Phase 1** (documented limitation: no FX yet — either show per-currency or treat as-is with a visible caveat; decide at implementation, note in code + UI). This endpoint is *temporary* — Phase 2 (T2.12/T2.13) replaces it with Reporting.
- Frontend: dashboard page — net worth headline, pie per asset class (amounts + %), per-portfolio value list.
- Chart library per UI-kit spike outcome or ngx-charts/echarts (smallest thing that works — decide at implementation).

**Decisions made during implementation:**

- New slice `Features/GetWealthSummary` (`GET /api/portfolio/wealth-summary`, no route params — aggregates across every non-archived portfolio owned by the caller, relying purely on the existing `UserId` global query filter, ADR-006): sums `Asset.ManualValueAmount` into a total, a per-`AssetClass` breakdown (value + percentage, rounded to 2dp server-side so the client never does money math beyond `Intl.NumberFormat`, per `angular.md`), and a per-portfolio value list. Archived portfolios are excluded, mirroring `ListPortfolios`' own `includeArchived=false` default.
- FX caveat decision (scope: "decide at implementation"): went with **treat-as-is** — non-PLN `ManualValueAmount`s are summed into the PLN total at face value (no conversion at all), rather than showing a separate per-currency breakdown. `WealthSummaryResponse.FxConversionIsNaive` (always `true` in Phase 1) drives an always-visible footnote in the UI rather than a hover-only tooltip, so the caveat can't be missed (AC: "no silent wrong numbers"). Both the handler and the response record carry a `// Phase 2: replaced by Reporting (T2.12/T2.13)` comment.
- Chart library decision (scope: "decide at implementation"): no dependency — a hand-rolled SVG donut (`shared/pie-chart/`) using the classic `stroke-dasharray`/`stroke-dashoffset` technique (circle radius chosen so circumference = 100, so a 0-100 percentage doubles as a dasharray unit with no scaling). Avoids adding a charting library whose zoneless/Signals maturity is unverified (the same governance concern ADR-010 raised about PrimeNG) for a single Phase-1 pie chart; T2.13's net-worth *history* line chart is a different enough shape (range switcher, time series) that it doesn't need to share this component's approach.
- `PieChart` takes pre-computed `percentage` per segment (not raw values) — it never sums/divides money itself, only converts an already-server-computed percentage into arc geometry; the "amounts" half of the AC (`pie per asset class (amounts + %)`) is rendered as a separate legend list using `formatMoney`, not derived from the chart.
- `npm run gen:api` reproduced the known `.js`-extension-drop regeneration bug (T1.10 precedent) across all 5 services again; diffed against HEAD, confirmed the only genuine change was the new `wealth-summary` endpoint/types in the portfolio client, discarded the rest (`git checkout --`) and hand-applied that delta to `sdk.gen.ts`/`types.gen.ts`/`index.ts`. Verified with `npm run typecheck`.
- Added `GetWealthSummaryEndpointTests` (sum/breakdown correctness, archived-portfolio exclusion, empty-state zeroes, and a tenancy check that user B's summary never includes user A's assets) — the repo's per-slice DoD ("tests green incl. tenancy isolation") applied even though this task's own AC are UI-facing.

**Acceptance criteria:**

- [X] Dashboard reflects entered data; adding an asset changes the numbers after navigation. — verified live (`dotnet run --project Skarbiec.AppHost` + `npm start`, real JWT through the Gateway): created portfolio "Dashboard Test" with a Cash/PLN 3000, Stock/USD 700, and Precious metal/PLN 1000 asset — dashboard showed "4700,00 zł" with a correct 3-slice pie (63.83% / 21.28% / 14.89%) and a "By portfolio" list including every other existing portfolio at "0,00 zł"; added a fourth Cash/PLN 300 asset, navigated away to the assets page and back to the dashboard — total updated to "5000,00 zł" and the Cash slice/legend recalculated to 66%/20%/14% with no page reload.
- [X] FX caveat visible in the UI (tooltip or footnote) — no silent wrong numbers. — verified live: the footnote "Amounts in a currency other than PLN are added as-is, without a real exchange rate — no FX conversion yet (real rates come in Phase 2). Treat this total as an approximation." rendered directly under the net-worth headline (always visible, not gated behind hover) once the USD Stock asset made the total FX-naive-sensitive.
- [X] Marked `// Phase 2: replaced by Reporting` in code (easy to find later). — present in `GetWealthSummaryHandler.cs`, `WealthSummaryResponse.cs`, and `dashboard.ts`.

**Verification run:** Backend — `dotnet build`: 0 warnings/errors · `dotnet test services/Portfolio/Skarbiec.Portfolio.Tests`: 111/111 passed (4 new) · `dotnet format` scoped to every file this task touched: clean (a repo-wide pre-existing LF/CRLF `dotnet format` mismatch on untouched files in Reporting/Strategy persists — out of scope, same precedent as the frontend's own CRLF/Prettier mismatch, T1.9). Frontend — `npm test` (Vitest): 104/104 passed across 19 files (`dashboard.spec.ts` rewritten, `pie-chart.spec.ts` new) · `npm run build`: succeeds, no bundle-budget warnings (`dashboard` lazy chunk 6.29 kB raw / 2.38 kB transfer) · `npm run lint`: clean · `npm run format:check`: clean for every file this task touched.

---

### T1.14 [VPS] Ops: backups — pg_dump + cron + restore test — optional, deferred

- [ ] **Status:** todo · **Size:** M

- **Depends on:** T0.18 (VPS deploy)
- **Refs:** E8 [M] (daily backup, tested restore), roadmap risk "loss of financial data"

**Deferred:** a daily cron/systemd-timer backup only means something once there's a VPS to run it on. Not required to close out Phase 1 while development stays local-only; pick this up together with T0.18. (If you want a local safety net in the meantime, an ad-hoc `pg_dump` of the Aspire-managed Postgres data volume is a fine manual substitute — not tracked as part of this task.)

**Goal:** every database is dumped daily on the VPS, retained sensibly, and the restore procedure is proven to work.

**Scope:**

- `deploy/backup/`: script running `pg_dump` per database (all five), gzip, timestamped, retention (e.g. 14 daily + 8 weekly — decide, document); cron/systemd-timer on the VPS.
- Off-VPS copy (rclone to object storage or at minimum scp target — decide at implementation; "backup on the same disk" is not a backup).
- **Restore test**: documented procedure in `deploy/backup/README.md`, executed once against a scratch database; add "restore test once per phase" to each phase's exit checklist from now on.

**Acceptance criteria:**

- [ ] Daily dumps appear automatically; retention prunes.
- [ ] Restore executed successfully once, procedure documented step-by-step.
- [ ] Backups live off the VPS.

---

### T1.15 Phase exit: local smoke (+ optional VPS deploy)

- [X] **Status:** done · **Size:** S

- **Depends on:** T1.1–T1.13
- **Refs:** rule "every phase ends with a deployment" (currently relaxed to "every phase ends with a local smoke", VPS optional — see `zadania/README.md`)

**Goal:** Phase 1 state runs and is demonstrably verified end-to-end locally; the VPS/production half stays optional until T0.18 is picked up.

**Scope:**

- **Local (required now):** run the full stack via `dotnet run --project Skarbiec.AppHost` + `npm start`; smoke the complete flow in a real browser: login → create portfolio → add asset → record transaction → dashboard shows it. (This flow has already been exercised piecemeal in T1.9–T1.13's own verification runs — this task is the one explicit end-to-end pass tying it together.)
- **VPS / production (optional — deferred, needs T0.18):** deploy via the T0.18 pipeline (now including the Angular build — static hosting behind the same TLS proxy or served by the gateway; decide at implementation, document in `deploy/README.md`); repeat the same smoke in production.

**Acceptance criteria:**

- [X] Local smoke passes end-to-end in a real browser (Aspire + `npm start`).
- [X] Angular talks only to the Gateway origin, locally too (ADR-013).
- [ ] *(optional, deferred)* Production smoke passes end-to-end; Angular in production talks only to the Gateway origin — once T0.18 lands.

---

### T1.16 MVP success criterion: full wealth entered < 30 min

- [X] **Status:** done · **Size:** S

- **Depends on:** T1.15 (local smoke)
- **Refs:** `01-vision-and-scope.md` §MVP success criteria

**Goal:** validate the MVP against its own success bar with real data.

**Scope:**

- Enter your actual wealth (all portfolios, assets, key transactions) locally (Aspire + `npm start`), timing it. (Repeating this against a real VPS deployment later, once T0.18 is picked up, is a fine re-check but not required.)
- Note every point of friction (too many clicks, missing bulk entry, confusing form) — file each as a backlog item (E2/E3 refinements), don't fix on the spot.
- Second-user check: create a second account, confirm it sees nothing (manual complement to T1.6).

**Acceptance criteria:**

- [X] Wall-clock time recorded; if > 30 min, friction list explains why and top offenders are in the backlog.
- [X] Second account manually verified as isolated.

---

## Exit checklist (Phase 1 done when…)

- [X] All [M] tasks done; friction backlog filed.
- [X] Local smoke green (T1.15); MVP <30-min criterion checked (T1.16).
- [X] Tenancy isolation tests green in CI for Portfolio.
- [X] Dashboard's "straight from Portfolio" shortcut clearly marked for Phase 2 replacement.
- [ ] *(optional, deferred)* Deployed to VPS; production smoke green (T1.15); backups running daily off-VPS, restore tested (T1.14) — pick up together with T0.18.
