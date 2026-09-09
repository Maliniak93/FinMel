# Phase 0 — Platform

**Duration:** 4–5 weeks, **hard limit 5 weeks** — anything missing goes to the backlog, not into Phase 0.
**Goal:** the whole platform skeleton works end-to-end: one F5 starts everything locally, one messaging flow runs through the outbox, CI builds only what changed. VPS deployment (T0.18) is optional/deferred while development stays local-only — see `zadania/README.md`.
**Deliverable (from roadmap):** login through the Gateway locally, trace Gateway→Identity in the Aspire dashboard; the same "in production" deliverable is optional/deferred until T0.18 lands.
**Sources:** roadmap Phase 0, E8/E9, ADR-002/003/005/006/010/012/013/014.

## Task index

| ID    | Task                                                 | Size | Depends on       |
| ----- | ---------------------------------------------------- | ---- | ---------------- |
| T0.1  | Monorepo scaffold                                    | S    | —               |
| T0.2  | `Skarbiec.Contracts` + SharedKernel                | M    | T0.1             |
| T0.3  | `Skarbiec.ServiceDefaults`                         | L    | T0.1             |
| T0.4  | Aspire AppHost                                       | M    | T0.3             |
| T0.5  | Identity: service skeleton                           | M    | T0.3, T0.4       |
| T0.6  | Identity: registration                               | M    | T0.5             |
| T0.7  | Identity: login + JWT issuance                       | L    | T0.6             |
| T0.8  | Identity: refresh + logout                           | M    | T0.7             |
| T0.9  | Test infrastructure (Testcontainers)                 | L    | T0.5             |
| T0.10 | MassTransit + EF Outbox,`UserRegistered`           | L    | T0.2, T0.6, T0.9 |
| T0.11 | Outbox durability test                               | M    | T0.10            |
| T0.12 | Idempotent consumer template (inbox)                 | M    | T0.10            |
| T0.13 | Skeletons: Portfolio/MarketData/Strategy/Reporting   | L    | T0.3, T0.4       |
| T0.14 | Architecture test + tenancy test template            | M    | T0.9, T0.13      |
| T0.15 | YARP Gateway                                         | L    | T0.7, T0.13      |
| T0.16 | Spike ADR-010: Angular Material vs PrimeNG           | M    | —               |
| T0.17 | CI: GitHub Actions with path filters                 | L    | T0.9             |
| T0.18 | [VPS, optional] Deploy: Dockerfiles, compose, VPS    | L    | T0.15, T0.17     |
| T0.19 | Phase exit: local trace check (+ optional VPS smoke) | S    | T0.15            |

---

### T0.1 Repo: monorepo scaffold

- [X] **Status:** done · **Size:** S

- **Depends on:** —
- **Refs:** ADR-001 (monorepo), `02-architecture.md` §repo layout

**Goal:** the repository has the agreed structure and shared build configuration; `dotnet build` succeeds on an empty solution.

**Scope:**

- Folders: `services/`, `gateway/`, `contracts/`, `web/`, `deploy/` (keep `skarbiec-plan/`).
- `Skarbiec.sln` (or slnx), root `Directory.Build.props`: .NET 10, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, LangVersion C# 14.
- `Directory.Packages.props` — central package management from day 1.
- `.editorconfig` (file-scoped namespaces, `sealed` by default via analyzers where possible), `.gitignore`, root `README.md` (one paragraph + links to `skarbiec-plan/`).

**Acceptance criteria:**

- [X] `dotnet build` green on a clean clone.
- [X] All folders exist and are committed (use `.gitkeep` where empty).
- [X] Central package versions — no `Version=` in individual `.csproj` files.

---

### T0.2 Contracts: `Skarbiec.Contracts` + SharedKernel primitives

- [X] **Status:** done · **Size:** M
> Amended by ADR-019 — contract versioning rules relaxed while greenfield mode holds (records edited in place, no `V2`); `CONTRACTS.md` rewritten accordingly.

- **Depends on:** T0.1
- **Refs:** E9 [S] (contract versioning), ADR-008, ADR-012, `03-domain-model.md` §asset classes

**Goal:** one project holds all cross-service event/DTO records and shared primitives; versioning rules are written down next to the code.

**Scope:**

