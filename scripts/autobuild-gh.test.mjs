import assert from "node:assert/strict";
import { test } from "node:test";
import { closeUnlessClosed, createTimeoutBudget, detectChildLimit, mergeUnlessMerged, postOnce, runStepWithLimit } from "./autobuild-gh.mjs";
import { skipMarker } from "./autobuild-queue.mjs";

const RESET = 1_800_000_600;
const marker = (kind, resetAt) => `GITHUB_LIMIT ${JSON.stringify({ kind, resetAt })}`;

function fakeClock(startMs = (RESET - 3600) * 1000) {
  const clock = { t: startMs, sleeps: [], logs: [] };
  clock.now = () => clock.t;
  clock.sleep = async (ms) => {
    clock.sleeps.push(ms);
    clock.t += ms;
  };
  clock.log = (line) => clock.logs.push(line);
  return clock;
}

test("detectChildLimit reads the marker or returns null", () => {
  assert.deepEqual(detectChildLimit({ code: 1, output: `noise\n${marker("primary-graphql", RESET)}\nmore` }), { kind: "primary-graphql", resetAt: RESET });
  assert.deepEqual(detectChildLimit({ code: 4, output: marker("primary-rest", RESET) }), { kind: "primary-rest", resetAt: RESET });
  assert.equal(detectChildLimit({ code: 1, output: "build failed" }), null);
  assert.equal(detectChildLimit({ code: 0, output: "fine" }), null);
});

test("child limit reruns same step", async () => {
  for (const first of [
    { code: 4, output: marker("primary-graphql", RESET) },
    { code: 1, output: `stream\n${marker("primary-graphql", RESET)}\n` },
  ]) {
    const clock = fakeClock();
    let calls = 0;
    const result = await runStepWithLimit(async () => (++calls === 1 ? first : { code: 0, output: "done" }), {
      log: clock.log,
      sleep: clock.sleep,
      now: clock.now,
    });
    assert.equal(calls, 2);
    assert.deepEqual(result, { code: 0, output: "done" });
    assert.deepEqual(clock.sleeps, [RESET * 1000 + 30_000 - (RESET - 3600) * 1000]);
    assert.ok(clock.logs.some((l) => /^limit GitHuba \(primary-graphql\), czekam do \d{2}:\d{2}$/.test(l)));
  }
});

test("a step that ends without the limit is returned as is", async () => {
  const clock = fakeClock();
  let calls = 0;
  const result = await runStepWithLimit(async () => (++calls, { code: 1, output: "tests failed" }), {
    log: clock.log,
    sleep: clock.sleep,
    now: clock.now,
  });
  assert.equal(calls, 1);
  assert.equal(result.code, 1);
  assert.deepEqual(clock.sleeps, []);
});

test("wait excluded from timeouts", async () => {
  const clock = fakeClock();
  const start = clock.t;
  const budget = createTimeoutBudget(1000, clock.now);
  clock.t += 900;
  assert.equal(budget.expired(), false);
  let calls = 0;
  await runStepWithLimit(async () => (++calls === 1 ? { code: 4, output: marker("primary-graphql", RESET) } : { code: 0, output: "" }), {
    log: clock.log,
    sleep: clock.sleep,
    now: clock.now,
    onWait: (ms) => budget.exclude(ms),
  });
  assert.ok(clock.t - start > 1000);
  assert.equal(budget.expired(), false);
  clock.t += 200;
  assert.equal(budget.expired(), true);
});

test("merge skipped when merged", async () => {
  let merges = 0;
  assert.equal(await mergeUnlessMerged({ state: async () => "MERGED", merge: async () => void merges++ }), "skipped");
  assert.equal(merges, 0);
  assert.equal(await mergeUnlessMerged({ state: async () => "OPEN", merge: async () => void merges++ }), "done");
  assert.equal(merges, 1);
});

test("close skipped when closed", async () => {
  let closes = 0;
  assert.equal(await closeUnlessClosed({ state: async () => "CLOSED", close: async () => void closes++ }), "skipped");
  assert.equal(closes, 0);
  assert.equal(await closeUnlessClosed({ state: async () => "OPEN", close: async () => void closes++ }), "done");
  assert.equal(closes, 1);
});

test("skip comment is once per card", async () => {
  const posted = [];
  const post = async (body) => void posted.push({ body });
  const comments = async () => posted;

  assert.equal(await postOnce({ comments, marker: skipMarker(466), body: "skipped", post }), "done");
  assert.equal(await postOnce({ comments, marker: skipMarker(466), body: "skipped", post }), "skipped");
  assert.equal(await postOnce({ comments, marker: skipMarker(467), body: "skipped", post }), "done");
  assert.equal(posted.length, 2);
  assert.match(posted[0].body, /<!-- skarbiec-autobuild-skip:466 -->/);
});

test("summary posted once", async () => {
  const MARK = "<!-- skarbiec-summary:epic-346 -->";
  const posted = [];
  const post = async (body) => void posted.push(body);
  assert.equal(await postOnce({ comments: async () => [{ body: "hi" }, { body: `summary\n${MARK}` }], marker: MARK, post }), "skipped");
  assert.deepEqual(posted, []);
  assert.equal(await postOnce({ comments: async () => [{ body: "hi" }], marker: MARK, post }), "done");
  assert.equal(posted.length, 1);
  assert.ok(posted[0].includes(MARK));
});
