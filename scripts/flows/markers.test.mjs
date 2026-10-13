import assert from "node:assert/strict";
import { test } from "node:test";
import { makeSandbox, markerOf, put, readFacts, runFacts } from "./helpers.mjs";

const script = (name, body) =>
  `#!/usr/bin/env node\n// Usage: node scripts/${name}.mjs\nimport { spawnSync } from "node:child_process";\n${body}\n`;

test("facts gives scripts their direct markers, the read-only one none, and a command the inherited markers with their path", () => {
  const sb = makeSandbox();
  try {
    put(sb, "scripts/g.mjs", script("g", `spawnSync("git", ["commit", "-m", "x"]);`));
    put(sb, "scripts/m.mjs", script("m", `spawnSync("gh", ["pr", "merge", "1"]);`));
    put(sb, "scripts/e.mjs", script("e", `spawnSync("gh", ["issue", "edit", "1"]);`));
    put(sb, "scripts/verify.mjs", script("verify", `console.log("verify");`));
    put(sb, "scripts/compose.mjs", script("compose", `console.log("compose");`));
    put(sb, "scripts/e2e.mjs", script("e2e", `console.log("e2e");`));
    put(sb, "scripts/v.mjs", script("v", `spawnSync(process.execPath, ["scripts/verify.mjs", "--all"]);`));
    put(sb, "scripts/c.mjs", script("c", `spawnSync(process.execPath, ["scripts/compose.mjs", "up"]);`));
    put(sb, "scripts/x.mjs", script("x", `spawnSync(process.execPath, ["scripts/e2e.mjs", "run"]);`));
    put(sb, "scripts/ro.mjs", `#!/usr/bin/env node\n// Usage: node scripts/ro.mjs\nconsole.log("read only");\n`);
    put(
      sb,
      ".claude/agents/chainagent.md",
      "---\nname: chainagent\ndescription: Reaches scripts.\nmodel: haiku\n---\n\nRun `node scripts/g.mjs` and then `node scripts/v.mjs`.\n",
    );
    put(
      sb,
      ".claude/workflows/flow-three.js",
      "export const meta = {\n  name: 'flow-three',\n  description: 'Chain',\n  phases: [{ title: 'Go', detail: 'go' }],\n}\n\nawait step('chainagent', 'Chain', ['go'])\n",
    );
    put(
      sb,
      ".claude/skills/chain/SKILL.md",
      "---\nname: chain\ndescription: Reaches markers through a workflow and an agent.\n---\n\nRun the `flow-three` workflow.\n",
    );

    const run = runFacts(sb);
    assert.equal(run.status, 0, run.output);
    const { nodes } = readFacts(sb);

    assert.ok(markerOf(nodes["script:g"], "zmienia git")?.direct);
    assert.ok(markerOf(nodes["script:m"], "merguje")?.direct);
    assert.ok(markerOf(nodes["script:m"], "pisze do GitHuba")?.direct);
    assert.ok(markerOf(nodes["script:e"], "pisze do GitHuba")?.direct);
    assert.ok(markerOf(nodes["script:v"], "pełny verify")?.direct);
    assert.ok(markerOf(nodes["script:c"], "dotyka lokalnego stacku"));
    assert.ok(markerOf(nodes["script:compose"], "dotyka lokalnego stacku")?.direct);
    assert.ok(markerOf(nodes["script:x"], "dotyka izolowanego stacku e2e"));
    assert.ok(markerOf(nodes["script:e2e"], "dotyka izolowanego stacku e2e")?.direct);
    assert.equal(nodes["script:ro"].markers.length, 0);
    assert.equal(markerOf(nodes["script:verify"], "pełny verify"), undefined);

    const chain = nodes["cmd:chain"];
    const git = markerOf(chain, "zmienia git");
    const verify = markerOf(chain, "pełny verify");
    assert.ok(git && verify, "command inherits git and verify markers");
    assert.equal(git.direct, false);
    assert.equal(verify.direct, false);
    assert.match(git.via, /g\.mjs/);
    assert.match(verify.via, /v\.mjs/);
    assert.equal(chain.markers.filter((m) => m.direct).length, 0);
  } finally {
    sb.cleanup();
  }
});
