---
paths:
  - "**/*.cs"
  - "**/*.csproj"
  - "**/*.props"
  - "**/*.slnx"
---

# .NET conventions (C# 14 / .NET 10)

Items marked *(default — confirm in Phase 0)* are opinionated choices not backed by an ADR yet; use them, but flag when the user touches the area for the first time.

## Language

- `<Nullable>enable</Nullable>`, implicit usings, file-scoped namespaces everywhere.
- `record` for DTOs, requests/responses, event contracts; `required` members over constructor telescoping.
- Classes `sealed` by default; primary constructors for DI; pattern matching over type checks/casts.
- No `async void`; `CancellationToken` accepted and passed through in every handler and EF/HTTP call.

## Solution hygiene *(default — confirm in Phase 0)*

- `Directory.Build.props` at repo root: `LangVersion` latest, `TreatWarningsAsErrors`, `AnalysisLevel` latest, `ImplicitUsings`, `Nullable`.
- Central Package Management: versions only in `Directory.Packages.props`.
- `dotnet format` clean before commit.

## Minimal APIs

- Endpoints grouped with `MapGroup("/api/<service>/<feature>")`; one endpoint file per slice registering itself via an extension method.
- Return `TypedResults` with `Results<T1, T2, ...>` unions — never bare `IResult`.
- Validation: .NET 10 built-in Minimal API validation — `builder.Services.AddValidation()` + DataAnnotations on request records; invalid input short-circuits to 400 ProblemDetails. Complex/cross-field rules: `IValidatableObject` or a validator class in the slice. *(default — confirm in Phase 0; fallback: FluentValidation)*
- Errors as ProblemDetails end-to-end: request validation → 400 automatically (previous bullet); handler-level expected failures (not found, conflict, business-rule violation) → the handler returns `Result`/`Result<T>`, the endpoint maps the failure to `TypedResults`/ProblemDetails via a shared helper in ServiceDefaults (ADR-017); genuinely unexpected failures → exception, caught by the global `AddProblemDetails()` handler in ServiceDefaults. Correlation/trace id included in every response.
- OpenAPI: built-in `Microsoft.AspNetCore.OpenApi` (`AddOpenApi()`/`MapOpenApi()`) — no Swashbuckle. The generated doc feeds the Angular TS client.

## Slices (ADR-002, ADR-004)

- `Features/<FeatureName>/` = endpoint + handler + validator (+ request/response records). Handler is a plain class resolved from DI — no MediatR, no Service/Repository layers.
- Handler methods return `Result`/`Result<T>` (railway-oriented; the `Result`/`Result<T>`/`Error` types live in `Skarbiec.Contracts`) — never throw for expected failures. The endpoint calls the handler and maps the result to `TypedResults` via the ProblemDetails-mapping helper in ServiceDefaults (ADR-017). Exceptions stay reserved for genuinely unexpected failures.
- Extract shared code only on the third use.
- `UserId` always from JWT claims (`ClaimsPrincipal`), never from the request body or route (ADR-006).

## EF Core 10

- One DbContext per service; migrations live in the service; no lazy loading, no `Include` chains crossing aggregate boundaries.
- Reads: `AsNoTracking()`; projections with `Select` into response records.
- Global query filter on `UserId` + save interceptor stamping `UserId` (ADR-006).
- Money: `decimal` with explicit precision (e.g. `HasPrecision(18, 8)` for quantity/prices, `(18, 2)` for PLN amounts) — never `double`/`float`.
- Time: `DateTimeOffset`/UTC (`timestamptz`); `DateOnly` for `PriceQuote`/`FxRate` dates. Unique indexes: (instrument, date), (pair, date).
- Cross-service references are plain `Guid` columns — no FK, no navigation (ADR-003).

## Messaging (ADR-012)

- Publish only via MassTransit EF Outbox — never `IPublishEndpoint` outside the outbox transaction.
- Consumers idempotent: inbox/dedup by `MessageId`. Contracts in `Skarbiec.Contracts`, additive versioning (breaking = new `V2` type).
- Trace context propagates automatically (MassTransit + OTel) — no manual correlation IDs.
- Details and tests: follow `/new-event`.

## HTTP between services

- Named/typed `HttpClient` + `Microsoft.Extensions.Http.Resilience` (retry with jitter, circuit breaker, timeout) — configured once in ServiceDefaults.
- Forward the caller's JWT (token passthrough); downstream service filters by its own `UserId` claim.

## Testing

- xUnit v3 *(default — confirm in Phase 0)* + Testcontainers (PostgreSQL, RabbitMQ) for slice integration tests.
- Per service DoD: tenancy isolation test (user B gets 404 on user A's resource), health-check smoke test.
- NetArchTest: every user-owned entity has `UserId`; no references between service projects.
- Contract tests: previously serialized event payloads still deserialize.

### Test helpers live in exactly one place

**Never copy a test helper between test classes.** Unlike production code (where the rule is "extract on the third use"), a duplicated arrange helper gets extracted on the *second* — it's plumbing, not a domain decision, and copies drift silently. Two layers own it:

| Layer | Where | Holds |
| --- | --- | --- |
| Cross-service infrastructure | `Skarbiec.Testing` | `SkarbiecContainersFixture` (shared PG + RabbitMQ), `SkarbiecApiFactory<TProgram>`, `ServiceEndpointTests<TProgram>` (factory lifetime + per-test DB reset), `TestJwtIssuer` / `CreateAuthenticatedClient`, `TenancyIsolationTests<TProgram>` |
| Per-service domain | `<Service>.Tests/Fixtures/` | `<Service>EndpointTests` base binding the factory, `<Service>Api` (route builders + arrange calls), `<Service>Assertions` (invariants asserted from more than one slice), direct-DbContext access for facts HTTP can't express |

- A test class is `[Collection(TestingDefaults.CollectionName)]` + `: <Service>EndpointTests(containers)` + facts. It must **not** declare its own `_factory`, `InitializeAsync`/`DisposeAsync`, or route constants — the base and `<Service>Api` own those. (A service with only one host-backed test class may derive from `ServiceEndpointTests<Program>` directly and extract the per-service base when the second one arrives.)
- Fixture helpers are **arrange only** and `EnsureSuccessStatusCode`. A test asserting on endpoint X calls X directly and inspects the raw `HttpResponseMessage` — routing it through the helper would turn the failure under test into an exception. Say so in a comment at the top of such a class.
- Give helpers optional parameters with sane defaults (`name`, `assetClass`, `quantity`) so a call site states only what the fact depends on.
- Before adding a helper to a test class, check `Fixtures/` first — and when a fact needs a variant, add a parameter there instead of a private copy.

## Observability

- Everything through `Skarbiec.ServiceDefaults`: OTel traces/logs/metrics, `/health/live` + `/health/ready`. Never configure these per-service by hand.
