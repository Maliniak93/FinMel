import assert from "node:assert/strict";
import { test } from "node:test";
import { edgeCount, makeSandbox, put, readFacts, runFacts } from "./helpers.mjs";

const USAGE = (name) => `#!/usr/bin/env node\n// Usage: node scripts/${name}.mjs\n`;

test("facts produces one edge of the right kind and direction per call in skills, workflows and scripts", () => {
  const sb = makeSandbox();
  try {
    put(sb, "scripts/b.mjs", `${USAGE("b")}console.log("b");\n`);
    put(
      sb,
      ".claude/skills/caller/SKILL.md",
      "---\nname: caller\ndescription: Calls things.\n---\n\nRun `node scripts/b.mjs`, then the `flow-one` workflow, then ask the `worker` agent.\n",
    );
    put(
      sb,
      "scripts/spawner.mjs",
      `${USAGE("spawner")}import { spawnSync } from "node:child_process";\nspawnSync(process.execPath, ["scripts/b.mjs"]);\n`,
    );
    put(
      sb,
      "scripts/launcher.mjs",
      `${USAGE("launcher")}import { spawnSync } from "node:child_process";\nspawnSync("claude", ["-p", "/alpha do it"]);\n`,
    );

    const run = runFacts(sb);
    assert.equal(run.status, 0, run.output);
    const facts = readFacts(sb);

    assert.equal(edgeCount(facts, "cmd:caller", "script:b"), 1);
    assert.equal(edgeCount(facts, "cmd:caller", "wf:flow-one"), 1);
    assert.equal(edgeCount(facts, "cmd:caller", "agent:worker"), 1);
    assert.equal(facts.edges.filter((e) => e.from === "cmd:caller").length, 3);
    assert.equal(edgeCount(facts, "wf:flow-one", "agent:worker"), 1);
    assert.equal(edgeCount(facts, "script:spawner", "script:b"), 1);
    assert.equal(edgeCount(facts, "script:launcher", "cmd:alpha"), 1);
    assert.equal(facts.edges.filter((e) => e.from === "script:spawner").length, 1);
    assert.equal(facts.edges.filter((e) => e.from === "script:launcher").length, 1);
    assert.equal(facts.edges.filter((e) => e.from === e.to).length, 0, "no self edges");
  } finally {
    sb.cleanup();
  }
});

test("facts makes no edge from a bare scripts/<x>.mjs mention in a skill, agent or workflow", () => {
  const sb = makeSandbox();
  try {
    put(sb, "scripts/b.mjs", `${USAGE("b")}console.log("b");\n`);
    put(
      sb,
      ".claude/agents/talker.md",
      "---\nname: talker\ndescription: Only talks.\nmodel: haiku\n---\n\nThe `scripts/b.mjs` script is not yours.\n",
    );
    put(
      sb,
      ".claude/skills/talks/SKILL.md",
      "---\nname: talks\ndescription: Only talks.\n---\n\nSee scripts/b.mjs for details.\n",
    );
    put(
      sb,
      ".claude/workflows/talk.js",
      "export const meta = { name: 'talk', description: 'Only mentions scripts/b.mjs', phases: [] }\n",
    );

    const run = runFacts(sb);
    assert.equal(run.status, 0, run.output);
    const facts = readFacts(sb);
    assert.equal(edgeCount(facts, "agent:talker", "script:b"), 0);
    assert.equal(edgeCount(facts, "cmd:talks", "script:b"), 0);
    assert.equal(edgeCount(facts, "wf:talk", "script:b"), 0);
  } finally {
    sb.cleanup();
  }
});
