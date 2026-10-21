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
| Build · format check | `dotnet build Skarbiec.slnx` · `dotnet format Skarbiec.slnx --verify-no-changes` |
| Test — **Docker must be running** (Testcontainers) | `dotnet test` |
| One-shot verification, scoped to the changed files (`--all`: everything; `--fix` reformats the changed files first; `--cache` answers a tree already proven green, a green wider run counts; every failing project reported; 120-min deadline) | `node scripts/verify.mjs [--quick\|--all\|--projects A,B] [--fix] [--cache] [--cache-check]` |
| Preflight for /build: checks node/dotnet/gh/git, starts Docker, stops the stack, installs web deps | `node scripts/preflight.mjs [--dry-run] [--no-web]` |
| Live plan status (spec issues on the GitHub project vs. git vs. open PRs); `--write` refreshes the block in `skarbiec-plan/README.md` | `node scripts/plan-status.mjs [--write] [--no-gh]` |
| Spec issues on the FinMel project: read, list, move a card, comment, tick ACs, heal a missing Branch | `node scripts/gh-project.mjs get\|list\|set\|heal\|comment\|tick` |
| /idea prompts (local, gitignored `skarbiec-plan/prompts/`): context digest, list, show, validate + copy, set status, file path | `node scripts/idea.mjs context\|list\|show\|check\|mark\|path` |
| Local offline code guide (gitignored `skarbiec-plan/przewodnik/`): extract facts, build from Claude's prose; `/docs` drives both | `node scripts/docs.mjs facts\|build` |
| Local offline workflow map (gitignored `skarbiec-plan/flows/`): extract commands, workflows, agents, scripts, build from Claude's step descriptions; `/flows` drives both | `node scripts/flows.mjs facts\|build` |
| $ at list prices per agent/run; `--sessions` = main sessions per command + Explore; `--timeline` = wall time per agent of the newest run | `node scripts/run-cost.mjs [--since YYYY-MM-DD] [--issue n] [--sessions] [--timeline]` |
| The uncommitted change incl. untracked files, index untouched | `node scripts/review-diff.mjs [--stat] [-- <path>]` |
| The Ship step: guard lane, commit, push, PR, run report; `--fix-pr` pushes a CI fix to the open PR and comments it | `node scripts/ship.mjs <n> --branch b --title t --json '…' [--blocked\|--fix-pr] [--dry-run]` |
| Failed CI jobs of a PR: failed step, log tail, failed test names (read by `/fix ci:#<n>`) | `node scripts/ci-digest.mjs <pr> [--lines N]` |
| Build the queue unattended, item by item (an epic or a standalone issue; `<n>` limits it to one): each part via a fresh `claude -p "/build #n"`, marks it `autobuild`, waits for green CI (a red one reruns failed jobs once, then gets one `/fix ci:#n` session on the PR branch), squash-merges, syncs master; resumes from GitHub state; stops when the usage limit runs out; keeps the PC awake from launch (guard confirmed and re-armed after every wake; slept time is logged and not counted towards the 4 h limit; a build with no progress for 45 awake minutes is killed and replayed once), `--start` postpones the work, in a terminal a start wizard asks every setting no flag answered and confirms once, `--until HH:MM` starts no new build after that time; sleeps or hibernates through the Claude usage limit (`--on-limit`, `--max-wait`) and asks with a 10-min Tak/Nie popup before every sleep and the end-of-run shutdown; one stop file; readable log in `skarbiec-plan/autobuild-runs/` | `node scripts/autobuild.mjs [<n>] [--items N\|all] [--include-manual] [--start now\|MIN\|HH:MM] [--until HH:MM] [--max-tasks N] [--no-shutdown] [--dry-run] \| --stop` |
| Measure test suite times (each .NET test project, web, e2e) on a quiet machine, keep the previous run beside the new one in gitignored `skarbiec-plan/test-timings/last-run.json`, print the deltas and overwrite the self-contained `report.html` beside it; `--check` only tests that the machine is quiet (exit 3 busy); `--open` opens the report; `--report-only` renders it from `last-run.json` without measuring | `node scripts/test-timings.mjs [--only dotnet,web,e2e] [--check] [--open] [--report-only]` |
| Delete local branches whose PR is merged (dry run unless `--apply`) | `node scripts/prune-branches.mjs [--apply]` |
| Local docker compose stack (built from a commit, rebuilt on ship; started/stopped only by the user — agents never touch it, the one exception is the isolated e2e project below) | `node scripts/compose.mjs build|up|down|reset|status` |
| Browser e2e on an isolated `skarbiec-e2e` compose project (images from the working tree, market data from a stub, tmpfs data; agents may start and stop this project only) → `.git/e2e-result.json`; exit 0 green, 1 a test failed, 3 environment | `node scripts/e2e.mjs run [--headed|--ui] [--grep p] [--keep] [--detach|--await]|up|down|status|report` |
| Frontend dev server · unit tests (Vitest) | `cd web && npm start` · `cd web && npm test` |
| Regenerate the TS client after an API change — reads the build-time OpenAPI files once spec-00 lands, until then needs the stack running | `cd web && npm run gen:api` |
| Add a migration, then always `dotnet format` | `dotnet ef migrations add <Name> --project services/<S>/Skarbiec.<S>` |

