# Deploy notes

## Local: Aspire AppHost (T0.4)

`dotnet run --project Skarbiec.AppHost` starts one PostgreSQL container and one RabbitMQ
container (management plugin enabled) plus every registered service, wired through the Aspire
dashboard (traces/logs/metrics out of the box, ADR-014).

### Per-service database users (ADR-003)

One PostgreSQL instance hosts a database per service (`identity_db`, `portfolio_db`,
`marketdata_db`, `strategy_db`, `reporting_db`). Each database is provisioned with its **own**
Postgres role, scoped so it can only connect to its own database:

- `Skarbiec.AppHost/AppHost.cs` has a local `AddServiceDatabase(serviceName, databaseName)`
  helper. For each service it:
  1. Defines a **fixed** (not generated) local-dev-only password parameter `<service>-db-password`
     — see "Why fixed, not generated passwords" below for why these deliberately don't use
     Aspire's auto-generated/persisted secrets.
  2. Calls `postgres.AddDatabase(...)` with a custom `WithCreationScript(...)` that runs, against
     the admin `postgres` database, right after the Postgres container becomes ready:
     ```sql
     CREATE DATABASE "<db>";
     CREATE USER "<db>_user" WITH PASSWORD '<generated>';
     REVOKE CONNECT ON DATABASE "<db>" FROM PUBLIC;
     GRANT CONNECT, TEMP ON DATABASE "<db>" TO "<db>_user";
     ALTER DATABASE "<db>" OWNER TO "<db>_user";
     ```
  Postgres grants `CONNECT` on every new database to `PUBLIC` by default — the `REVOKE`/`GRANT`
  pair is what actually enforces isolation; without it any role could open a connection to any
  database on the instance. Ownership gives the service's own user full DDL rights for its own
  EF Core migrations later.
- The script is safe to re-run: Aspire calls it every time the AppHost starts, and Postgres
  raises `42P04` ("database already exists") on the `CREATE DATABASE` line, which Aspire catches
  and ignores — the rest of the script (role/grants) only ever runs once, on first creation.

**Manual verification** (documented per T0.4's AC, since no real service consumes these users
yet — that starts in T0.5):

```bash
docker exec -it <postgres-container> psql -U <db>_user -d <db> -c "select current_user;"   # succeeds
docker exec -it <postgres-container> psql -U <db>_user -d <other_db> -c "select 1;"          # fails: FATAL permission denied for database
```

**For T0.5+**: a service must **not** reference the `PostgresDatabaseResource` returned by
`AddServiceDatabase` directly with `.WithReference(...)` — that resource's default connection
string uses the Postgres server's *admin* user, not the service's own restricted user. Build a
dedicated connection string instead, reusing the same `<service>-db-password` parameter, e.g.:

```csharp
var identityConnectionString = builder.AddConnectionString(
    "identity-db",
    ReferenceExpression.Create(
        $"Host={postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Host)};" +
        $"Port={postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Port)};" +
        $"Database=identity_db;Username=identity_user;Password={identityDbPassword}"));
```

### Data persistence / idempotent restarts

Both the Postgres and RabbitMQ resources use `.WithDataVolume()` (named Docker volumes, not bind
mounts) — restarting the AppHost reuses the same containers' data instead of losing it, and
`docker volume ls` shows no orphaned volumes accumulating across restarts (Aspire reuses the same
volume name, derived from the app + resource name, every run). Verified manually across several
container restarts: the databases and per-service roles/grants from the very first run were still
there — only the (empty) `CREATE DATABASE` retry hit `42P04` and was skipped.

### Why fixed, not generated, local-dev passwords

The Postgres admin password, the RabbitMQ admin password, and every `<service>-db-password` are
**fixed literal parameters** (`builder.AddParameter(name, "literal-value", secret: true)`), not
Aspire's auto-generated-and-persisted-to-user-secrets default. This was a deliberate fix after
hitting a real outage: Postgres/RabbitMQ bake their admin credentials into the data volume the
*first* time they initialize and never change them again. Aspire's generated-secret parameters
only stay in sync with that baked-in value as long as they're faithfully reloaded from
user-secrets on *every single run* — which requires `ASPNETCORE_ENVIRONMENT`/
`DOTNET_ENVIRONMENT=Development` to be set (normally via the default launch profile). Any run that
skips that (`--no-launch-profile` with no environment override, a different IDE run
configuration, CI, etc.) silently regenerates a fresh random value and overwrites `secrets.json` —
while the already-initialized volume keeps expecting the old one. From that point on, *every*
connection — including Aspire's own resource health checks — fails authentication, and Postgres
and RabbitMQ show as permanently "Unhealthy" in the dashboard no matter how many times you
restart, because there's no self-healing: the mismatch persists until something makes the two
values match again.

A fixed value can't drift, because there's nothing to regenerate — this eliminates the failure
mode entirely rather than just documenting around it. These aren't real secrets in any case:
they're throwaway localhost containers for local dev only (production, in T0.18, uses `.env`
per ADR-005/011).

**If you ever do see Postgres/RabbitMQ "Unhealthy"** (e.g. after changing one of these fixed
passwords in code without also resetting local state): the volumes are out of sync with whatever
password is currently configured. Fix by removing the named volumes so they reinitialize cleanly
on the next run — no real data exists yet in Phase 0, so this is always safe:
```bash
docker volume rm skarbiec.apphost-<hash>-postgres-data skarbiec.apphost-<hash>-rabbitmq-data
```
(find the exact names with `docker volume ls`).

## Production: docker compose on a VPS (T0.18)

Not built yet — see `skarbiec-plan/zadania/phase-0-platform.md` T0.18.
