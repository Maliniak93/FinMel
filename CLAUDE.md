# Skarbiec

Personal wealth-management web app and a deliberate microservices learning project.
Stack: .NET 10 (4 services + YARP gateway) · Angular 22 · PostgreSQL (db per service) · RabbitMQ + MassTransit v8 (outbox/inbox) · .NET Aspire locally.

## Project state: contracts free, data protected (ADR-043)
One user, local-only — contracts, API shapes and enums change in place (never add a `V2` type); delete replaced code, tests and docs instead of deprecating them; propose a rewrite when it beats a patch.
Data in the compose stack is protected: a migration that destroys it needs the user's consent in the ticket (`## Utrata danych`), a move needs `## Przeniesienie danych`; migration history only grows.

## Commands
| Task | Command |
| --- | --- |
| Run everything locally (Aspire: infra, services, dashboard) | `dotnet run --project Skarbiec.AppHost` |
| Build · format check | `dotnet build Skarbiec.slnx` · `dotnet format Skarbiec.slnx --verify-no-changes` |
| Test — **Docker must be running** (Testcontainers) | `dotnet test` |
| One-shot verification scoped to the changed files (`--all` everything, `--fix` reformats first, `--cache` reuses a green run of the same tree; 120-min deadline) | `node scripts/verify.mjs [--quick\|--all\|--projects A,B] [--fix] [--cache] [--cache-check]` |
| Preflight for /build: tools, Docker, stack stopped, web deps | `node scripts/preflight.mjs [--dry-run] [--no-web]` |
| Live plan status: spec issues on the GitHub project vs. git vs. open PRs | `node scripts/plan-status.mjs [--no-gh] [--area a] [--priority P]` |
| Spec issues and cards on the FinMel project | `node scripts/gh-project.mjs get\|list\|set\|heal\|comment\|tick` |
| /idea prompts in gitignored `skarbiec-plan/prompts/` | `node scripts/idea.mjs context\|list\|show\|check\|mark\|path` |
| Local offline code guide (gitignored `skarbiec-plan/przewodnik/`); `/docs` drives both | `node scripts/docs.mjs facts\|build` |
| Local offline workflow map (gitignored `skarbiec-plan/flows/`); `/flows` drives both | `node scripts/flows.mjs facts\|build` |
| $ at list prices per tier, agent type and run; `--sessions` main sessions, `--timeline` agent wall time of the newest run | `node scripts/run-cost.mjs [--since YYYY-MM-DD] [--issue n] [--sessions] [--timeline]` |
| The uncommitted change incl. untracked files, index untouched | `node scripts/review-diff.mjs [--stat] [-- <path>]` |
| The Ship step: lane guard, commit, push, PR, run report; `--fix-pr` for a CI fix | `node scripts/ship.mjs <n> --branch b --title t --json '…' [--blocked\|--fix-pr] [--dry-run]` |
| Failed CI jobs of a PR, read by `/fix ci:#<n>` | `node scripts/ci-digest.mjs <pr> [--lines N]` |
| The owner's unattended queue run: headless `/build` per part, green CI, squash-merge, `test-timings.mjs --from-autobuild --wait 15 --timeout 45` after an epic; `skarbiec-plan/runbooks/autobuild.md` | `node scripts/autobuild.mjs [<n>] [--items N\|all] [--include-manual] [--start now\|MIN\|HH:MM] [--until HH:MM] [--max-tasks N] [--no-shutdown] [--dry-run] \| --stop` |
| The owner's test suite timings → gitignored `skarbiec-plan/test-timings/history.json` | `node scripts/test-timings.mjs [--only dotnet,web] [--check] [--open] [--report-only] [--wait <min>] [--timeout <min>] [--from-autobuild]` |
| Delete local branches whose PR is merged (preview unless `--apply`) | `node scripts/prune-branches.mjs [--apply]` |
| Delete generated local files past their age (preview unless `--apply`) | `node scripts/clean.mjs [--apply]` |
| The owner's compose stack, built from a commit — agents never touch it | `node scripts/compose.mjs build|up|down|reset|backup|backups|restore|status` |
| Browser e2e on the isolated `skarbiec-e2e` project, the only stack an agent may start and stop → `.git/e2e-result.json` (exit 0 green, 1 failed, 3 environment) | `node scripts/e2e.mjs run [--headed|--ui] [--grep p] [--keep] [--detach|--await]|up|down|status|report` |
| Frontend dev server · unit tests (Vitest) | `cd web && npm start` · `cd web && npm test` |
| Regenerate the TS client after an API change | `cd web && npm run gen:api` |
| Add a migration, then always `dotnet format` | `dotnet ef migrations add <Name> --project services/<S>/Skarbiec.<S>` |

