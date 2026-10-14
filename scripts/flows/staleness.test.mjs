import assert from "node:assert/strict";
import { appendFileSync, existsSync, rmSync } from "node:fs";
import path from "node:path";
import { test } from "node:test";
import { mkdirSync, writeFileSync } from "node:fs";
import { readFileSync } from "node:fs";
import { T0, descPath, descriptionFor, historyDir, makeSandbox, pipelineReady, put, readFacts, readyToBuild, runBuild, runFacts, step, writeRun } from "./helpers.mjs";

const ALL = ["agent:worker", "cmd:alpha", "script:beta", "script:gamma", "wf:flow-one"];

function described() {
  const sb = makeSandbox();
  readyToBuild(sb);
  return sb;
}

const staleOf = (run) => [...run.result.stale].sort();

test("facts without any description reports every node stale", () => {
  const sb = makeSandbox();
  try {
    const run = runFacts(sb);
    assert.equal(run.status, 0, run.output);
    assert.deepEqual(staleOf(run), ALL);
  } finally {
    sb.cleanup();
  }
});

test("facts on unchanged sources reports no stale node and --all reports every node", () => {
  const sb = described();
  try {
    const run = runFacts(sb);
    assert.equal(run.status, 0, run.output);
    assert.deepEqual(staleOf(run), []);
    assert.deepEqual(staleOf(runFacts(sb, ["--all"])), ALL);
  } finally {
    sb.cleanup();
  }
});

test("facts after an agent file changes reports the agent and every node naming it, directly or through a workflow, and no other", () => {
  const sb = described();
  try {
    appendFileSync(path.join(sb.root, ".claude/agents/worker.md"), "\nAlso do something new.\n");
    const run = runFacts(sb);
    assert.equal(run.status, 0, run.output);
    assert.deepEqual(staleOf(run), ["agent:worker", "cmd:alpha", "wf:flow-one"]);
  } finally {
    sb.cleanup();
  }
});

test("facts after a workflow or the agent it names changes reports a command that names only the workflow", () => {
  const sb = makeSandbox();
  try {
    put(
      sb,
      ".claude/skills/viawf/SKILL.md",
      "---\nname: viawf\ndescription: Runs only the workflow.\n---\n\nRun the `flow-one` workflow.\n",
    );
    readyToBuild(sb);
    assert.deepEqual(staleOf(runFacts(sb)), []);

    appendFileSync(path.join(sb.root, ".claude/agents/worker.md"), "\nAlso do something new.\n");
    assert.deepEqual(staleOf(runFacts(sb)), ["agent:worker", "cmd:alpha", "cmd:viawf", "wf:flow-one"]);

    readyToBuild(sb);
    appendFileSync(path.join(sb.root, ".claude/workflows/flow-one.js"), "\n// touched\n");
    assert.deepEqual(staleOf(runFacts(sb)), ["cmd:viawf", "wf:flow-one"]);
  } finally {
    sb.cleanup();
  }
});

test("facts after a file in a skill dir or a script dir changes reports that node", () => {
  const sb = makeSandbox();
  try {
    put(sb, ".claude/skills/alpha/extra.md", "reference\n");
    put(sb, "scripts/beta/helper.mjs", "export const x = 1;\n");
    put(sb, "scripts/beta/helper.test.mjs", "export const t = 1;\n");
    readyToBuild(sb);

    appendFileSync(path.join(sb.root, ".claude/skills/alpha/extra.md"), "more\n");
    assert.deepEqual(staleOf(runFacts(sb)), ["cmd:alpha"]);
    readyToBuild(sb);

    appendFileSync(path.join(sb.root, "scripts/beta/helper.mjs"), "export const y = 2;\n");
    assert.deepEqual(staleOf(runFacts(sb)), ["script:beta"]);
    readyToBuild(sb);

    appendFileSync(path.join(sb.root, "scripts/beta/helper.test.mjs"), "export const u = 3;\n");
    assert.deepEqual(staleOf(runFacts(sb)), []);
  } finally {
    sb.cleanup();
  }
});

test("facts after CLAUDE.md changes reports no stale node", () => {
  const sb = described();
  try {
    appendFileSync(path.join(sb.root, "CLAUDE.md"), "\nA new rule.\n");
    const run = runFacts(sb);
    assert.equal(run.status, 0, run.output);
    assert.deepEqual(staleOf(run), []);
  } finally {
    sb.cleanup();
  }
});

test("facts after a script is deleted drops its node and build removes its description; an added script is reported stale", () => {
  const sb = described();
  try {
    rmSync(path.join(sb.root, "scripts/gamma.mjs"));
    rmSync(path.join(sb.root, "scripts/gamma.test.mjs"));
    put(sb, "scripts/delta.mjs", "#!/usr/bin/env node\n// Usage: node scripts/delta.mjs\nconsole.log('delta');\n");

    const run = runFacts(sb);
    assert.equal(run.status, 0, run.output);
    assert.deepEqual(staleOf(run), ["script:delta"]);
    assert.ok(!("script:gamma" in readFacts(sb).nodes));

    const build = runBuild(sb);
    assert.equal(build.status, 0, build.output);
    assert.ok(!existsSync(descPath(sb, "script:gamma")), "gamma description deleted");
    assert.ok(existsSync(descPath(sb, "script:beta")), "other descriptions kept");
  } finally {
    sb.cleanup();
  }
});

test("facts reports every node stale when its description is stamped with schema 1", () => {
  const sb = described();
  try {
    const facts = readFacts(sb);
    for (const [id, node] of Object.entries(facts.nodes)) {
      mkdirSync(path.dirname(descPath(sb, id)), { recursive: true });
      writeFileSync(descPath(sb, id), JSON.stringify(descriptionFor(id, node.hash, { schema: 1 })));
    }
    assert.deepEqual(staleOf(runFacts(sb)), ALL);
  } finally {
    sb.cleanup();
  }
});

test("facts after only the run records change reports no stale node and the next build shows the new numbers; schema 2 descriptions are all stale", () => {
  const sb = makeSandbox();
  try {
    const h = historyDir(sb);
    writeRun(h, { id: "p1", startMs: T0, durationSec: 100, status: "ready", agents: [{ type: "implementer", label: "implement the spec", atSec: 0 }] });
    pipelineReady(sb, { wf: [step("a", { agent: "implementer", measure: "run:build-feature", next: [{ to: "b" }] }), step("b", { agent: "scout", next: [] })] });
    assert.deepEqual(staleOf(runFacts(sb, ["--history", h])), []);
    assert.equal(runBuild(sb, ["--history", h]).status, 0);
    assert.match(readFileSync(path.join(sb.out, "index.html"), "utf8"), /n=1\b/);

    writeRun(h, { id: "p2", startMs: T0 + 100000, durationSec: 300, status: "ready", agents: [{ type: "implementer", label: "implement the spec", atSec: 0 }] });
    const run = runFacts(sb, ["--history", h]);
    assert.equal(run.status, 0, run.output);
    assert.deepEqual(staleOf(run), []);
    assert.equal(runBuild(sb, ["--history", h]).status, 0);
    assert.match(readFileSync(path.join(sb.out, "index.html"), "utf8"), /n=2\b/);

    const facts = readFacts(sb);
    for (const [id, node] of Object.entries(facts.nodes)) {
      writeFileSync(descPath(sb, id), JSON.stringify(descriptionFor(id, node.hash, { schema: 2 })));
    }
    assert.equal(staleOf(runFacts(sb, ["--history", h])).length, Object.keys(facts.nodes).length);
  } finally {
    sb.cleanup();
  }
});
