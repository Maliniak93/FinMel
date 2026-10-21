import assert from "node:assert/strict";
import { test } from "node:test";
import { BUILD_LIMIT_MS, HANG_MS, buildThroughHangs, createAwakeClock, createHangDetector, createHangPolicy, hasWorkProcess, isWorkProcess, summaryLines } from "./autobuild-watch.mjs";

const MIN = 60e3;

function fakeTime(start = new Date(2026, 9, 20, 1, 0, 0).getTime()) {
  let t = start;
  return { now: () => t, advance: (ms) => void (t += ms), at: (h, m) => new Date(2026, 9, 20, h, m, 0).getTime(), set: (v) => void (t = v) };
}

function minutes(time, clock, count, each = async () => {}) {
  return (async () => {
    for (let i = 0; i < count; i++) {
      time.advance(MIN);
      clock.tick();
      await each();
    }
  })();
}

test("clock detects a slept gap and formats the line", () => {
  const time = fakeTime();
  time.set(time.at(2, 8));
  const clock = createAwakeClock({ now: time.now });
  time.advance(MIN);
  assert.equal(clock.tick(), null);
  assert.equal(clock.sleptMs(), 0);

  time.set(time.at(5, 7));
  const gap = clock.tick();
  assert.equal(gap.line, "PC spał 02:09–05:07 (178 min)");
  assert.equal(gap.ms, 178 * MIN);
  assert.equal(clock.sleptMs(), 178 * MIN);

  time.advance(MIN);
  assert.equal(clock.tick(), null);
  assert.equal(clock.sleptMs(), 178 * MIN);
});

test("build limit counts awake time only", async () => {
  const time = fakeTime();
  const clock = createAwakeClock({ now: time.now });
  await minutes(time, clock, 166);
  time.advance(178 * MIN);
  assert.ok(clock.tick());
  assert.equal(clock.expired(BUILD_LIMIT_MS), false);
  assert.equal(Math.round(clock.awakeMs() / MIN), 166);

  await minutes(time, clock, 74);
  assert.equal(clock.expired(BUILD_LIMIT_MS), false);
  await minutes(time, clock, 2);
  assert.equal(Math.round(clock.awakeMs() / MIN), 242);
  assert.equal(clock.expired(BUILD_LIMIT_MS), true);
});

const snapshot = { phase: "build", agent: "implementer", tokens: 120000, tools: 40 };

function detectorWith({ hasWork } = {}) {
  const time = fakeTime();
  const clock = createAwakeClock({ now: time.now });
  const detector = createHangDetector({ clock, hasWork });
  return { time, clock, detector };
}

test("detector hangs after 45 idle minutes", async () => {
  const { time, clock, detector } = detectorWith();
  detector.progress(snapshot);
  await minutes(time, clock, 44);
  assert.equal((await detector.check()).hung, false);
  time.advance(MIN);
  clock.tick();
  assert.deepEqual(await detector.check(), { hung: true, phase: "build", agent: "implementer", tokens: 120000, tools: 40 });
  assert.equal(HANG_MS, 45 * MIN);
});

test("progress resets idle", async () => {
  const { time, clock, detector } = detectorWith();
  detector.progress(snapshot);
  await minutes(time, clock, 40, () => detector.check());
  detector.progress({ ...snapshot, tokens: 130000 });
  await minutes(time, clock, 40, () => detector.check());
  assert.equal((await detector.check()).hung, false);

  detector.progress({ ...snapshot, tokens: 130000, tools: 41 });
  await minutes(time, clock, 40, () => detector.check());
  detector.agentEvent();
  await minutes(time, clock, 40, () => detector.check());
  assert.equal((await detector.check()).hung, false);

  detector.progress({ ...snapshot, tokens: 130000, tools: 41 });
  await minutes(time, clock, 10, () => detector.check());
  assert.equal((await detector.check()).hung, true);
});

test("sleep is not idle time", async () => {
  const { time, clock, detector } = detectorWith();
  detector.progress(snapshot);
  await minutes(time, clock, 30, () => detector.check());
  time.advance(178 * MIN);
  assert.ok(clock.tick());
  assert.equal((await detector.check()).hung, false);
  assert.ok(detector.idleMs() <= 31 * MIN);
  await minutes(time, clock, 10, () => detector.check());
  assert.equal((await detector.check()).hung, false);
  await minutes(time, clock, 6, () => detector.check());
  assert.equal((await detector.check()).hung, true);
});

const proc = (pid, parentPid, name, commandLine = "") => ({ pid, parentPid, name, commandLine });
const claudeTree = (...children) => [proc(1, 0, "node.exe", "launcher"), proc(10, 1, "claude.exe", "claude -p /build"), ...children];