## Repo layout
`Skarbiec.AppHost/` Aspire orchestration (local entry point) · `Skarbiec.ServiceDefaults/` OTel, health, JWT, resilience, Result→ProblemDetails, messaging, tenancy · `Skarbiec.Testing/` test fixtures, tenancy template · `services/` Identity, Portfolio, MarketData, Reporting (slices in `Features/<Name>/`) · `gateway/` YARP
`contracts/` Skarbiec.Contracts (events/DTOs edited in place, Result, Money, enums) · `web/` Angular 22 · `scripts/`, `requests/` (`.http` per service) · `skarbiec-plan/` planning docs (`archive/` = history)

## Hard architecture rules
1. 4 services + gateway: Identity, Portfolio, MarketData, Reporting. No new service without an ADR (ADR-001, ADR-020).
2. Vertical slices: a feature is a folder with endpoint + handler + validator. No Service/Repository layers, no MediatR (ADR-002, ADR-004).
3. Handlers return `Result`/`Result<T>`; the endpoint maps a failure to ProblemDetails. Never throw for an expected failure (ADR-017).
4. Database per service. Reference other services by id only — no FKs, no cross-DB queries (ADR-003).
5. Domain facts are events carrying full state, published only through the MassTransit EF outbox; consumers are idempotent (inbox) and never call the publisher back (ADR-012, ADR-021).
6. REST between services only for request-path lookups (Portfolio→MarketData instrument lookup, and the transaction-date FX rate lookup) and the daily Reporting→MarketData price/FX batch and its history twin for rebuilds (Reporting also reads the bond-series rates batch for bond valuation, ADR-047) — all on `/internal` endpoints (`MapInternalGroup`: anonymous, not in OpenAPI, unreachable through the Gateway, global data only), called with no token. No gRPC (ADR-021, ADR-022, ADR-026, ADR-027, ADR-032).
7. External price APIs are called only from MarketData jobs; the exceptions are ticker verification through `ITickerVerifier` and the FX lookup read-through for dates older than coverage (ADR-007, ADR-018, ADR-045).
8. Every user-owned entity carries `UserId` from JWT claims — never from the request. EF global query filter; tenancy isolation tests are part of DoD (ADR-006).
9. Money is `decimal`/`Money`, base currency PLN (ADR-008).
10. Transactions are the source of truth for asset quantity (ADR-009).
11. Angular talks only to the Gateway, through the generated client (ADR-013).

## Conventions
Path-scoped rules in `.claude/rules/` load when you touch matching files — read the matching rule before creating files in an area you have not touched yet. .NET 10 and Angular 22 move faster than training data: verify an API through microsoft-docs (.NET/EF) or context7 (Angular, MassTransit), not from memory.
Comments: none by default — names carry the meaning. Allowed: a one-line `//` for a non-obvious why (workaround, framework quirk, a rule deliberately bypassed) and a one-line `/// <summary>` on a request/response record, event or contract DTO (or its property) whose name does not say everything (unit, sign, currency). Never: restating code, XML docs on classes/methods/tests, `<remarks>`/`<param>`/`<see cref>`, references to ACs, tasks, specs, issues or ADRs, history ("previously…"), banners, multi-line blocks. Directives (`#!`, `// @ts-check`, `/// <reference>`, `# @name`, marker comments read by scripts) stay; a script's usage header stays at one line per flag.

