---
name: flows
description: Regenerate the local offline Polish workflow map (skarbiec-plan/flows/) - extracts commands, workflows, agents and scripts, writes a step description for every stale node, builds the HTML. Writes only under skarbiec-plan/flows/.
argument-hint: "[--all]"
disable-model-invocation: true
model: sonnet
effort: medium
allowed-tools: Bash, Read, Write, Grep, Glob
---

Regenerate the workflow map. Talk to the user in Polish; write the descriptions in Polish.

## Steps

1. Run `node scripts/flows.mjs facts $ARGUMENTS`. The last line `FLOWS_RESULT: {json}` lists `stale` nodes
   (description missing, or written for older sources). Nothing stale -> say so and stop.
2. For each stale node run `node scripts/flows.mjs facts --node <id>` to read its facts and `sources`, read
   those source files, then write `skarbiec-plan/flows/.work/desc/<id>.json` (the `:` of the id becomes `-`,
   e.g. `script:ship` -> `script-ship.json`) as JSON:
   `{ "schema": 3, "sources": "<hash of that node from skarbiec-plan/flows/.work/facts.json>", "summary": "...", "steps": [ { "id": "s1", "title": "...", "detail": "...", "condition": "...", "agent": "...", "retries": 2, "limit": { "ref": "<node id>#<CONSTANT>", "text": "..." }, "calls": "<node id>", "measure": "<measurableKeys entry>", "next": [ { "to": "s2", "when": "..." } ] } ] }`
   - `summary`, `title`, `detail`, `condition`, `limit.text`, `next.when` in Polish; `condition`, `agent`, `retries`,
     `limit` and `calls` only when the sources state them; `agent` must be the name of an existing agent;
   - the 7 pipeline nodes (`cmd:idea`, `cmd:design`, `cmd:build`, `cmd:fix`, `wf:build-feature`, `script:ship`,
     `script:autobuild`) are graphs: every step has `next` (`[]` on a terminal step), every `to` is a step of the same node, every
     step is reachable from the first. Name every branch the sources state (labels, tier, `skip-tests`, `e2e` part,
     blocked/ready, escalation, CI result) as a `next` with a `when`; a loop is a `next` back to an earlier step;
   - `limit.ref` whenever the limit is one of the `limitKeys` that `facts --node <id>` prints for that node (any node
     may use it); `limit.text` only for a prose limit with no such constant; `calls` for a step that runs another node;
   - `measure`: set it on every step that matches a key of the `measurableKeys` that `facts --node <id>` prints
     (`wf:build-feature`: the `agent:<type>:<label>` of the agent the step spawns, `run:build-feature` for a step
     that stands for the whole run, `verify:implementer` for the verification inside the implementer; `script:autobuild`:
     `autobuild:build` for the claude build of a part, `autobuild:ci` for the wait for CI); a step with no matching key, and every
     interactive step (`/idea`, `/design` sessions), gets none. Never copy numbers into a description - `build`
     renders them from the run records and `brak pomiarów` where no sample exists;
   - other nodes keep a plain list: `next` may be omitted;
   - every step comes from the sources you read - never invent a step, a branch, a retry or a limit;
   - steps in the order the node does them, one per real action, not per line of code.
3. Run `node scripts/flows.mjs build`. A failure names the node and the field and leaves the previous map
   untouched: fix that description and run `build` again.
4. Report in at most three lines: which nodes were rewritten and the path `skarbiec-plan/flows/index.html`.

## Boundaries

Write only under `skarbiec-plan/flows/`. Edit no source file, run no git mutation, install nothing.
