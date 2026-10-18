import assert from "node:assert/strict";
import test from "node:test";
import { buildState, createSandbox } from "./fake-gh-sandbox.mjs";

const codeMapBody = (...paths) => `## Goal\n\nx\n\n## Code map\n\n${paths.map((p) => `- ${p}`).join("\n")}\n\n## Out of scope\n\n- nothing\n`;
const writes = (sandbox) => sandbox.log().filter((e) => e.kind === "setField" || e.kind === "fieldBatch" || e.kind === "mutation");

test("dry run resolves from PR files and Code map", () => {
  const state = buildState({
    issues: [
      { number: 10, status: "Done", labels: ["spec"], branch: "feat/a" },
      { number: 11, status: "In progress", labels: ["spec"], body: codeMapBody("scripts/q.mjs") },
      { number: 12, status: "Todo", labels: ["epic"] },
      { number: 13, status: "Done", labels: ["spec"], branch: "feat/d", area: "Docs" },
      { number: 14, status: "Done", labels: ["spec"], branch: "feat/none" },
    ],
    prs: [
      { number: 50, headRefName: "feat/a", files: [{ path: "web/x.ts" }, { path: "services/y.cs" }] },
      { number: 51, headRefName: "feat/d", files: [{ path: "skarbiec-plan/a.md" }] },
    ],
  });
  const sandbox = createSandbox(state);
  try {
    const run = sandbox.run("area-backfill.mjs", []);

    assert.equal(run.code, 0, run.out);
    assert.match(run.stdout, /#10 → Fullstack \(PR #50\)/);
    assert.match(run.stdout, /#11 → Scripts \(Code map\)/);
    assert.doesNotMatch(run.stdout, /#12 →/);
    assert.doesNotMatch(run.stdout, /#13 →/);
    const unresolved = run.stdout.slice(run.stdout.indexOf("Unresolved"));
    assert.ok(run.stdout.includes("Unresolved"), run.stdout);
    assert.match(unresolved, /#14/);
    assert.doesNotMatch(unresolved, /#1[0-3]/);
    assert.deepEqual(writes(sandbox), []);
    assert.equal(sandbox.state().issues[10].area, null);
  } finally {
    sandbox.cleanup();
  }
});

test("apply batches 20 per request and is idempotent", () => {
  const issues = Array.from({ length: 45 }, (_, k) => ({
    number: 200 + k,
    status: "Todo",
    labels: ["spec"],
    body: codeMapBody("scripts/q.mjs"),
  }));
  const sandbox = createSandbox(buildState({ issues }));
  try {
    const first = sandbox.run("area-backfill.mjs", ["--apply"]);

    assert.equal(first.code, 0, first.out);
    assert.equal(sandbox.log().filter((e) => e.kind === "prList").length, 1);
    assert.deepEqual(
      sandbox.log().filter((e) => e.kind === "fieldBatch").map((e) => e.size),
      [20, 20, 5],
    );
    for (const i of Object.values(sandbox.state().issues)) assert.equal(i.area, "Scripts", `#${i.number}`);
    assert.match(first.stdout, /GitHub: GraphQL/);

    const before = writes(sandbox).length;
    const second = sandbox.run("area-backfill.mjs", ["--apply"]);

    assert.equal(second.code, 0, second.out);
    assert.equal(writes(sandbox).length, before);
  } finally {
    sandbox.cleanup();
  }
});
