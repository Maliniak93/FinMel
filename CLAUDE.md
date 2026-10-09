# Skarbiec

Personal wealth-management web app and a deliberate microservices learning project.
Stack: .NET 10 (4 services + YARP gateway) · Angular 22 · PostgreSQL (db per service) · RabbitMQ + MassTransit v8 (outbox/inbox) · .NET Aspire locally.

## Project state: greenfield (ADR-019)
One user, no real data, local-only — so: edit contracts in place (never add a `V2` type); write destructive migrations with no backfill (squashing a service to one `InitialCreate` is allowed); delete replaced code, tests and docs instead of deprecating them; propose a rewrite when it beats a patch.
Greenfield changes none of: tenancy isolation, outbox + idempotent consumers, the Result pattern, the definition of done.

## Commands
| Task | Command |
| --- | --- |
| Run everything locally (Aspire: Postgres + RabbitMQ + services + dashboard) | `dotnet run --project Skarbiec.AppHost` |
| Build | `dotnet build Skarbiec.slnx` |
| Test — **Docker must be running** (Testcontainers) | `dotnet test` |
| Format check | `dotnet format Skarbiec.slnx --verify-no-changes` |
| One-shot verification: format → build → tests → web → API client; `--fix` first reformats the changed files; every failing project is reported; 120-min deadline | `node scripts/verify.mjs [--quick\|--all\|--projects A,B] [--fix] [--cache-check]` |
| Preflight for /build: checks node/dotnet/gh/git, starts Docker, stops the stack, installs web deps | `node scripts/preflight.mjs [--dry-run] [--no-web]` |
| Live plan status (spec issues on the GitHub project vs. git vs. open PRs); `--write` refreshes the block in `skarbiec-plan/README.md` | `node scripts/plan-status.mjs [--write] [--no-gh]` |
| Spec issues on the FinMel project: read, list, move a card, comment, tick ACs | `node scripts/gh-project.mjs get\|list\|set\|comment\|tick` |
| $ at list prices per agent/run; `--sessions` = main sessions per command + Explore; `--timeline` = wall time per agent of the newest run | `node scripts/run-cost.mjs [--since YYYY-MM-DD] [--issue n] [--sessions] [--timeline]` |
| The uncommitted change incl. untracked files, index untouched | `node scripts/review-diff.mjs [--stat] [-- <path>]` |
| The Ship step: guard lane, commit, push, PR, run report | `node scripts/ship.mjs <n> --branch b --title t --json '…' [--blocked] [--dry-run]` |
| Delete local branches whose PR is merged (dry run unless `--apply`) | `node scripts/prune-branches.mjs [--apply]` |
| Local docker compose stack (built from a commit, rebuilt on ship; started/stopped only by the user) | `node scripts/compose.mjs build|up|down|reset|status` |
| Frontend dev server | `cd web && npm start` |
| Frontend unit tests (Vitest) | `cd web && npm test` |
| Regenerate the TS client after an API change — reads the build-time OpenAPI files once spec-00 lands, until then needs the stack running | `cd web && npm run gen:api` |
| Add a migration, then always `dotnet format` | `dotnet ef migrations add <Name> --project services/<S>/Skarbiec.<S>` |

## Repo layout
```
Skarbiec.AppHost/         # Aspire orchestration — local entry point
Skarbiec.ServiceDefaults/ # OTel, health, JWT, resilience, Result→ProblemDetails, messaging, tenancy
Skarbiec.Testing/         # containers fixture, api factory, tenancy template
services/                 # Identity, Portfolio, MarketData, Reporting (Strategy removed by spec-01) — slices in Features/<Name>/
gateway/                  # YARP
contracts/                # Skarbiec.Contracts — events/DTOs edited in place, Result, Money, enums
web/                      # Angular 22 frontend
scripts/  requests/       # verify.mjs · .http files per service
skarbiec-plan/            # planning docs: product, architecture, domain, decisions, workflow, ideas, runbooks/ (archive/ = history)
```

## Hard architecture rules
1. 4 services + gateway: Identity, Portfolio, MarketData, Reporting. No new service without an ADR (ADR-001, ADR-020).
2. Vertical slices: a feature is a folder with endpoint + handler + validator. No Service/Repository layers, no MediatR (ADR-002, ADR-004).
3. Handlers return `Result`/`Result<T>`; the endpoint maps a failure to ProblemDetails. Never throw for an expected failure (ADR-017).
4. Database per service. Reference other services by id only — no FKs, no cross-DB queries (ADR-003).
5. Domain facts are events carrying full state, published only through the MassTransit EF outbox; consumers are idempotent (inbox) and never call the publisher back (ADR-012, ADR-021).
6. REST between services only for request-path lookups (Portfolio→MarketData instrument lookup, and the transaction-date FX rate lookup) and the daily Reporting→MarketData price/FX batch and its history twin for rebuilds — all on `/internal` endpoints (`MapInternalGroup`: anonymous, not in OpenAPI, unreachable through the Gateway, global data only), called with no token. No gRPC (ADR-021, ADR-022, ADR-026, ADR-027, ADR-032).
7. External price APIs are called only from MarketData jobs; the single exception is ticker verification through `ITickerVerifier` (ADR-007, ADR-018).
8. Every user-owned entity carries `UserId` from JWT claims — never from the request. EF global query filter; tenancy isolation tests are part of DoD (ADR-006).
9. Money is `decimal`/`Money`, base currency PLN (ADR-008).
10. Transactions are the source of truth for asset quantity (ADR-009).
11. Angular talks only to the Gateway, through the generated client (ADR-013).