- `contracts/Skarbiec.Contracts` class library: C# records only, no logic beyond validation of invariants.
- `Result`/`Result<T>`/`Error` primitives (ADR-017) — every validating factory (starting with `Money.Create`) returns these instead of throwing; live here (not in ServiceDefaults) because Contracts has zero solution-internal dependencies.
- `Money` value object (`decimal Amount`, `string Currency`; PLN default per ADR-008) — invariants: amount precision, non-null currency; validated via `Money.Create(...)` → `Result<Money>` (ADR-017), no throwing constructor.
- `AssetClass` enum: `Cash`, `Deposit`, `Stock`, `Etf`, `Bond`, `Crypto`, `PreciousMetal`, `RealEstate`, `Other`.
- First event record: `UserRegistered` (UserId, Email, DisplayName, occurredAtUtc).
- `CONTRACTS.md` in the project: additive versioning rules (new field = optional with default; breaking change = new `V2` record type; never rename/remove).

**Acceptance criteria:**

- [X] Project referenced only via `ProjectReference` (no service internals leak in).
- [X] Deserialization contract test: an event serialized from a JSON fixture with *extra* unknown fields still deserializes (forward compatibility).
- [X] `Money` unit tests: negative amount rejected where invariant requires, currency required (via `Result<Money>` failure, not an exception).
- [X] `Result`/`Result<T>` unit tests: success exposes the value; failure exposes the error and blocks value access.

---

### T0.3 Platform: `Skarbiec.ServiceDefaults`

- [X] **Status:** done · **Size:** L

- **Depends on:** T0.1
- **Refs:** E9 [M] (shared ServiceDefaults), E8 [M] (OTel), `02-architecture.md` §observability

**Goal:** one shared project wires OTel, health checks, JWT auth and HTTP resilience; every service gets all of it with a single `AddServiceDefaults()` call.

**Scope:**

- Start from the Aspire ServiceDefaults template project; extend it.
- OpenTelemetry: traces + metrics + logs, OTLP exporter, W3C `traceparent` propagation (HTTP handled by ASP.NET Core; RabbitMQ handled later by MassTransit).
- Health checks: `/health/live` (self) and `/health/ready` (dependencies: DB, broker — registered per service).
- JWT bearer auth extension: reads signing key + issuer from configuration; maps `sub`→`UserId` claim accessor helper (`ICurrentUser` or similar) — **UserId always from claims, never from the body** (ADR-006).
- `Microsoft.Extensions.Http.Resilience` defaults for outgoing HTTP: retry with jitter, circuit breaker, timeout.
- ProblemDetails as the default error shape (`AddProblemDetails()` + exception handler with correlation id).
- Shared `Result`/`Result<T>` → `TypedResults`/ProblemDetails mapping helper, built on the `Result`/`Result<T>`/`Error` types from `Skarbiec.Contracts` (T0.2) — every service's slices use this instead of throwing for expected failures (ADR-017).

**Acceptance criteria:**

- [X] A sample service referencing ServiceDefaults exposes `/health/live` + `/health/ready` and emits traces visible in the Aspire dashboard.
- [X] Unauthenticated call to a protected endpoint → 401; garbage token → 401.
- [X] Errors return ProblemDetails with a correlation/trace id field.
- [X] A sample handler returning a failed `Result` maps to the correct ProblemDetails status via the shared helper.

---

### T0.4 Platform: Aspire AppHost

- [X] **Status:** done · **Size:** M

- **Depends on:** T0.3
- **Refs:** E9 [M] (single F5), ADR-014, ADR-003

**Goal:** `F5` on the AppHost starts PostgreSQL (with per-service databases), RabbitMQ and every service, with the dashboard showing traces and logs.

**Scope:**

- `AppHost` project: PG container resource with databases `identity_db`, `portfolio_db`, `marketdata_db`, `strategy_db`, `reporting_db`; RabbitMQ container (management plugin for local debugging).
- Register services as they come to exist (Identity first, rest in T0.13); connection strings/injection via Aspire references.
- Per-service DB users with access only to their own database (ADR-003) — provisioning script or init SQL; document in `deploy/README.md`.

**Acceptance criteria:**

- [X] Single F5/`dotnet run` on AppHost: PG + RabbitMQ + registered services up, dashboard reachable.
- [X] Each service connects with its own DB user; connecting to another service's DB with that user fails (manual check, documented).
- [X] Restarting the AppHost is idempotent (no orphaned containers, volumes persist data locally).

---

### T0.5 Identity: service skeleton

- [X] **Status:** done · **Size:** M

- **Depends on:** T0.3, T0.4
- **Refs:** ADR-002 (slices), ADR-003, ADR-005

**Goal:** an empty but production-shaped Identity service: Minimal API, EF Core + first migration, wired into Aspire and ServiceDefaults.