## Workflow
Specs are GitHub issues on the **FinMel project** (fields and labels: `skarbiec-plan/workflow.md` → The board), no spec files in the repo. `/idea` shapes a prompt; `/design <idea>` interviews and publishes the issue (a split becomes an `epic`) → `/build #<n>`: prepare cuts the branch → test-writer → implementer (verifies itself, escalates once) → reviewer → `ship.mjs` commits, pushes, opens the PR and comments the run report (run `/build` in a fresh session or `claude --model haiku "/build #<n>"`): **you merge**. The ticket's Area picks the build profile: its phases, the playbooks agents read first and the reviewer's `review-checklist` sections. `skip-tests` skips the test phase, `/build --skip tests,review` overrides it per run; re-running `/build #<n>` replays an interrupted run (`--fresh` starts over). Every epic with an app-visible change ends with an `e2e` part. `/fix <bug>` publishes its issue at P0 and builds nothing; `/board`, `/prio #<n> P<k>`, `/check`, `/ops <task>`.
Tier 1 = a precedent for this exists in the same service; Tier 2 = a new pattern, cross-service work, or an algorithm. The implementer gets 3 verify fixes per context and escalates once; a failed escalation fails the run. Model and effort per role and tier: `skarbiec-plan/workflow.md` → Tier rule.
Definition of done: tests green including tenancy, zero warnings, `dotnet format` clean, TS client regenerated when the API changed, `requests/*.http` updated, rules and ADRs updated when a convention changes. An e2e part: the whole e2e suite green and no production file changed.
No agent runs a git mutation: in a build run `gh-project.mjs prepare` cuts the branch and `scripts/ship.mjs` commits, pushes and opens the PR; otherwise only `ops` in an explicit `/ops` chore. Only the owner and `scripts/autobuild.mjs` merge.
Verify scope comes from the changed files against master (a full-scope file such as `contracts/` runs everything; the map is in `skarbiec-plan/workflow.md` → Definition of Done). The implementer runs `verify.mjs --fix --cache` on that scope (`--all` on an epic's last non-e2e part); `ship.mjs` gates on the same scope with `--cache-check`. Otherwise agents run only `--filter`ed tests (Bash `timeout: 600000`), never a full suite, `dotnet build`, `dotnet format` or the npm checks.

## Documentation (read on demand)
`skarbiec-plan/` (`README.md` lists it; check `decisions.md` before any architectural change) · the READMEs of `Skarbiec.Testing/`, `Skarbiec.ServiceDefaults/Messaging/`, `web/`, `deploy/` · `contracts/Skarbiec.Contracts/CONTRACTS.md`

## Gotchas
- A running local stack (AppHost, services, `ng serve`) locks build outputs and `node_modules` binaries (MSB3021/3026/3027, EBUSY/EPERM, port in use): run `node scripts/stop-stack.mjs` and retry; `verify.mjs` does it itself. Never restart the stack afterwards.
- e2e (`.claude/rules/e2e.md`, ADR-034): `verify.mjs` never runs Playwright; `stop-stack.mjs` never touches the e2e containers.
- `dotnet test` needs Docker; test hosts set `Testing:DisableBackgroundJobs` (Quartz off, triggers NoOp). The Gateway rate limit is 100 requests / 10 s, and seed and smoke scripts trip it.
- Aspire and e2e databases are disposable: after a destructive migration drop them (`skarbiec-plan/runbooks/local-dev.md`); the compose stack is not (ADR-043). The Angular CLI needs Node ≥ 22.22.3 / 24.15 / 26.

## Response style
Be terse: no narration of steps, no restating file contents, no closing summaries unless asked. One short clarifying question beats guessing. Every file is written in English; talk to the user in Polish.
