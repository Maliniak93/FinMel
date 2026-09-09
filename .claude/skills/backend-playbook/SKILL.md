---
name: backend-playbook
description: Vertical-slice, event, migration, and cross-service HTTP patterns for the four .NET services.
user-invocable: false
---

# Backend playbook
Target architecture: 4 services — Identity, Portfolio, MarketData, Reporting — behind the YARP Gateway. No gRPC.
Domain facts travel as events carrying **full state** through the MassTransit EF outbox; consumers are idempotent
via the inbox and never call the publisher back over REST. REST between services is allowed only for: (1)
request-path validation — Portfolio → MarketData instrument lookup, forwarding the caller's JWT; (2) Reporting →
MarketData price/FX batch endpoints, authenticated with a SystemCaller token.

## New vertical slice
1. Create `Features/<Name>/` with `<Name>Endpoint.cs`, `<Name>Handler.cs`, `<Name>Request.cs`/`<Name>Response.cs`. No Service/Repository layer, no MediatR.
2. Handler is a plain class resolved from DI. Returns `Result`/`Result<T>` — never throws for an expected failure (not found, conflict, validation, unavailable dependency).
3. Endpoint returns `Results<TSuccess, ProblemHttpResult>` (or a wider union). Map with `result.ToHttpResult()` for a plain 200/204+Problem shape, or `result.IsSuccess ? TypedResults.Created(...) : result.Error.ToProblem()` for a custom success shape (e.g. 201). Always `TypedResults`/`Results<...>` — never a bare `IResult`.
4. Register the route with a `Map<Name>Endpoint(this IEndpointRouteBuilder app)` extension, grouped via `app.MapGroup("/api/<service>/<feature-path>")`, called from `Program.cs`.
5. **Register the handler in `Program.cs`: `builder.Services.AddScoped<XHandler>();`.** Forgetting this compiles fine — the only symptom is the host failing at startup with a `[FromBody]` binding-inference error on the endpoint delegate. A brand-new slice failing that way means check `Program.cs` first.
6. Validation: DataAnnotations on the request record; cross-field rules via `IValidatableObject.Validate(...)`. Invalid input short-circuits to 400 automatically (`builder.Services.AddValidation()`).
7. `UserId` always from `ICurrentUser`/JWT claims — never the request body or route. Reads use `AsNoTracking()` with `Select` projections into response records.
8. Errors: a static `<Feature>Errors`/`<Entity>Errors` class, `new("<Category>.<Detail>", "message")`. `<Category>` must be one of `NotFound`, `Conflict`, `Validation`, `Unauthorized`, `Forbidden`, `ServiceUnavailable` — `ResultHttpExtensions.ToProblem()` maps the prefix before the first `.` to an HTTP status; anything else falls back to 400.
9. Copy the nearest existing slice in the same service rather than inventing structure.

## New event
1. Contract: a `record` in `contracts/Skarbiec.Contracts/Events` carrying the **full state** a consumer needs (event-carried state transfer) — not a delta, not just an id. ADR-019 (greenfield): edit the record in place; no `V2` type, no deprecate-and-add. Update every publisher, consumer, and test in the same change.
2. Publish only through the outbox: `await publishEndpoint.Publish(new TheEvent {...}, cancellationToken);` **before** the same `SaveChangesAsync` call that saves the business change, so both commit in one transaction. Never resolve `IBus` directly; never publish outside that transaction.
3. Consumer: `sealed class XConsumer(...) : IConsumer<TheEvent> { ... }`. Keep `Consume` idempotent in spirit even though the inbox stops redelivery — an outbound call inside it can still double-fire on a retry before the first attempt's inbox row commits. A consumer must never call the publishing service back over REST for more data — the event already carries what's needed.
4. Definition: `internal sealed class XConsumerDefinition : IdempotentConsumerDefinition<XConsumer, XDbContext>;` (empty — the base wires capped retry + `UseEntityFrameworkOutbox`).
5. Register both together inside `AddRabbitMqMessaging<...>()`: `configureConsumers: x => x.AddConsumer<XConsumer>(typeof(XConsumerDefinition))`. The two-generic-argument `AddConsumer<TConsumer, TDefinition>()` does not compile — `IRegistrationConfigurator`'s own arity-1 `AddConsumer<T>` hides it; pass the definition as a `Type` instead.
6. The consuming service's `DbContext.OnModelCreating` must call `AddInboxStateEntity()` alongside `AddOutboxMessageEntity()`/`AddOutboxStateEntity()`, with service-prefixed table names, backed by a migration.
7. Trace context propagates automatically through MassTransit + OTel — never add a manual correlation id.

