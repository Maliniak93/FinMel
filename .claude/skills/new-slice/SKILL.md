---
name: new-slice
description: Scaffold a vertical slice (endpoint + handler + validator + tests) in a Skarbiec service.
argument-hint: <Service> <FeatureName>
disable-model-invocation: true
---

Scaffold a vertical slice in `services/<Service>/Features/<FeatureName>/`.

If `Skarbiec.sln` does not exist yet, stop: implementation has not started — point at T0.1 in `skarbiec-plan/zadania/phase-0-platform.md`.

## Steps

1. Parse `$ARGUMENTS` for service and feature name; ask one question if missing.
2. Create the slice folder containing:
   - `<FeatureName>Endpoint.cs` — Minimal API endpoint, registered in the service's endpoint mapping; maps the handler's `Result`/`Result<T>` to `TypedResults`/ProblemDetails (ADR-017)
   - `<FeatureName>Handler.cs` — plain class resolved from DI (no MediatR); returns `Result`/`Result<T>`, never throws for expected failures
   - `<FeatureName>Validator.cs` — request validation
3. Tenancy: read `UserId` from JWT claims only; rely on the EF global query filter. Never accept `UserId` from the request body.
4. Tests in the service's test project:
   - integration test on Testcontainers (PostgreSQL; RabbitMQ if the slice publishes events)
   - tenancy isolation test: user B gets 404 on user A's resource
5. Use an existing slice in the same service as the pattern reference. Extract shared code only on the third use.

## Rules

- No Service/Repository layers, no MediatR (ADR-002, ADR-004).
- Handlers return `Result`/`Result<T>` for expected failures; the endpoint maps them to ProblemDetails, never throw for expected error paths (ADR-017).
- Money as `decimal`/`Money`; base currency PLN.
- If the slice publishes an event, follow /new-event conventions (outbox only).