**Scope:**

- `services/Identity/Skarbiec.Identity` + `Features/` folder (vertical slices; no Service/Repository layers, no MediatR — ADR-002/004; handlers return `Result`/`Result<T>` — ADR-017).
- EF Core 10 + Npgsql, `IdentityDbContext` (ASP.NET Identity schema + `DisplayName`, `BaseCurrency` default PLN on the user), UTC timestamps (`timestamptz`).
- Initial migration; migrations applied at startup in Development only (production: explicit step in deploy — decision recorded in `deploy/README.md`).
- Wire into AppHost + ServiceDefaults.

**Acceptance criteria:**

- [X] Service starts under Aspire; `/health/ready` green (DB reachable).
- [X] Migration creates the schema in `identity_db`; no tables in other databases.
- [X] Solution builds with zero warnings.

---

### T0.6 Identity: user registration

- [X] **Status:** done · **Size:** M

- **Depends on:** T0.5
- **Refs:** E1 [M] (register), ADR-005

**Goal:** `POST /register` creates a user with a properly hashed password and validated input.

**Scope:**

- Slice `Features/Register/`: endpoint + handler + validator (built-in validation, `TypedResults`; handler returns `Result`/`Result<T>` — ADR-017).
- ASP.NET Identity user manager; default Identity hasher; password policy (length, complexity — Identity defaults, documented).
- Duplicate e-mail → 409 ProblemDetails; weak password → 400 ProblemDetails with field errors.
- E-mail confirmation deliberately **out** (backlog E1 [S]).

**Acceptance criteria:**

- [X] Valid request → 201, user row in `identity_db`, password stored hashed (verified in test).
- [X] Weak password / malformed e-mail → 400 ProblemDetails; duplicate → 409.
- [X] Slice integration test on Testcontainers PG (once T0.9 lands, wire it in).

---

### T0.7 Identity: login + JWT issuance

- [X] **Status:** done · **Size:** L

- **Depends on:** T0.6
- **Refs:** E1 [M] (stay logged in), ADR-005

**Goal:** `POST /login` returns a 15-minute access token and sets a rotated 30-day refresh token in an httpOnly cookie.

**Scope:**

- Slice `Features/Login/`: credential check via Identity, JWT with `sub` = UserId, short claims set, 15 min expiry; signing key from configuration (user-secrets locally, `.env` on VPS — never in the repo).
- Refresh token: opaque random value, stored hashed in DB with expiry (30 days) and `ReplacedBy` chain for rotation; delivered as `Secure; HttpOnly; SameSite` cookie.
- Failed login → 401 without revealing which part was wrong; consider Identity lockout defaults.

**Acceptance criteria:**

- [X] Correct credentials → 200 + access token in body + refresh cookie set.
- [X] Access token validates against ServiceDefaults JWT config (round-trip test: call a protected sample endpoint).
- [X] Wrong password → 401 ProblemDetails; token lifetime is 15 min (asserted in test).

---

### T0.8 Identity: refresh + logout

- [X] **Status:** done · **Size:** M

- **Depends on:** T0.7
- **Refs:** E1 [M] (logout invalidates refresh), ADR-005

**Goal:** `POST /refresh` rotates the refresh token and issues a new access token; `POST /logout` revokes the refresh chain.

**Scope:**

- Slice `Features/Refresh/`: validate cookie token against the hashed store, reject expired/revoked/already-rotated (reuse detection → revoke the whole chain), issue new pair.
- Slice `Features/Logout/`: revoke current refresh token + clear cookie.
- Revocation strategy is short-TTL + rotation (ADR-005) — no server-side access-token blacklist.

**Acceptance criteria:**

- [X] Refresh with a valid cookie → new access token, old refresh token no longer usable.
- [X] Reusing a rotated refresh token → 401 and the chain is revoked (test).
- [X] After logout, refresh → 401.

---

### T0.9 Platform: test infrastructure (xUnit + Testcontainers)

- [X] **Status:** done · **Size:** L

- **Depends on:** T0.5
- **Refs:** E8 [M] (slice tests on Testcontainers)

**Goal:** a reusable test foundation every service uses: real PG + RabbitMQ in containers, `WebApplicationFactory` fixture, auth helper for issuing test JWTs.

**Scope:**

