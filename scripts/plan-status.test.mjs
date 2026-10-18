import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { AREAS } from "./areas.mjs";
import { buildState, createSandbox } from "./fake-gh-sandbox.mjs";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");

// #1 Backend, #2 no area, epic #3 whose parts #4-#6 are Backend, Frontend, E2E
const areaState = () =>
  buildState({
    issues: [
      { number: 1, status: "Todo", title: "Backend item", labels: ["spec"], area: "Backend" },
      { number: 2, status: "Todo", title: "Unlabelled item", labels: ["spec"] },
      { number: 3, status: "Todo", title: "Area epic", labels: ["epic"] },
      { number: 4, status: "Todo", title: "Backend part", labels: ["spec"], parent: 3, area: "Backend" },
      { number: 5, status: "Todo", title: "Frontend part", labels: ["spec"], parent: 3, area: "Frontend" },
      { number: 6, status: "Todo", title: "E2E part", labels: ["spec"], parent: 3, area: "E2E" },
    ],
  });

const rowOf = (out, n) =>
  out
    .split(/\r?\n/)
    .filter((l) => l.startsWith("| #"))
    .map((l) => l.split("|").slice(1, -1).map((c) => c.trim()))
    .find((cells) => cells[0] === `#${n}`);

test("area column and epic sequence", () => {
  const sandbox = createSandbox(areaState(), { branch: "" });
  try {
    const { code, out } = sandbox.run("plan-status.mjs", []);

    assert.equal(code, 0, out);
    assert.match(out, /^\| Issue \| Title \| Status \| Area \| Tier · Kind \| Where it stands \|$/m);
    assert.equal(rowOf(out, 1)?.[3], "Backend");
    assert.equal(rowOf(out, 2)?.[3], "—");
    assert.equal(rowOf(out, 3)?.[3], "Backend → Frontend → E2E");
  } finally {
    sandbox.cleanup();
  }
});

test("filter by area keeps matching epics", () => {
  const sandbox = createSandbox(areaState(), { branch: "" });
  try {
    const frontend = sandbox.run("plan-status.mjs", ["--area", "frontend"]);

    assert.equal(frontend.code, 0, frontend.out);
    assert.ok(rowOf(frontend.stdout, 3), "epic #3 has a part in Frontend and stays");
    assert.equal(rowOf(frontend.stdout, 1), undefined);
    assert.equal(rowOf(frontend.stdout, 2), undefined);
    const queued = frontend.stdout.split(/\r?\n/).filter((l) => /^\d+\. #/.test(l));
    assert.deepEqual(
      queued.map((l) => l.match(/#(\d+)/)[1]),
      ["3"],
    );

    const upper = sandbox.run("plan-status.mjs", ["--area", "FRONTEND"]);
    assert.equal(upper.stdout, frontend.stdout);

    const bogus = sandbox.run("plan-status.mjs", ["--area", "nope"]);
    assert.notEqual(bogus.code, 0, bogus.out);
    for (const area of AREAS) assert.ok(bogus.out.includes(area), `${area} is listed`);
  } finally {
    sandbox.cleanup();
  }
});

test("board skill passes an area to --area", () => {
  const skill = readFileSync(path.join(root, ".claude", "skills", "board", "SKILL.md"), "utf8");
  assert.match(skill, /^argument-hint: "\[area\]"$/m);
  assert.match(skill, /--area/);
});