## EF migrations
- `dotnet ef migrations add <Name> --project services/<Service>/Skarbiec.<Service>`, then `dotnet format`.
- Destructive changes are fine and backfill is optional (ADR-019, greenfield) — don't add compatibility shims for data that doesn't exist yet.
- `xmin` is EF's Postgres concurrency token, added as a **shadow property** — never let a migration add or drop a real `xmin` column; one appearing in a migration diff means the model is wrong, not the DB.
- An enum property configured with `HasDefaultValue(SomeEnum.X)` relies on EF treating the CLR-default value (`0`) as "unset" so it defers to the DB default. The member meant as the default **must be the first one declared** (value `0`) — reordering the enum silently breaks this.

## Cross-service HTTP — the only two allowed call sites
- Typed client: `builder.Services.AddHttpClient<IThing, Impl>(c => c.BaseAddress = new Uri("https+http://<service>-service"))`.
- User-context call (Portfolio → MarketData instrument lookup): add `.AddJwtForwardingHandler()` — forwards the caller's own JWT.
- Reporting → MarketData batch endpoints only: add `.AddSystemTokenHandler()`, and the MarketData endpoint requires `.RequireAuthorization(SystemCaller.PolicyName)`. Never use `AddSystemTokenHandler` anywhere else.
- On failure/timeout, return a `ServiceUnavailable.<Detail>` `Result` from the handler (maps to 503) — never let the exception bubble as a bare 500.

## Quartz jobs (MarketData only — ADR-007)
- Register the job and its trigger behind `Testing:DisableBackgroundJobs`: when set, register a `NoOp*Trigger` implementation of the trigger interface instead of scheduling anything, so HTTP slice tests can still resolve it with no live scheduler.
- Give the job its own `public const string ActivitySourceName` + `private static readonly ActivitySource`, and register it in `Program.cs`'s OpenTelemetry tracing builder via `.AddSource(TheJob.ActivitySourceName)`.
- External price/FX APIs (NBP, Stooq, CoinGecko) are called **only** from these jobs — never from a request handler. The one exception is `ITickerVerifier` (`Skarbiec.MarketData.Sources.Verification`, ADR-018): a synchronous, in-request check that a user-supplied ticker exists at its provider before the instrument is created — a ticker already in the DB skips the external call entirely, an unverified one gets one attempt with a hard ~5 s timeout and no rate-limit retry, and it never persists a quote or values anything.

## OpenAPI
`builder.AddServiceOpenApi();` alongside the other `builder.Add...()` calls; `app.MapServiceOpenApi("<service>")` alongside `app.MapDefaultEndpoints()`. Development-only — production never maps the document.

## After adding or changing an endpoint
1. Add or update a working request in `requests/<service>.http` (reuse the file's `{{baseUrl}}` and chained `@name` variables).
2. `cd web && npm run gen:api` — see `frontend-playbook` for the diff-check procedure (the generator can drop unrelated `.js` import extensions repo-wide; don't let that slip into the change).

## New service checklist
Compare `<Service>` against Portfolio/Reporting/MarketData/Identity — every one of these exists per service:
1. Project references `Skarbiec.ServiceDefaults`.
2. `DbContext` with the `UserId` global query filter + save interceptor.
3. Own database + user: `await AddServiceDatabase("<name>", "<name>_db")` in `Skarbiec.AppHost/AppHost.cs`.
4. YARP routes for `/api/<name>/*` in `gateway/Skarbiec.Gateway/appsettings.json` (business + `-openapi` routes, matching an existing service's block).
5. CI job + path filter for `services/<Name>/**` in `.github/workflows/ci.yml`.
6. Docker entry for the service's Dockerfile directory in `.github/dependabot.yml`.
7. Entry in `web/openapi-ts.config.ts` for hey-api generation.
8. `[CollectionDefinition(TestingDefaults.CollectionName)]` in the new test project (xUnit only discovers one declared in the assembly under test).
9. `requests/<name>.http`.

## Conventions and ADRs
Changing a convention or a hard rule in the same change that motivated it: update `.claude/rules/*.md` and/or append an entry to `skarbiec-plan/decisions.md`. Never leave the code and the documented rule disagreeing.

## Definition of done
- Tests green, including tenancy isolation for any new user-owned resource.
- Zero warnings (`TreatWarningsAsErrors`), `dotnet format` clean.
- Handler registered in `Program.cs`; endpoint mapped; migration present if the model changed.
- `requests/*.http` and the generated TS client updated when the API surface changed.
- Rules/ADRs updated when a convention or hard rule changed.
