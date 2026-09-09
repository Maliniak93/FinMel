---
paths:
  - "**/*.cs"
  - "**/*.csproj"
  - "**/*.props"
  - "**/*.slnx"
---

# .NET conventions (C# 14 / .NET 10)

Messaging lives in `messaging.md`, tests in `testing.md`, domain rules in `domain.md`.

## Language

- `<Nullable>enable</Nullable>`, implicit usings, file-scoped namespaces everywhere.
- `record` for DTOs, requests/responses and event contracts; `required` members over constructor telescoping.
- Classes `sealed` by default; primary constructors for DI; pattern matching over type checks and casts.
- No `async void`. Accept a `CancellationToken` in every handler, consumer and job, and pass it through every EF and HTTP call.

## Solution hygiene

- `Directory.Build.props` at the repo root owns `LangVersion`, `TreatWarningsAsErrors`, `AnalysisLevel`, `ImplicitUsings`, `Nullable` — never set them per project.
- Central Package Management: package versions live only in `Directory.Packages.props`.
- Add every new project to `Skarbiec.slnx`; build with `dotnet build Skarbiec.slnx`.
- Warnings are errors. Run `dotnet format` before handing work off.

## Minimal APIs

- Group endpoints with `MapGroup("/api/<service>/<feature>")`; one endpoint file per slice, registering itself through a `Map<Feature>Endpoint()` extension method called from `Program.cs`.
- Return `TypedResults` inside a `Results<T1, T2, ...>` union — never a bare `IResult`.
- Validation: `builder.Services.AddValidation()` plus DataAnnotations on the request record; .NET 10's built-in Minimal API validation short-circuits invalid input to a 400 ProblemDetails. Cross-field rules go in `IValidatableObject` on the same record, or a validator class in the slice.
- Errors are ProblemDetails end to end: bad input → 400 automatically; an expected failure (not found, conflict, business rule) → the handler's failed `Result` mapped by `ResultHttpExtensions` in ServiceDefaults (ADR-017); a genuinely unexpected failure → an exception caught by ServiceDefaults' `AddProblemDetailsWithTraceId()`. Every response carries the trace id.
- OpenAPI through ServiceDefaults only: `builder.AddServiceOpenApi()` + `app.MapServiceOpenApi("<service>")`, which puts the document under the service's own `/api/<service>/openapi/...` prefix so it flows through the existing Gateway route. Never add Swashbuckle.

## Slices (ADR-002, ADR-004)

- `Features/<FeatureName>/` = endpoint + handler + validator (+ request/response records). The handler is a plain class resolved from DI — no MediatR, no Service or Repository layer.
- Handler methods return `Result`/`Result<T>` (the `Result`, `Result<T>` and `Error` types live in `Skarbiec.Contracts`). Never throw for an expected failure; exceptions stay reserved for the genuinely unexpected.
- **Register every handler**: `builder.Services.AddScoped<XHandler>();` in `Program.cs`, next to the others. Forgetting it still builds green — the host then fails at startup because Minimal API parameter binding tries to infer the unregistered handler as `[FromBody]` and reports two body parameters.
- Extract shared code only on the third use.
- `UserId` always comes from the JWT via `ICurrentUser` — never from the request body, query or route (ADR-006).

## EF Core 10

- One `DbContext` per service; its migrations live in the service project. No lazy loading, no `Include` chain crossing an aggregate boundary.
- Reads use `AsNoTracking()` and project with `Select` straight into the response record.
- Tenancy: a global query filter on `UserId` plus the save interceptor that stamps it (ADR-006).
- `decimal` with explicit precision — `HasPrecision(18, 8)` for quantities and prices, `(18, 2)` for money amounts. Never `double`/`float`.
- Time: `DateTimeOffset` in UTC (`timestamptz`); `DateOnly` for quote and rate dates. Unique indexes as listed in `domain.md`.
- Concurrency: an `xmin` shadow property as the concurrency token. A migration must never create, drop or alter it — if a generated migration touches `xmin`, delete those lines by hand.
- Enums are stored as ints, so with `HasDefaultValue` the CLR default 0 is what an existing row gets: the member that means "not set" (or the intended default) must be the one with value 0. Ordering enum members carelessly silently rewrites history.
- Run `dotnet format` right after `dotnet ef migrations add` — generated migration files are not formatted.
- Migrations may be destructive and may be squashed to a single `InitialCreate` (ADR-019). No backfill scripts, no compatibility shims; drop the local database instead.
- Cross-service references are plain `Guid` columns — no FK, no navigation property (ADR-003).

## HTTP between services

- One typed `HttpClient` per dependency, based at `https+http://<service>-service` (Aspire service discovery). Resilience comes from ServiceDefaults' `ConfigureHttpClientDefaults` — do not add retry or circuit-breaker policies per client.
- `.AddJwtForwardingHandler()` when the call happens inside a user's request and the downstream service must see that user (Portfolio → MarketData instrument lookup).
- `.AddSystemTokenHandler()` only for the daily Reporting → MarketData price/FX batch endpoints, which are the only endpoints behind the `SystemCaller` policy. Jobs and consumers have no inbound token to forward, which is the whole reason it exists.
- `IgnoreQueryFilters()` is allowed only in those `SystemCaller` endpoints and in consumers that legitimately write for many users. Every such call needs a comment saying why.
- A transport failure is an expected failure: return a `ServiceUnavailable`-prefixed `Error` so the endpoint maps it to 503 (see `Features/AssetErrors.cs` in Portfolio) — never let the `HttpRequestException` escape.

## Quartz jobs (MarketData only)

- Register the schedule behind `Testing:DisableBackgroundJobs`; when it is set, register the NoOp trigger implementation instead (`NoOpSyncTrigger`, `NoOpHistoryBackfillTrigger`) so slice tests never race a job.
- Each job owns an `ActivitySource` exposed as `public const string ActivitySourceName`, registered in the service's `AddSource(...)` call in `Program.cs`, and starts an activity named `<Job>.Run`.
- Jobs are the only place that talks to NBP, Stooq or CoinGecko (ADR-007) — never a request path, with `ITickerVerifier` as the single exception (ADR-018).

## Observability

Everything through `Skarbiec.ServiceDefaults`: OTel traces/logs/metrics, `/health/live`, `/health/ready`, JWT auth, ProblemDetails. Never configure any of these per service by hand.