- Shared test utilities project (e.g. `Skarbiec.Testing`): PG + RabbitMQ Testcontainers fixtures (collection fixtures for reuse across tests), respawn/cleanup strategy between tests.
- `WebApplicationFactory`-based slice-test base class: overrides connection strings to containers, disables background jobs by default.
- Test-auth helper: mint JWTs for arbitrary `UserId`s with the test signing key — needed for tenancy tests (T0.14).
- Convention: tests live per service (`services/Identity/Skarbiec.Identity.Tests`), pattern documented.

**Acceptance criteria:**

- [X] Identity registration + login tests run green against containerized PG locally and in CI.
- [X] Two tests writing the same entity do not interfere (isolation/cleanup works).
- [X] Base fixture usable from another service's test project without copy-paste.

---

### T0.10 Messaging: MassTransit v8 + EF Outbox, `UserRegistered`

- [X] **Status:** done · **Size:** L

- **Depends on:** T0.2, T0.6, T0.9
- **Refs:** E9 [M] (outbox), ADR-012

**Goal:** the first full messaging flow — registration publishes `UserRegistered` through the MassTransit EF Outbox, visible end-to-end in a trace.

**Scope:**

- MassTransit v8 (pin major version — no v9 upgrade, ADR-012) + RabbitMQ transport, configured in ServiceDefaults or a shared messaging extension; trace-context propagation on (MassTransit default).
- EF Outbox in Identity: outbox tables migration, `AddEntityFrameworkOutbox`, publish `UserRegistered` inside the registration handler's transaction.
- Delivery/queue naming conventions documented (kebab-case endpoints, one queue per consumer).
- Temporary logging consumer (in any skeleton service or a test host) proving delivery — replaced by real consumers later.

**Acceptance criteria:**

- [X] Registering a user inserts the event into the outbox table in the **same transaction** as the user row (asserted in test).
- [X] Event is delivered to RabbitMQ after commit and consumed (integration test on Testcontainers RabbitMQ).
- [X] Aspire dashboard shows one trace spanning HTTP request → outbox publish → consume.

---

### T0.11 Messaging: outbox durability test

- [X] **Status:** done · **Size:** M

- **Depends on:** T0.10
- **Refs:** E9 [M] AC: "killing the process between commit and publish doesn't lose the event"

**Goal:** proof that the outbox survives a crash between DB commit and broker publish.

**Scope:**

