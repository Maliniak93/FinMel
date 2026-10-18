import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import { awaitReady, buildWithResume, decideOnLimit, finishShutdown, handleLimit } from "./epic-limit.mjs";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const MIN = 60e3;
const now = new Date(2026, 9, 18, 23, 0, 0);
const after = (ms) => new Date(now.getTime() + ms);
const base = { now, onLimit: "sleep", maxWaitHours: 12, timersWork: true, hibernateAvailable: true, windowEnd: null };
const decide = (extra) => decideOnLimit({ ...base, ...extra });

test("suspend under an hour, hibernate from an hour", () => {
  for (const [wait, action] of [[10 * MIN, "wait"], [40 * MIN, "suspend"], [180 * MIN, "hibernate"]]) {
    const result = decide({ resetsAt: after(wait) });
    assert.equal(result.action, action);
    assert.equal(result.until.getTime(), after(wait + 5 * MIN).getTime());
  }
  assert.equal(decide({ resetsAt: after(180 * MIN), hibernateAvailable: false }).action, "suspend");
  for (const wait of [10 * MIN, 40 * MIN, 180 * MIN]) {
    assert.equal(decide({ resetsAt: after(wait), timersWork: false }).action, "wait");
  }
});

test("limit end reasons", () => {
  const unknown = decide({ resetsAt: null });
  assert.deepEqual([unknown.action, unknown.reason], ["end", "limit-unknown"]);
  const far = decide({ resetsAt: after(30 * 60 * MIN) });
  assert.deepEqual([far.action, far.reason], ["end", "limit-too-far"]);
  const exit = decide({ resetsAt: after(40 * MIN), onLimit: "exit" });
  assert.deepEqual([exit.action, exit.reason], ["end", "limit"]);
});

test("window before reset ends the run", () => {
  const result = decide({ resetsAt: after(180 * MIN), windowEnd: after(60 * MIN) });
  assert.deepEqual([result.action, result.reason], ["end", "window"]);
});

function powerFakes(answer) {
  const calls = [];
  return {
    calls,
    confirm: async (action) => (calls.push(["confirm", action]), answer),
    suspendUntil: async (date, mode) => void calls.push([mode, date.getTime()]),
    waitUntil: async (date) => void calls.push(["wait", date.getTime()]),
    log: () => {},
  };
}

test("popup no turns sleep into an awake wait", async () => {
  const resetsAt = after(180 * MIN);
  const until = after(185 * MIN).getTime();
  for (const answer of ["yes", "timeout"]) {
    const fakes = powerFakes(answer);
    await handleLimit({ ...base, resetsAt }, fakes);
    assert.deepEqual(fakes.calls, [["confirm", "hibernate"], ["hibernate", until]]);
  }
  const no = powerFakes("no");
  await handleLimit({ ...base, resetsAt }, no);
  assert.deepEqual(no.calls, [["confirm", "hibernate"], ["wait", until]]);
});

test("readiness retries then gives up", async () => {
  const clock = () => {
    let t = 0;
    return { now: () => t, sleep: async (ms) => void (t += ms), elapsed: () => t };
  };
  let failures = 2;
  const first = clock();
  const ok = await awaitReady({ check: async () => failures-- <= 0, now: first.now, sleep: first.sleep });
  assert.equal(ok.ready, true);
  assert.equal(first.elapsed(), 60e3);

  const second = clock();
  const bad = await awaitReady({ check: async () => false, now: second.now, sleep: second.sleep });
  assert.equal(bad.ready, false);
  assert.equal(bad.reason, "wake-not-ready");
  assert.ok(second.elapsed() >= 15 * MIN && second.elapsed() <= 16 * MIN);
});

test("resume rebuilds the same part uncounted", async () => {
  const ctx = { builds: 1 };
  const part = { number: 7 };
  const built = [];
  const events = [];
  const results = [{ limit: { resetsAt: after(40 * MIN) } }, {}];
  const result = await buildWithResume({
    ctx,
    part,
    build: async (p) => (built.push(p.number), events.push("build"), results.shift()),
    onLimit: async () => void events.push("limit"),
    awaitReady: async () => (events.push("ready"), { ready: true }),
  });
  assert.deepEqual(built, [7, 7]);
  assert.deepEqual(events, ["build", "limit", "ready", "build"]);
  assert.equal(ctx.builds, 1);
  assert.equal(result.limit, undefined);
});

test("shutdown asks first", async () => {
  for (const answer of ["yes", "timeout"]) {
    const runs = [];
    await finishShutdown({ confirm: async () => answer, run: (cmd, args) => runs.push([cmd, ...args]) });
    assert.deepEqual(runs, [["shutdown", "/s", "/t", "0"]]);
  }
  const declined = [];
  await finishShutdown({ confirm: async () => "no", run: (...a) => declined.push(a) });
  assert.deepEqual(declined, []);

  for (const flags of [{ noShutdown: true }, { dryRun: true }]) {
    let asked = 0;
    const runs = [];
    await finishShutdown({ ...flags, confirm: async () => (asked++, "yes"), run: (...a) => runs.push(a) });
    assert.equal(asked, 0);
    assert.deepEqual(runs, []);
  }
});

test("docs list the limit flags and no shutdown delay", () => {
  const workflow = readFileSync(path.join(root, "skarbiec-plan", "workflow.md"), "utf8");
  for (const word of ["--on-limit", "--max-wait", "--sleep-test", "--hibernate-test", "limit-unknown", "limit-too-far", "wake-not-ready"]) {
    assert.ok(workflow.includes(word), word);
  }
  const stale = "/t " + "600";
  for (const file of [["skarbiec-plan", "workflow.md"], ["CLAUDE.md"], ["scripts", "epic.mjs"]]) {
    assert.ok(!readFileSync(path.join(root, ...file), "utf8").includes(stale), file.join("/"));
  }
});
