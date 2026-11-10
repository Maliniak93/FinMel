# Skarbiec — planning documentation

Personal wealth-management web app and a deliberate microservices learning project (.NET 10 + Angular 22 + PostgreSQL + RabbitMQ). This folder is tracked in the repo and is the single current source of truth for product, architecture, decisions and workflow — superseded material lives in `archive/`, which agents don't read.

## How we work

Specs are GitHub issues on the FinMel project, not files (see `workflow.md` → The board).

1. **`/idea <loose idea>`** (optional) — interviews and challenges a loose idea or bug report and saves a ready prompt that `/design idea:<slug>` or `/fix idea:<slug>` reads.
2. **`/design <idea>`** — interview, drafts the spec and, after your approval, publishes it as an issue at Status Todo.
3. **`/build #<issue> [--tier 1|2]`** — moves the card to In progress, runs Tests → Implement → Verify → Review → Ship: commits the change on the issue's branch, pushes and opens the PR, then comments the run report with the PR link on the issue. **The merge is yours** — no agent merges.
4. **`/fix <bug>`** — reproduces the bug, finds the root cause and, after your approval, publishes a fix issue at P0 whose acceptance criterion is the reproduction test; `scripts/autobuild.mjs` builds it.
5. **`/board`** — what is in progress, what to build next, cards that disagree with git.
6. **`/prio #<n> P<k>`** — sets an item's priority and moves its card into that band of the queue.
7. **`/check`** — quick repo-wide verification (`node scripts/verify.mjs`) with a five-line summary.
8. **`/ops <task>`** — CI, dependabot, runner, branch cleanup.

Full detail, agent roles and the pipeline diagram: `workflow.md`. Live status: the `SessionStart` hook and `/board`.

## Documents

| File | Content |
|---|---|
| `product.md` | vision, problem, users, features (delivered / next / later), MVP success criteria, out of scope |
| `architecture.md` | services, communication rules, events, current-vs-target, diagrams (C4, container, 4 sequences) |
| `domain.md` | per-service data model, one ER diagram per service, valuation algorithm, invariants, simplifications |
| `decisions.md` | architecture decision records (ADRs) — check before any architectural change |
| `workflow.md` | agent roles, the `/design` → `/build` pipeline, Definition of Ready/Done, tiers, budgets, git conventions |
| `ideas.md` | future features and platform work, one line each, grouped and tiered |
| `runbooks/local-dev.md` | running the stack, seeds, database resets, manual sync, `.http` files |
| `runbooks/ci.md` | self-hosted runner, path-filtered jobs, dependabot rules |
| `runbooks/troubleshooting.md` | known failure modes and their fixes |
| `runbooks/autobuild.md` | the unattended queue run (`scripts/autobuild.mjs`): plan, window, resume, CI rerun and fix, e2e repair, limits, power, logs |
| `runbooks/e2e-part.md` | the e2e part of an epic: templates, `prepare`, the e2e agent, self-check, review rules, ship gate, block |
| `runbooks/build-replay.md` | how a re-run `/build` replays the finished steps of an interrupted run |
| `runbooks/model-tuning.md` | model and effort principles, where each value lives, the cost baseline and the open experiments |
| `archive/` | superseded planning docs — history only, not read by agents |
