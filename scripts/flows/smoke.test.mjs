import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, rmSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { REPO_ROOT, edgeCount, markerOf, readFacts, runFlows } from "./helpers.mjs";

test("facts on this repository finds the core nodes, edges and markers and no playbook", () => {
  const dir = mkdtempSync(path.join(os.tmpdir(), "flows-smoke-"));
  try {
    const out = path.join(dir, "out");
    const run = runFlows(["facts", "--root", REPO_ROOT, "--out", out]);
    assert.equal(run.status, 0, run.output);
    const facts = readFacts({ out });
    const ids = Object.keys(facts.nodes);

    for (const id of ["cmd:build", "cmd:design", "cmd:fix", "cmd:idea", "wf:build-feature", "agent:implementer", "script:ship", "script:epic"]) {
      assert.ok(ids.includes(id), `${id} present`);
    }
    for (const id of ids) assert.doesNotMatch(id, /playbook/, `${id} is a playbook`);

    const rows = JSON.stringify(facts.nodes["wf:build-feature"].facts).match(/ → /g) ?? [];
    assert.ok(rows.length >= 4, `build-feature level rows: ${rows.length}`);

    assert.equal(edgeCount(facts, "script:epic", "cmd:build"), 1);
    assert.equal(edgeCount(facts, "cmd:build", "wf:build-feature"), 1);
    assert.equal(edgeCount(facts, "wf:build-feature", "agent:implementer"), 1);

    assert.ok(markerOf(facts.nodes["script:ship"], "zmienia git"));
    assert.ok(markerOf(facts.nodes["script:ship"], "pisze do GitHuba"));
    assert.ok(markerOf(facts.nodes["script:epic"], "merguje"));
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test("facts on this repository lists the referable limits of the pipeline scripts and has all 7 pipeline nodes", () => {
  const dir = mkdtempSync(path.join(os.tmpdir(), "flows-smoke-"));
  try {
    const run = runFlows(["facts", "--root", REPO_ROOT, "--out", path.join(dir, "out")]);
    assert.equal(run.status, 0, run.output);
    const facts = readFacts({ out: path.join(dir, "out") });
    for (const id of ["cmd:idea", "cmd:design", "cmd:build", "cmd:fix", "wf:build-feature", "script:ship", "script:epic"]) {
      assert.ok(id in facts.nodes, `${id} present`);
    }
    const limit = (node) => (facts.nodes[node].facts.limits ?? []).map((l) => l.name);
    assert.ok(limit("script:verify").includes("DEFAULT_DEADLINE_MIN"));
    assert.ok(limit("script:epic").includes("MAX_E2E_REPAIRS"));
    assert.ok(limit("script:epic").includes("CI_TIMEOUT_MS"));
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test("facts on this repository with an empty history exits 0 and every measurement has n=0", () => {
  const dir = mkdtempSync(path.join(os.tmpdir(), "flows-smoke-"));
  try {
    const history = path.join(dir, "history");
    const epicRuns = path.join(dir, "epic-runs");
    mkdirSync(history);
    mkdirSync(epicRuns);
    const run = runFlows(["facts", "--root", REPO_ROOT, "--out", path.join(dir, "out"), "--history", history, "--epic-runs", epicRuns]);
    assert.equal(run.status, 0, run.output);
    const measurements = Object.entries(readFacts({ out: path.join(dir, "out") }).measurements ?? {});
    assert.ok(measurements.length > 0, "measurement keys listed");
    for (const [key, m] of measurements) assert.equal(m.n, 0, `${key} has no samples`);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
