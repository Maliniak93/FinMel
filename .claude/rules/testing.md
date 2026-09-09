---
paths:
  - "**/*Tests*/**"
  - "Skarbiec.Testing/**"
---

# Testing conventions

xUnit v3 + Testcontainers (PostgreSQL, RabbitMQ). Wiring walkthrough: `Skarbiec.Testing/README.md`.

## Tests come first, and they come from the spec

- Write the tests **red**, straight from the spec's acceptance criteria, and run them to confirm they fail for the intended reason before any implementation exists.
- One test per acceptance criterion, named `Method_Scenario_Outcome` (`Get_ByStranger_ReturnsNotFound`, `Add_WhenMarketDataUnavailable_ReturnsServiceUnavailable`).
- Per service DoD: tenancy isolation tests, a health-check test, and an outbox/idempotency test for every event the service publishes or consumes.
- NetArchTest guards the architecture: every user-owned entity has `UserId`; no references between service projects.

## Test helpers live in exactly one place

**Never copy a test helper between test classes.** Unlike production code (extract on the third use), a duplicated arrange helper gets extracted on the *second* — it is plumbing, not a domain decision, and copies drift silently. Two layers own it:

| Layer | Where | Holds |
| --- | --- | --- |
| Cross-service infrastructure | `Skarbiec.Testing` | `SkarbiecContainersFixture` (shared PG + RabbitMQ), `SkarbiecApiFactory<TProgram>`, `ServiceEndpointTests<TProgram>` (factory lifetime + per-test DB reset), `TestJwtIssuer` / `CreateAuthenticatedClient`, `TenancyIsolationTests<TProgram>`, `HostlessOutboxProvider` |
| Per-service domain | `<Service>.Tests/Fixtures/` | `<Service>EndpointTests` base binding the factory, `<Service>Api` (route builders + arrange calls), `<Service>Assertions` (invariants asserted from more than one slice), direct-DbContext access for facts HTTP cannot express |

Before adding a helper to a test class, check `Fixtures/` first — and when a fact needs a variant, add a parameter there instead of a private copy.

## Test class shape

- `[Collection(TestingDefaults.CollectionName)]` + `: <Service>EndpointTests(containers)` + facts. It must **not** declare its own `_factory`, `InitializeAsync`/`DisposeAsync`, or route constants — the base and `<Service>Api` own those. (A service with a single host-backed test class may derive from `ServiceEndpointTests<Program>` directly; extract the per-service base when the second one arrives.)
- Each test project needs its own one-line `[CollectionDefinition]` — xUnit only discovers it in the assembly under test.
- Respawn resets the database in `InitializeAsync`, i.e. before **every** `[Fact]` (xUnit builds a fresh class instance per fact). Reset through the factory (`ResetDatabaseAsync()`), never the containers fixture directly — the factory boots the host and applies migrations first, and Respawn needs the schema.

## Fixture helpers are arrange only

They `EnsureSuccessStatusCode`. A test asserting on endpoint X **calls X directly** and inspects the raw `HttpResponseMessage` — routing it through a helper would turn the failure under test into an exception. Say so in a comment at the top of such a class. Give helpers optional parameters with sane defaults (`name`, `assetClass`, `quantity`) so a call site states only what its fact depends on.

## Tenancy isolation

- A flat, top-level resource: inherit `TenancyIsolationTests<TProgram>` and supply only how to create, locate and list that one resource. The four facts (stranger's GET/PUT/DELETE all 404 — never 403, which leaks existence — plus absence from the stranger's listing) then come for free.
- A nested resource (asset under portfolio, transaction under asset) cannot use the template as-is: reimplement the same four facts **plus** the sneaky paths — a stranger's parent id in the route, the owner's parent id with a stranger's token, and a cross-owner id pair.

## Messaging and background work

- Outbox and durability tests build their provider with `HostlessOutboxProvider`: MassTransit's hosted services never start, so the delivery poller cannot remove the row before the assertion reads it. Consumer tests that need a live bus use a queue name unique to the test class — queues are durable and outlive one class on the shared broker.
- `SkarbiecApiFactory<TProgram>` sets `Testing:DisableBackgroundJobs`, so Quartz schedules are skipped and triggers resolve to their NoOp implementation. A test that wants the real job registers Quartz itself.
- `/health/ready` needs polling with a deadline, not a single assertion: MassTransit's health check reports "not started" for a moment after the host comes up — exactly the window a real readiness probe rides out.

## Running

- `dotnet test` requires Docker to be running.
- One project at a time while iterating: `dotnet test services/<S>/Skarbiec.<S>.Tests`.
- Pure functions (valuation and insight math, `Money`, `Result`, currency validation) get fast table-driven unit tests with `[Theory]`/`[InlineData]` — no containers, no host.
