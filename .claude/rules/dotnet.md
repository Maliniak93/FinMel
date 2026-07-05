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
- All errors as ProblemDetails (`AddProblemDetails()` + exception handler in ServiceDefaults); correlation/trace id included in the response.
- OpenAPI: built-in `Microsoft.AspNetCore.OpenApi` (`AddOpenApi()`/`MapOpenApi()`) — no Swashbuckle. The generated doc feeds the Angular TS client.

## Slices (ADR-002, ADR-004)

- `Features/<FeatureName>/` = endpoint + handler + validator (+ request/response records). Handler is a plain class resolved from DI — no MediatR, no Service/Repository layers.
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

## Observability

- Everything through `Skarbiec.ServiceDefaults`: OTel traces/logs/metrics, `/health/live` + `/health/ready`. Never configure these per-service by hand.