test("work process holds the timer", async () => {
  let tree = claudeTree(proc(20, 10, "node.exe", "node C:\\repo\\scripts\\verify.mjs --fix"), proc(21, 20, "dotnet.exe", "dotnet test"));
  const { time, clock, detector } = detectorWith({ hasWork: async () => hasWorkProcess(tree, 10) });
  detector.progress(snapshot);
  await minutes(time, clock, 10, () => detector.check());
  const held = detector.idleMs();
  await minutes(time, clock, 120, () => detector.check());
  assert.equal(detector.idleMs(), held);
  assert.equal((await detector.check()).hung, false);

  tree = claudeTree();
  await minutes(time, clock, 20, () => detector.check());
  assert.equal((await detector.check()).hung, false);
  await minutes(time, clock, 21, () => detector.check());
  assert.equal((await detector.check()).hung, true);
});

test("mcp servers do not hold the timer", async () => {
  const tree = claudeTree(
    proc(30, 10, "node.exe", "node C:\\Users\\x\\AppData\\npm\\node_modules\\@playwright\\mcp\\cli.js --headless"),
    proc(31, 10, "node.exe", "node C:\\tools\\context7-mcp\\index.js"),
    proc(32, 10, "powershell.exe", "powershell -Command Get-CimInstance"),
    proc(33, 10, "claude.exe", "claude --helper"),
    proc(34, 10, "dotnet.exe", "dotnet C:\\sdk\\MSBuild.dll /nodeReuse:true"),
  );
  assert.equal(hasWorkProcess(tree, 10), false);
  assert.equal(isWorkProcess(proc(40, 10, "node.exe", "node scripts/e2e.mjs run")), true);
  assert.equal(isWorkProcess(proc(41, 10, "node.exe", "node node_modules/vitest/vitest.mjs")), true);
  assert.equal(isWorkProcess(proc(42, 10, "testhost.exe", "testhost.exe --port 1")), true);
  assert.equal(isWorkProcess(proc(43, 10, "VSTest.Console.exe", "vstest.console.exe")), true);
  assert.equal(hasWorkProcess(claudeTree(proc(50, 99, "dotnet.exe", "dotnet test")), 10), false);

  const { time, clock, detector } = detectorWith({ hasWork: async () => hasWorkProcess(tree, 10) });
  detector.progress(snapshot);
  await minutes(time, clock, 45, () => detector.check());
  assert.equal((await detector.check()).hung, true);
});

test("first hang restarts and second stops", () => {
  const policy = createHangPolicy();
  assert.deepEqual(policy.decide(478), { action: "restart", reason: "hang" });
  assert.deepEqual(policy.decide(478), { action: "stop", reason: "hang" });
  assert.equal(policy.restarts(), 1);
});

test("hang counter is per part", () => {
  const policy = createHangPolicy();
  assert.equal(policy.decide(478).action, "restart");
  assert.equal(policy.decide(479).action, "restart");
  assert.equal(policy.decide(479).action, "stop");
  assert.equal(policy.decide(478).action, "stop");
  assert.equal(policy.restarts(), 2);
});

test("restart kills then stops the stack then replays", async () => {
  const events = [];
  const logs = [];
  const results = [{ hung: { phase: "build", agent: "implementer", tokens: 120000, tools: 40 } }, { code: 0 }];
  const result = await buildThroughHangs({
    part: { number: 478 },
    policy: createHangPolicy(),
    build: async () => {
      const next = results.shift();
      events.push(next.hung ? "build+kill" : "replay");
      return next;
    },
    stopStack: async () => void events.push("stop-stack"),
    resumeFrom: () => "implementer",
    log: (line) => logs.push(line),
  });
  assert.deepEqual(events, ["build+kill", "stop-stack", "replay"]);
  assert.equal(result.code, 0);
  assert.equal(logs[0], "zawieszenie: brak postępu 45 min (build · implementer · 120k tokens · 40 tools) — restart #478");
  assert.match(logs[1], /wznawia od implementer/);

  const again = [{ hung: snapshot }, { hung: snapshot }];
  const stopped = await buildThroughHangs({ part: { number: 479 }, policy: createHangPolicy(), build: async () => again.shift(), stopStack: async () => {}, log: () => {} });
  assert.equal(stopped.hangStop, true);
  assert.equal(again.length, 0);
});

test("summary lists restarts, sleep and guard warning", () => {
  const lines = summaryLines({ restarts: 1, sleptMs: 178 * MIN, guardWarning: "UWAGA: blokada snu nie działa (no ready)" });
  assert.deepEqual(lines, ["hang restarts: 1", "PC spał łącznie 178 min", "UWAGA: blokada snu nie działa (no ready)"]);
  assert.deepEqual(summaryLines({ restarts: 0, sleptMs: 0, guardWarning: null }), []);
});
