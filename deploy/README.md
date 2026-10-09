# Deploy notes

## Local: Aspire AppHost (T0.4)

`dotnet run --project Skarbiec.AppHost` starts one PostgreSQL container and one RabbitMQ
container (management plugin enabled) plus every registered service, wired through the Aspire
dashboard (traces/logs/metrics out of the box, ADR-014).

### Per-service database users (ADR-003)

One PostgreSQL instance hosts a database per service (`identity_db`, `portfolio_db`,
`marketdata_db`, `reporting_db`). Each database is provisioned with its **own**
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

### EF Core migrations (T0.5)

Each service applies its own pending EF Core migrations at startup (`Database.MigrateAsync()`),
but only when `ASPNETCORE_ENVIRONMENT=Development` — convenient for local Aspire runs, but running
schema changes on every container start is not something production should do implicitly. In
production (T0.18), migrations are an explicit step in the deploy script instead.

While ADR-019 (greenfield mode) holds, migrations carry no data: dropping a database and letting it
be recreated from scratch is an acceptable upgrade path, and a service's migration history may be
squashed to a single `InitialCreate`. Production migration discipline arrives with T0.18.

MarketData's Quartz job store is not part of its migrations: the scheduler provisions its own
`quartz.qrtz_*` tables at first start (`ProvisionSchema()`, `Sources/QuartzStore.cs`) and creates the
`quartz` schema itself beforehand. So the MarketData **runtime** DB account — not only the migration
step — needs `CREATE` on its database (to create schema `quartz`) and on that schema (tables, indexes).
Locally `marketdata_user` owns `marketdata_db`, which covers both.

## Local: docker compose

The whole stack (Postgres, RabbitMQ, four services, Gateway, Angular app behind nginx, Aspire dashboard) also runs from locally built images, independent of the working tree and of `/build`. Aspire stays the development entry point; this is a side stack for trying a shipped build before its PR is merged.

```bash
node scripts/compose.mjs build [--ref <ref>]   # six skarbiec-local/* images from a git archive of <ref> (default HEAD)
node scripts/compose.mjs up                    # docker compose up -d --wait on the built images
node scripts/compose.mjs down                  # stop and remove the containers, keep the volumes
node scripts/compose.mjs reset                 # down + remove the volumes (databases and queues gone)
node scripts/compose.mjs status                # container states and whether each runs the newest image
```

- Images are built from a commit (`git archive`), never the working tree; each is labelled `org.opencontainers.image.revision=<sha>`. `ship.mjs` runs `build --ref <shipped sha>` after every successful ship; `build` never starts, stops or recreates a container, so a running stack keeps its old image until the next `up` (`status` flags it as outdated). The previous, now-dangling image is removed unless a container still uses it.
- Ports: app `http://localhost:8080` (nginx: the Angular app, `/api/` proxied to the Gateway), Aspire dashboard `http://localhost:18888`, Postgres `localhost:55432`. RabbitMQ, the services and the Gateway publish no host port, so `/internal` stays unreachable (ADR-027).
- Services run with `ASPNETCORE_ENVIRONMENT=Development`: startup migrations, the MarketData seed and the Quartz price sync run as under Aspire. Passwords are fixed local-only values (same rationale as above); the JWT key is the committed local-dev one.
- Volumes `skarbiec-local_postgres-data` and `skarbiec-local_rabbitmq-data` are separate from Aspire's. There is no auto-start: after a reboot the stack stays down until `compose.mjs up`.
- Requires Docker running. `stop-stack.mjs` only kills host processes, so it leaves this stack alone.

## Production: docker compose on a VPS (T0.18)

Not built yet — see `skarbiec-plan/zadania/phase-0-platform.md` T0.18.

Requirement for whatever T0.18 builds: only the Gateway publishes a port; every service sits on an
internal network the outside world cannot reach. Service-only endpoints under `/internal/**` are
anonymous and trust the network, not a token (ADR-027) — exposing a service's port directly would
expose them.