- Integration test: commit the transaction with the outbox row, **stop the bus / kill the delivery service before dispatch**, restart delivery, assert the event still reaches the consumer exactly as expected.
- Document in the test *why* it exists (this is the pattern's whole point) — it doubles as learning material.

**Acceptance criteria:**

- [X] Test red when outbox is bypassed (publish directly) — verified once, noted in test comment.
- [X] Test green with outbox: event delivered after "restart", nothing lost, no duplicate user row.

---

### T0.12 Messaging: idempotent consumer template (inbox)

- [X] **Status:** done · **Size:** M

- **Depends on:** T0.10
- **Refs:** E9 [M] (idempotent consumers), ADR-012

**Goal:** a documented, tested pattern for consumers that deduplicate by `MessageId`, ready to copy into every service.

**Scope:**

- MassTransit inbox (`AddEntityFrameworkInbox`/bus outbox with dedup) configured on the sample consumer; retry policy (exponential, capped) + error queue convention.
- Double-delivery test: deliver the same message (same `MessageId`) twice → side effect happens once.
- Short `docs/messaging.md` (or section in the service README): how to add a consumer, the checklist (inbox on, retry, idempotent handler logic).

**Acceptance criteria:**

- [X] Double-delivery test green.
- [X] Poisoned message ends in the error queue after N retries, does not block the queue.
- [X] Pattern write-up committed next to the code.

---

### T0.13 Services: skeletons for Portfolio, MarketData, Strategy, Reporting

- [X] **Status:** done · **Size:** L

- **Depends on:** T0.3, T0.4
- **Refs:** ADR-002/003/006, `02-architecture.md` §services

**Goal:** four service skeletons identical in shape to Identity: health checks, OTel, EF + empty migration, tenancy filter — so later phases only add features.

**Scope:**

- Per service: project under `services/<Name>/`, ServiceDefaults reference, own DbContext + `*_db` database + own DB user, initial (possibly empty) migration, `Features/` folder.
- **Tenancy plumbing** (ADR-006): `IUserOwned { Guid UserId }` marker, EF global query filter on `UserId` from the current-user accessor, save interceptor stamping `UserId` on insert — implemented once in a shared per-service extension (in ServiceDefaults or SharedKernel) and wired in Portfolio/Strategy/Reporting (MarketData's instruments/quotes are global — no filter there; custom instruments revisited in Phase 2).
- All four registered in the Aspire AppHost.

**Acceptance criteria:**

- [X] F5 starts 5 services + gateway deps; all `/health/ready` green.
- [X] Query filter proven by a unit/integration test in at least one service: entity of user A invisible in a context scoped to user B.
- [X] Each service only has access to its own database.

---

### T0.14 Quality: architecture test + tenancy test template

- [X] **Status:** done · **Size:** M

- **Depends on:** T0.9, T0.13/
- **Refs:** ADR-006, E1 [M] (isolation), `03-domain-model.md` §invariants

**Goal:** automated guardrails: every user-owned entity has `UserId` (NetArchTest), and a reusable tenancy isolation test template exists.

**Scope:**

- NetArchTest (or equivalent) test per service: all entities implementing `IUserOwned` (or in the domain namespace, decision documented) expose `UserId`; endpoints never bind `UserId` from the request body (convention check or code review checklist item if not automatable).
- Tenancy test template on the T0.9 fixture: user A creates a resource → user B GET → **404** (not 403 — no existence leak); parameterized to reuse across services in later phases.
- Add both to the definition of done in `README.md` of `zadania/`.

**Acceptance criteria:**

- [X] Architecture test fails when a user-owned entity without `UserId` is introduced (verified with a throwaway entity).
- [X] Tenancy template used by at least the Identity or skeleton-level test and green.

---

### T0.15 Gateway: YARP

- [X] **Status:** done · **Size:** L

- **Depends on:** T0.7, T0.13
- **Refs:** E9 [M] (Angular talks only to Gateway), ADR-013

**Goal:** a single entry point routing `/api/<service>/*` to services, validating JWT signatures, enforcing CORS and rate limits.

**Scope:**

- `gateway/Skarbiec.Gateway`: YARP with prefix routes — `/api/identity/*` → Identity, `/api/portfolio/*` → Portfolio, `/api/marketdata/*` → MarketData, `/api/strategy/*` → Strategy, `/api/reporting/*` → Reporting; path transform strips the prefix.
- JWT validation at the gateway (signature + expiry); token passthrough downstream (ADR-005) — services still authenticate themselves (defense in depth).
- Anonymous allow-list: register/login/refresh routes only.
- CORS for the future Angular origin (config-driven per environment); ASP.NET rate limiter (sane default policy, stricter on auth endpoints).
- Cluster destinations from configuration (Aspire service discovery locally, compose DNS in production — ADR-016: no Consul).

**Acceptance criteria:**

- [X] Request without token to a protected route → **401 at the gateway** (never reaches the service — verified via traces/logs).
- [X] Login through the gateway works end-to-end; the same JWT then authorizes a protected call routed to a skeleton service.
- [X] Burst beyond the rate limit → 429.

---

### T0.16 Spike: Angular Material vs PrimeNG (ADR-010) [S]

- [X] **Status:** done · **Size:** M

- **Depends on:** —
- **Refs:** ADR-010 (pending)

**Goal:** a decision, not code: pick the UI kit for Phase 1 and update ADR-010 to ✅.

**Scope:**

- Time-boxed to 1 day. Criteria: data tables (transactions!), form components, theming effort, signals/zoneless compatibility, bundle size, maintenance signals.
- Throwaway repo/StackBlitz with a table + form in each kit; do **not** commit spike code to the monorepo.
- Write the outcome into `06-adr-decisions.md` (decision, criteria, loser's dealbreaker).

**Acceptance criteria:**

- [X] ADR-010 status ✅ with a one-paragraph rationale.
- [X] Phase 1 task T1.8 can name the chosen kit without further discussion.

---

### T0.17 CI: GitHub Actions with path filters

- [X] **Status:** done · **Size:** L

- **Depends on:** T0.9
- **Refs:** E8 [M] (CI monorepo), roadmap Phase 0

**Goal:** CI builds and tests only the services affected by a change; images are produced per service.

**Scope:**

- Workflow with path filters per service (`services/Identity/**`, `gateway/**`, `contracts/**` → everything, etc.); matrix or per-service jobs.
- Steps: restore → build (warnings as errors) → test (Testcontainers — ensure the runner supports Docker) → docker build + push to a registry (GHCR) tagged with SHA.
- Contracts change triggers all service builds (they all depend on it).
- Keep deploy manual for now (one command, T0.18); CI produces artifacts only.

**Acceptance criteria:**

- [X] A PR touching only Identity runs Identity (+gateway/web only if touched) jobs — verified on a test PR.
- [X] A change in `contracts/` rebuilds all services.
- [X] Testcontainers-based tests pass in CI.

---

### T0.18 [VPS] Deploy: Dockerfiles, compose, VPS provisioning — optional, deferred

- [ ] **Status:** todo · **Size:** L

- **Depends on:** T0.15, T0.17
- **Refs:** ADR-011, ADR-014, E8 [M] (compose deploy with a single command)

**Deferred:** development is local-only (Aspire) for now — this task is not required to close out Phase 0 or to start Phase 1+. Nothing downstream hard-depends on it anymore (see T0.19); pick it up whenever VPS deployment actually starts.

**Goal:** the system runs on a VPS from `deploy/docker-compose.yml`, deployable with one command.

**Scope:**

- Dockerfile per service + gateway (multi-stage, non-root user).
- `deploy/`: compose with PG (one instance, per-service DBs + users — init script), RabbitMQ, all services, gateway; **memory limits per container** (8 GB VPS budget, ADR-011); restart policies; healthcheck-based `depends_on`.
- VPS provisioning (documented step-by-step in `deploy/README.md`): Docker + compose plugin, firewall (only 80/443 + SSH), TLS termination (Caddy or Traefik in front of the gateway), `.env` for secrets (never in repo).
- One-command deploy script (pull images by tag + `docker compose up -d`); rollback = redeploy previous tag (documented).
- Migration execution strategy for production (explicit step in the deploy script).

**Acceptance criteria:**

- [ ] `./deploy.sh <tag>` (or equivalent single command) brings the stack up on the VPS.
- [ ] HTTPS works; only gateway is exposed publicly; RabbitMQ/PG unreachable from the internet.
- [ ] Sum of memory limits fits the VPS with headroom; stack survives a VPS reboot (restart policies).

0.1

---

### T0.19 Phase exit: local trace check (+ optional VPS smoke)

- [X] **Status:** done · **Size:** S

- **Depends on:** T0.15
- **Refs:** roadmap Phase 0 deliverable

**Goal:** the Phase 0 deliverable demonstrably holds locally; the production half stays optional until T0.18 (VPS) is picked up.

**Scope:**

- **Local (required now):** register + login through the Gateway running under Aspire (curl/HTTP file — Angular comes in Phase 1); verify 401 without token, 200 with. One Aspire-dashboard trace spanning Gateway→Identity for the login request; screenshot or note in the phase log.
- Retro note (5 lines max) in this file: what leaked past the 5-week limit into the backlog.
- **VPS / production (optional — deferred, needs T0.18):** repeat the same register + login smoke against the deployed stack over HTTPS.

**Acceptance criteria:**

- [X] Local login through the Gateway works (401 without token, 200 with) — verified under Aspire.
- [X] Trace Gateway→Identity confirmed locally in the Aspire dashboard.
- [X] Leftover scope moved to backlog, not silently dropped.
- [ ] *(optional, deferred)* Production login works over HTTPS through the gateway — once T0.18 lands.

**Evidence (2026-08-03):**

- `GET /api/portfolio/me` via gateway (`https://localhost:60683`): no `Authorization` header → `401`; with the JWT from `POST /api/identity/login` → `200`, body = registered user's id.
- Aspire dashboard trace `128365af07f2e457dd6232e5de7d9af1`, `gateway: POST /api/identity/login/{**catch-all}`, 15:44:06 — spans gateway → identity-service → 2× postgresql (3 resources, 5 spans). Screenshot: `skarbiec-plan/zadania/evidence/t019-trace-login-gateway-identity.png`.

**Retro note:**

- Phase 0 (T0.1–T0.17) ran 2026-07-27 → 2026-07-30, ~4 days elapsed — nowhere near the 5-week limit.
- Only leftover: T0.18 (VPS/production deploy), deferred by deliberate choice (local-only dev for now), not time pressure — already its own tracked task, not dropped.
- No other scope was cut or pushed to `05-backlog.md`.

---

## Exit checklist (Phase 0 done when…)

- [X] All [M] tasks above done (T0.16 is [S] — may slip, but blocks T1.8).
- [X] Login through the Gateway confirmed locally (Aspire); trace Gateway→Identity visible in the dashboard.
- [X] Outbox durability + double-delivery tests green in CI.
- [X] Architecture + tenancy guardrail tests in place.
- [X] `CLAUDE.md` §Commands filled in (build/test/run commands now exist; `deploy` command added once T0.18 lands).
- [ ] *(optional, deferred)* Deployed to VPS; login through Gateway in production — T0.18.
