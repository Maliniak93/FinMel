import assert from "node:assert/strict";
import { appendFileSync, existsSync, rmSync } from "node:fs";
import path from "node:path";
import { test } from "node:test";
import { descPath, makeSandbox, put, readFacts, readyToBuild, runBuild, runFacts } from "./helpers.mjs";

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
