# Model tuning

How the model and effort of every Claude role are chosen, measured and changed. This file is the experiment ledger: it is
the one place outside `LEVELS` that may name a model with an effort.

## Principles

- The model sets capability, effort sets thoroughness. When an agent skipped files or tests, raise its effort; when it
  had the full context and was still wrong, raise its model.
- To get less thinking, lower the effort instead of adding prose to the prompt.
- Judge cost per completed run, not per token: a cheap run that blocks or escalates is not cheap.
- Change one thing at a time, with a metric, a baseline and a rule for taking it back.

## Where each value lives

| Role | Home |
|---|---|
| test-writer, implementer, `skip-tests` implementer, e2e agent, escalation, reviewer, re-review | the `LEVELS` object in `.claude/workflows/build-feature.js`, per tier |
| `ops`, `Explore`, `idea-critic` | the agent's frontmatter (`.claude/agents/*.md`) |
| `/idea`, `/design`, `/fix`, `/build` | the skill's frontmatter (`.claude/skills/<name>/SKILL.md`) |
| the session default | `~/.claude/settings.json` (`model`, `effortLevel`, `modelSettings`) |

A skill whose `model` differs from the session model forces an uncached read of the whole history; a change of effort
alone keeps the cache. `scripts/claude-setup.test.mjs` fails when a second pinned model id or a model/effort pair
appears outside `LEVELS`.

## How to measure

- `node scripts/run-cost.mjs --since <date>` — build runs from the local Claude Code history, priced at list prices.
  - **By tier:** `runs`; `ready` (the workflow returned ready); `blocked` and `blocked at` (the stage that stopped the run:
    `input`, `tests`, `implement`, `review`, `e2e`); `escalated` (runs with at least one `(escalated)` step, counted
    by label even when the escalated agent made no model call); `re-reviewed` (runs with at least one `re-review` step);
    `$/run` and `min/run` averages.
  - **By agent type:** one row per agent type and model: `agents`, `calls/agent`, cache read and its `share`,
    `cache write` and `write/agent` (tokens), `max ctx`, `bash explore` (share of exploring Bash calls), `$` and `$/agent`.
  - `--json` gives the same data with every run, including `deviations` per run.
- `node scripts/run-cost.mjs --sessions --since <date>` — main sessions by first slash command and non-workflow subagents.
- `skarbiec-plan/autobuild-runs/*.log` — relay failures: `the build session did not ship its ready run` (the
  `--from-run` rescue in `autobuild.mjs`) and `the build did not start`.
- `scripts/clean.mjs` deletes Claude session history after 30 days: record a baseline when an experiment starts.

## Baseline (2026-11-10)

`node scripts/run-cost.mjs --since 2026-10-10`, after the Sonnet 5.5 cache-read price fix:

    build-feature runs since 2026-10-10: 284 (247 ready, 36 blocked)
    cache read 1800.7M · cache write 69.8M · output 9792k · cache read per run 6.3M · cost $593.7 · $2.1 per run
    
    By tier
    tier  runs  ready  blocked  blocked at              escalated  re-reviewed  $/run  min/run
    ----  ----  -----  -------  ----------------------  ---------  -----------  -----  -------
    ?     6     0      6        input 6                 0          0            $0.0   0      
    1     120   109    11       e2e 8, implement 3      4          12           $1.4   41     
    2     158   138    19       implement 12, review 7  12         38           $2.7   31     
    
    By agent type
    agent        model       agents  calls/agent  cache read  share  cache write  write/agent  max ctx  bash explore  $       $/agent
    -----------  ----------  ------  -----------  ----------  -----  -----------  -----------  -------  ------------  ------  -------
    implementer  sonnet-5-5  257     30           1024.0M     57%    26.9M        105k         769k     42%           $247.2  $1.0   
    test-writer  sonnet-5-5  111     20           197.0M      11%    10.7M        97k          401k     44%           $78.5   $0.7   
    e2e          sonnet-5-5  53      33           185.8M      10%    6.6M         125k         220k     59%           $55.2   $1.0   
    test-writer  haiku-5-5   56      28           153.5M      9%     6.5M         116k         260k     31%           $11.6   $0.2   
    reviewer     opus-5-5    300     9            153.2M      9%     17.5M        58k          181k     35%           $195.1  $0.7   
    implementer  haiku-5-5   9       50           85.5M       5%     1.5M         163k         549k     44%           $5.0    $0.6   
    implementer  opus-5-5    1       27           1.7M        0%     0.1M         86k          86k      41%           $1.1    $1.1   
    

Derived (tiered runs only, 278): blocked at `tests` or `implement` 15 (5.4%), 1.75 deviations per run; Tier 1
escalation 3.3%, re-review 10.0%; Tier 2 escalation 7.6%, re-review 24.1%; reviewer $0.650 and 58,429 cache-write
tokens per agent. 15 of the 16 escalated agents in this window ended at once on the Claude session limit, so the
escalation count measures attempts, not escalated work.

Autobuild logs (40 runs, 2026-10-19 → 2026-11-07, 194 builds): 0 `--from-run` rescues, 2 `the build did not start`
stops (#626, #744) — about 0.5 per 50 builds.

## Open experiments

| Experiment | Started | Change | Metric | Take it back when |
|---|---|---|---|---|
| E1 | 2026-11-10 | `/build` relay: haiku `low` → `medium` | per 50 builds in the autobuild logs: ships rescued by `autobuild.mjs` (the `--from-run` path) plus `the build did not start` stops | the count does not fall |
| E2 | 2026-11-10 | reviewer prompt cache: 1 hour → 5 minutes | reviewer `$/agent`, over at least 20 runs | cache-write tokens per reviewer agent rise by more than 60% (the break-even point of the two write prices) |
| E3 (waiting) | — | implementer Tier 2: `high` → `medium` | Tier 2 escalation rate and re-review rate, over at least 20 runs | either rate rises by more than 5 percentage points over the baseline |
| Observation | 2026-11-10 | `/design` at `medium` (was `high`) | for the next 20 specs: share of runs blocked at `tests` or `implement`, and deviations per run | either rises by more than 5 percentage points over the baseline; `/design` then returns to `high` |

A decided experiment leaves this table; its outcome is then simply the value in the code.

**E3, prepared.** Starts only after the `/design` observation is decided, because both move the same quality numbers.
The change is one line in `LEVELS`: `implementer: { …, 2: { model: 'sonnet', effort: 'medium' } }`. Evaluate it with
`node scripts/run-cost.mjs --since <start date>`: the Tier 2 row's `escalated` / `runs` and `re-reviewed` / `runs`
against 7.6% and 24.1% above, and its `$/run` against $2.7.

## When a new model ships

1. Change the pinned id everywhere `scripts/claude-setup.test.mjs` reports it.
2. Update `PRICES` in `scripts/run-cost.mjs` (and its test).
3. Start again from the model's documented default effort, not from the old value.
4. Re-read the agent prompts for workarounds the new model no longer needs.

## When to use Fable 5.2 by hand

An ambiguous root-cause investigation or an architecture decision: `/model fable` at `high` effort. Describe the
outcome wanted and let it plan the steps. It is never part of the pipeline.
