---
name: testing-playbook
description: xUnit/Testcontainers conventions, fixture ownership, and tenancy/outbox/idempotency test patterns.
user-invocable: false
---

# Testing playbook
xUnit v3 + Testcontainers (PostgreSQL, RabbitMQ). **Docker must be running** — every slice/integration test needs
it. Write tests RED first: derive them from the spec's acceptance criteria, run them, and confirm they fail before
any production code exists. A compile failure because the API doesn't exist yet is an acceptable red — report
which type/member is missing. A new test that passes immediately is a bug in the test or a spec already satisfied
— never leave that unresolved.

## Where a helper lives — never copy one
- Cross-service infra (containers, factory base, JWT issuing, tenancy template, hostless outbox) → `Skarbiec.Testing`.
- Per-service arrange helpers, endpoint-test base, cross-slice assertions → `<Service>.Tests/Fixtures/`.
- Unlike production code (extract on the third use), a duplicated **test helper** is extracted on the **second** use — it's plumbing, and copies drift silently. Before adding anything to a test class, check `Fixtures/` first; if it's missing what you need, add it there (a new helper, or a new optional parameter on an existing one), never as a private copy inside the test class.

## Test class shape
`[Collection(TestingDefaults.CollectionName)] public sealed class XTests(SkarbiecContainersFixture containers) : <Service>EndpointTests(containers)` with `[Fact]`s only. No local `_factory`, no `InitializeAsync`/`DisposeAsync`, no route constants — the `<Service>EndpointTests` base and `<Service>Api` fixture own those. Name every fact `Method_Scenario_Outcome`.

## Calling the system under test
- Fixture helpers (`<Service>Api`) are **arrange only** and call `EnsureSuccessStatusCode()` internally — use them to set up state the fact doesn't care about (e.g. "a portfolio to add an asset to").
- The slice actually under test is called **directly**, inspecting the raw `HttpResponseMessage` (status code, `ProblemDetails`, body) — never through a fixture helper, which would turn the failure you're testing for into an unrelated exception.

## Tenancy isolation — required for every new user-owned resource
User B must get: `404` on `GET`/`PUT`/`DELETE` of user A's resource (never `403` — that leaks existence), an empty result from `LIST`, and `404` on any "sneaky" nested path (e.g. listing `/portfolios/{a's id}/assets` as user B). For a flat resource, inherit `TenancyIsolationTests<TProgram>` from `Skarbiec.Testing` and implement only `CreateResourceAsync`, `ListUrl`, `CreateUpdatePayload`, `AssertResourceAbsentFromListAsync` — the four isolation facts are then proven for you. For a nested resource, write the equivalent facts by hand alongside the slice's own tests.

## Outbox tests — required for every new event publish
Use `HostlessOutboxProvider.Build<TDbContext>(containers, configureServices, configureConsumers)` from `Skarbiec.Testing/Messaging` to build a bare `ServiceProvider` with the EF outbox wired but **no hosted bus running** — a live bus would race its own delivery poller against your assertion. Assert the outbox row exists in the same transaction/`SaveChanges` as the business row it accompanies.

## Idempotency and contract tests — required for every new/changed event
- **Idempotency**: deliver the same `MessageId` to the consumer twice; assert the side effect (a row, a counter) happened exactly once.
- **Contract deserialization**: a previously-serialized payload — and one with unknown extra fields — still deserializes. Events carry full state and are edited in place (ADR-019), so this is what catches an accidental breaking shape change.

## Pure functions
Valuation, allocation, rebalancing, quantity-recompute math: table-driven `[Theory]`/`[InlineData]` unit tests, no containers, no host. Property-style assertions (e.g. "recompute from scratch always agrees with the incremental result") where they're cheap to state.

## Other facts
- Health-check tests poll `/health/ready` for up to 10 s (a fresh Testcontainers Postgres/RabbitMQ can take a moment to become ready).
- `Testing:DisableBackgroundJobs` is set to `true` on every test host by `SkarbiecApiFactory`; a service with scheduled work (Quartz) must check this key before scheduling anything, and provide a `NoOp*` implementation of any trigger interface a slice test still needs to resolve.
- Run one project at a time: `dotnet test services/<Service>/Skarbiec.<Service>.Tests` (add `--filter "FullyQualifiedName~<Name>"` to target one class while iterating).