## Repo layout
`Skarbiec.AppHost/` Aspire orchestration (local entry point) · `Skarbiec.ServiceDefaults/` OTel, health, JWT, resilience, Result→ProblemDetails, messaging, tenancy · `Skarbiec.Testing/` containers fixture, api factory, tenancy template · `services/` Identity, Portfolio, MarketData, Reporting (slices in `Features/<Name>/`) · `gateway/` YARP
`contracts/` Skarbiec.Contracts (events/DTOs edited in place, Result, Money, enums) · `web/` Angular 22 · `scripts/`, `requests/` (`.http` per service) · `skarbiec-plan/` planning docs (`archive/` = history)

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
Specs are GitHub issues on the **FinMel project** (Status Todo → In progress → Done; fields Tier, Kind, Area, Priority, Branch; labels `spec`, `epic`, `skip-tests`, `e2e`), no spec files in the repo. `/idea` shapes a prompt; `/design <idea>` interviews and publishes the issue (a split becomes an `epic`) → `/build #<n>`: prepare cuts the branch → test-writer → implementer (verifies itself, escalates once) → reviewer → `ship.mjs` commits, pushes, opens the PR and comments the run report (run `/build` in a fresh session or `claude --model haiku "/build #<n>"`): **you merge**; the one exception is `scripts/autobuild.mjs`, launched from a terminal, which merges each epic part after green CI. The ticket's Area picks the build profile (`profileOf` in `scripts/areas.mjs`, passed by `prepare`; the report prints `Profil:`): Backend, Frontend, Fullstack run test-writer → implementer → reviewer, Scripts, Claude, Docs, CI/Infra, E2E run implementer → reviewer; agents read the profile's playbooks first (`Read first:`, no preloaded skills) and the reviewer applies its `review-checklist` sections; no Area = the full path. `skip-tests` skips the test phase; `/build --skip tests,review` overrides it per run; re-running `/build #<n>` replays an interrupted run (`--fresh` starts over). Every epic with an app-visible change ends with an `e2e` part: its `/build` runs the e2e agent (Playwright tests from the epic's `## Test manualny`, the whole suite, or a block on a stable app failure, `/fix`ed then resumed). `/fix <bug>` runs the same pipeline; `/board`, `/prio #<n> P<k>`, `/check`, `/ops <task>`.
Tier 1 = a precedent for this exists in the same service (test-writer haiku/high, implementer sonnet/medium → opus/medium; a skip-tests cleanup starts on haiku/high). Tier 2 = new pattern, cross-service work, or an algorithm (test-writer sonnet/medium, implementer sonnet/high → opus/high). The implementer gets 3 verify fixes per context and escalates once; a failed escalation fails the run. Reviewer opus/medium on Tier 1, opus/high on Tier 2; re-reviews opus/medium.
Definition of done: tests green including tenancy, zero warnings, `dotnet format` clean, TS client regenerated when the API changed, `requests/*.http` updated, rules and ADRs updated when a convention changes. An e2e part: the whole e2e suite green and no production file changed.
No agent runs a git mutation: in a build run `gh-project.mjs prepare` cuts the branch and `scripts/ship.mjs` commits, pushes and opens the PR; otherwise only `ops` in an explicit `/ops` chore. No agent merges; the only exception is `scripts/autobuild.mjs`, which the user launches from a terminal and which merges each part after green CI.
Verify scope comes from the changed files against master: a full-scope file (`contracts/`, `Skarbiec.ServiceDefaults/`, `Skarbiec.Testing/`, `Directory.*.props`, `Skarbiec.slnx`, `.editorconfig`, `ci.yml`) runs everything; `services/<S>/` that service; `web/`, `e2e/`, an API-surface file the client; `scripts-test` always. The implementer runs `verify.mjs --fix --cache` on that scope, `--all --fix --cache` only on an epic's last non-e2e part (`verifyAll`); `ship.mjs` gates on the same scope (`--cache-check`). A green wider entry on the same tree and HEAD satisfies a narrower check. `--all` is also run by hand (`/check --all`) and once on master after an `autobuild.mjs` run with a merge. The run report's `**Poza obszarem:**` line lists changed files outside the ticket's Area (reviewer: `minor`, blocking only when unrelated to the spec). Otherwise agents run only `--filter`ed tests (Bash `timeout: 600000`), never a full suite, `dotnet build`, `dotnet format` or the npm checks.

## Documentation (read on demand)
`skarbiec-plan/architecture.md` (services, flows) · `domain.md` (data model, valuation) · `decisions.md` (ADRs — check before any architectural change) · `workflow.md` (agents, DoR/DoD, git) · `ideas.md` · `runbooks/` · in repo: `Skarbiec.Testing/README.md`, `Skarbiec.ServiceDefaults/Messaging/README.md`, `web/README.md`, `deploy/README.md`, `contracts/Skarbiec.Contracts/CONTRACTS.md`

## Gotchas
- A running local stack (AppHost, services, `ng serve`) locks build outputs and `node_modules` binaries (MSB3021/3026/3027, EBUSY/EPERM, port in use): run `node scripts/stop-stack.mjs` and retry; `verify.mjs` does it itself. Never restart the stack afterwards.
- e2e (`.claude/rules/e2e.md`, ADR-034): web `:18080`, market stub `:18081`, dashboard `:18898`; `verify.mjs` only typechecks and formats `e2e/`, never runs Playwright; `stop-stack.mjs` never touches the e2e containers.
- `dotnet test` needs Docker; test hosts set `Testing:DisableBackgroundJobs` (Quartz off, triggers NoOp). The Gateway rate limit is 100 requests / 10 s, and seed and smoke scripts trip it.
- After a destructive migration, drop the local databases (`skarbiec-plan/runbooks/local-dev.md`). The Angular CLI needs Node ≥ 22.22.3 / 24.15 / 26. `npm run gen:api` can drop `.js` import extensions repo-wide: diff it and keep only the real schema delta.

## Response style
Be terse: no narration of steps, no restating file contents, no closing summaries unless asked. One short clarifying question beats guessing. Every file is written in English; talk to the user in Polish.
