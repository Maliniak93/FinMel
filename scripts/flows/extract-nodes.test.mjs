import assert from "node:assert/strict";
import { test } from "node:test";
import { has, makeSandbox, nodeIds, readFacts, runFacts } from "./helpers.mjs";

test("facts lists exactly the command, agent, workflow and two scripts with their frontmatter, flags, limits, phases and level rows", () => {
  const sb = makeSandbox();
  try {
    const run = runFacts(sb);
    assert.equal(run.status, 0, run.output);
    const facts = readFacts(sb);

    assert.deepEqual(nodeIds(facts), ["agent:worker", "cmd:alpha", "script:beta", "script:gamma", "wf:flow-one"]);

    const cmd = facts.nodes["cmd:alpha"];
    assert.equal(cmd.kind, "command");
    for (const text of ["Alpha command that does the alpha thing.", "<thing>", "sonnet", "medium", "Bash, Read"]) {
      assert.ok(has(cmd, text), `command fact "${text}"`);
    }

    const agent = facts.nodes["agent:worker"];
    assert.equal(agent.kind, "agent");
    for (const text of ["haiku", "high", "Read, Grep, Bash", "Agent", "pb", "Does the work.", "600000"]) {
      assert.ok(has(agent, text), `agent fact "${text}"`);
    }

    const wf = facts.nodes["wf:flow-one"];
    assert.equal(wf.kind, "workflow");
    for (const text of ["Fixture workflow with two phases", "Prepare", "get ready", "Run", "do the work"]) {
      assert.ok(has(wf, text), `workflow fact "${text}"`);
    }
    for (const row of ["tier >= 2 → sonnet/high", "noTests → haiku/high", "otherwise → sonnet/medium"]) {
      assert.ok(has(wf, row), `level row "${row}"`);
    }

    const beta = facts.nodes["script:beta"];
    assert.equal(beta.kind, "script");
    for (const text of ["--quick", "skip the slow part", "--out", "write the result there", "DEFAULT_TIMEOUT_MS", "30000", "MAX_RETRIES"]) {
      assert.ok(has(beta, text), `script beta fact "${text}"`);
    }

    const gamma = facts.nodes["script:gamma"];
    for (const text of ["--all", "--dry-run"]) assert.ok(has(gamma, text), `script gamma flag "${text}"`);
  } finally {
    sb.cleanup();
  }
});
