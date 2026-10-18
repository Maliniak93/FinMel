import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import path from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import { mayStart, nextWork, planFrom, startAt, startWork, windowEnd } from "./epic-queue.mjs";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");

const cards = [
  { number: 1, title: "running", status: "In progress", epic: false, epicRun: true },
  { number: 2, title: "manual", status: "In progress", epic: false, epicRun: false },
  { number: 3, title: "todo a", status: "Todo", epic: false },
  { number: 4, title: "todo b", status: "Todo", epic: true, subIssues: [5, 6] },
  { number: 5, title: "part 1", status: "Done", parent: 4 },
  { number: 6, title: "part 2", status: "Todo", parent: 4 },
  { number: 7, title: "finished", status: "Done", epic: false },
];
const queue = [
  { ...cards[0], manual: false },
  { ...cards[1], manual: true },
  { ...cards[2], manual: false },
  { ...cards[3], manual: false, next: 6 },
];
const numbers = (plan) => plan.map((p) => p.number);

test("plan skips manual by default", () => {
  assert.deepEqual(numbers(planFrom(queue, {}, cards)), [1, 3, 4]);
});

test("plan includes manual and limits items", () => {
  assert.deepEqual(numbers(planFrom(queue, { includeManual: true, items: 2 }, cards)), [1, 2]);
});

test("plan for one number", () => {
  assert.deepEqual(numbers(planFrom(queue, { only: 4 }, cards)), [4]);
  assert.deepEqual(numbers(planFrom(queue, { only: 2 }, cards)), [2]);
  assert.deepEqual(planFrom(queue, { only: 7 }, cards), []);
  assert.throws(() => planFrom(queue, { only: 6 }, cards), /epic #4/);
});

test("next work for standalone and epic", () => {
  assert.equal(nextWork(cards[2], cards), 3);
  assert.equal(nextWork(cards[3], cards), 6);
});

test("times are the next occurrence", () => {
  const at = (d, h, m) => new Date(2026, 9, d, h, m, 0, 0);
  const start = at(18, 23, 0);
  assert.equal(windowEnd("07:00", start).getTime(), at(19, 7, 0).getTime());
  assert.equal(startAt("02:30", start).getTime(), at(19, 2, 30).getTime());
  assert.equal(startAt("15", start).getTime(), at(18, 23, 15).getTime());
  assert.equal(startAt("now", start).getTime(), start.getTime());
  assert.equal(windowEnd("07:00", at(18, 6, 0)).getTime(), at(18, 7, 0).getTime());
});

test("window blocks new builds", () => {
  const end = new Date(2026, 9, 19, 7, 0, 0, 0);
  assert.equal(mayStart(end, end), "window");
  assert.equal(mayStart(end, new Date(2026, 9, 19, 7, 5, 0, 0)), "window");
  assert.ok(!mayStart(end, new Date(2026, 9, 19, 6, 59, 0, 0)));
  assert.ok(!mayStart(null, new Date()));
});

test("start labels item and part", async () => {
  const calls = [];
  await startWork({
    item: 4,
    part: 6,
    gh: async (argv) => void calls.push(["gh", ...argv].join(" ")),
    build: async () => void calls.push("build"),
  });
  const labels = calls.filter((c) => c.includes("--add-label"));
  assert.ok(labels.some((c) => c.includes(" 4 ") && c.includes("epic-run")));
  assert.ok(labels.some((c) => c.includes(" 6 ") && c.includes("epic-run")));
  assert.equal(calls.at(-1), "build");
  assert.ok(calls.indexOf("build") > calls.findLastIndex((c) => c.includes("--add-label")));
});

test("delay is an unknown flag", () => {
  const res = spawnSync(process.execPath, ["scripts/epic.mjs", "423", "--dry-run", "--delay", "5"], { cwd: root, encoding: "utf8" });
  assert.equal(res.status, 2);
  const out = `${res.stdout}${res.stderr}`;
  assert.match(out, /unexpected argument: --delay/);
  assert.doesNotMatch(out, /runner online/);
});

test("docs no longer mention delay and describe the window", () => {
  const read = (f) => readFileSync(path.join(root, f), "utf8");
  for (const f of ["CLAUDE.md", "skarbiec-plan/workflow.md", "scripts/epic.mjs"]) assert.doesNotMatch(read(f), /--delay/, f);
  assert.match(read("skarbiec-plan/workflow.md"), /--until/);
  assert.match(read("CLAUDE.md"), /--until/);
});
