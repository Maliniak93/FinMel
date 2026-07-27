---
name: new-service
description: Scaffold a new Skarbiec microservice skeleton. Requires an approved ADR - splitting services without one is forbidden.
argument-hint: <Name>
disable-model-invocation: true
---

STOP first: ADR-001 forbids adding services without an ADR. Ask which ADR approves this service and refuse to proceed without one.

If `Skarbiec.sln` does not exist yet, stop: implementation has not started — point at T0.1 in `skarbiec-plan/zadania/phase-0-platform.md`.

## Checklist

1. Project under `services/<Name>/` referencing `Skarbiec.ServiceDefaults` (OpenTelemetry, health checks `/health/live` + `/health/ready`, JWT auth, HTTP resilience, `Result`→ProblemDetails mapping helper).
2. Own database `<name>_db` with a dedicated DB user that has no access to other databases. No cross-DB joins, ever.
3. EF Core: DbContext, migrations, global query filter on `UserId` + save interceptor.
4. Register the service and its database in the Aspire AppHost.
5. YARP route `/api/<name>/*` in the Gateway.
6. CI: path filter for `services/<Name>/**` in GitHub Actions.
7. Tests from day one: health-check smoke test + tenancy isolation test.
