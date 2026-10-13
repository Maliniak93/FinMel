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
   `{ "schema": 1, "sources": "<hash of that node from skarbiec-plan/flows/.work/facts.json>", "summary": "...", "steps": [ { "id": "s1", "title": "...", "detail": "...", "condition": "...", "agent": "...", "retries": 2, "limit": "..." } ] }`
   - `summary`, `title`, `detail`, `condition`, `limit` in Polish; `condition`, `agent`, `retries` and `limit` only
     when the sources state them; `agent` must be the name of an existing agent;
   - every step comes from the sources you read - never invent a step, a branch, a retry or a limit;
   - steps in the order the node does them, one per real action, not per line of code.
3. Run `node scripts/flows.mjs build`. A failure names the node and the field and leaves the previous map
   untouched: fix that description and run `build` again.
4. Report in at most three lines: which nodes were rewritten and the path `skarbiec-plan/flows/index.html`.

## Boundaries

Write only under `skarbiec-plan/flows/`. Edit no source file, run no git mutation, install nothing.
