import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
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