## Conventions
Path-scoped rules in `.claude/rules/` (`dotnet.md`, `messaging.md`, `testing.md`, `angular.md`, `domain.md`) load automatically when you touch matching files — read the matching rule before creating files in an area you have not touched yet. .NET 10 and Angular 22 move faster than training data: verify an API through microsoft-docs (.NET/ASP.NET/EF) or context7 (Angular, MassTransit) instead of writing it from memory.
Comments: none by default — names carry the meaning. Allowed: a one-line `//` for a non-obvious why (workaround, framework quirk, a rule deliberately bypassed) and a one-line `/// <summary>` on a request/response record, event or contract DTO (or its property) whose name does not say everything (unit, sign, currency). Never: restating code, XML docs on classes/methods/tests, `<remarks>`/`<param>`/`<see cref>`, references to ACs, tasks, specs, issues or ADRs, history ("previously…"), banners, multi-line blocks. Directives (`#!`, `// @ts-check`, `/// <reference>`, `# @name`, marker comments read by scripts) stay; a script's usage header stays at one line per flag.

## Workflow
Specs are GitHub issues on the **FinMel project** (Status Todo → In progress → Done; custom fields Tier, Kind, Branch; labels `spec`, `epic`, `skip-tests`) — there are no spec files in the repo. `/design <idea>` interviews, drafts in the scratchpad and, after your approval, publishes the issue (new behaviour, a change to existing behaviour, or a cleanup; a split becomes an `epic` with sub-issues) → `/build #<n>` moves the card to In progress, prepare cuts the branch → test-writer → implementer (runs the full verify itself, escalates once) → reviewer → `ship.mjs` commits, pushes, opens the PR and comments the run report with the PR link on the issue (run `/build` in a fresh session or `claude --model haiku "/build #<n>"`): **you merge** (the branch is the issue's linked branch, so merging closes the issue and moves the card to Done). The `skip-tests` label (no behaviour changes) skips the test phase; `/build --skip tests,review` overrides it for one run. `/fix <bug>` reproduces, publishes a one-criterion `fix/<slug>` issue (or an epic of them) and runs the same pipeline; either command can split work into an `epic` whose parts build in order, one PR each; `/board` shows what to build next; `/check` runs verification; `/ops <task>` handles CI and GitHub chores.
Tier 1 = a precedent for this exists in the same service (test-writer haiku/high, implementer sonnet/medium → opus/medium; a skip-tests cleanup starts on haiku/high). Tier 2 = new pattern, cross-service work, or an algorithm (test-writer sonnet/medium, implementer sonnet/high → opus/high). The implementer gets 3 verify fixes per context and escalates once; a failed escalation fails the run. Reviewer opus/medium on Tier 1, opus/high on Tier 2; re-reviews opus/medium.
Definition of done: tests green including tenancy, zero warnings, `dotnet format` clean, TS client regenerated when the API changed, `requests/*.http` updated, rules and ADRs updated when a convention changes.
No agent runs a git mutation: in a build run `gh-project.mjs prepare` cuts the branch and `scripts/ship.mjs` commits, pushes and opens the PR; otherwise only `ops` in an explicit `/ops` chore. No agent merges.
`scripts/verify.mjs --all --fix --cache` runs only in the implementer, in full before it returns, and `ship.mjs` checks its cache (`--cache-check`) before committing — otherwise agents run only `--filter`ed tests (Bash `timeout: 600000`), never a full suite, `dotnet build`, `dotnet format` or the npm checks.

## Documentation (read on demand)
| File | Content |
| --- | --- |
| `skarbiec-plan/architecture.md` | services, communication, flows |
| `skarbiec-plan/domain.md` | data model per service, valuation |
| `skarbiec-plan/decisions.md` | ADRs — check before any architectural change |
| `skarbiec-plan/workflow.md` · `ideas.md` · `runbooks/` | agents, DoR/DoD, git conventions · feature pool · local-dev, ci, troubleshooting |
| in repo | `Skarbiec.Testing/README.md`, `Skarbiec.ServiceDefaults/Messaging/README.md`, `web/README.md`, `deploy/README.md`, `contracts/Skarbiec.Contracts/CONTRACTS.md` |

## Gotchas
- A running local stack (AppHost, services, `ng serve`) locks build outputs and `node_modules` binaries — any agent blocked by it (MSB3021/3026/3027, EBUSY/EPERM, port in use) runs `node scripts/stop-stack.mjs` and retries; `verify.mjs` does this itself. Never restart the stack afterwards.
- `dotnet test` needs Docker running; test hosts set `Testing:DisableBackgroundJobs`, so Quartz jobs are off and triggers are NoOp.
- After a destructive migration, drop the local databases — `skarbiec-plan/runbooks/local-dev.md`.
- The Gateway rate limit is 100 requests / 10 s; seed and smoke scripts trip it.
- The Angular CLI needs Node ≥ 22.22.3 / 24.15 / 26.
- `npm run gen:api` occasionally drops `.js` import extensions across the whole client — diff it and keep only the real schema delta.

## Response style
Be terse: no narration of steps, no restating file contents, no closing summaries unless asked. One short clarifying question beats guessing. Every file is written in English; talk to the user in Polish.
