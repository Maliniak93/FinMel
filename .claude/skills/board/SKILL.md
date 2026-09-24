---
name: board
description: Show the FinMel GitHub project board against git - open spec issues, what to build next, and cards whose state disagrees with the branches. Changes nothing.
argument-hint: ""
disable-model-invocation: true
model: haiku
effort: low
allowed-tools: Bash
---

Run `node scripts/plan-status.mjs` and print its output verbatim, minus the first two lines (title and
generated-comment). Add nothing, change nothing.
