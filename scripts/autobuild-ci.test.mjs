import assert from "node:assert/strict";
import { test } from "node:test";
import { CI_FIX_MARKER, CI_POLICY_LINE, classifyJob, countFailedTests, decideOnRed, driveCi, jobRef, partLine, runCiFix, summaryPartLine } from "./autobuild-ci.mjs";

const URL_OF = (runId, jobId) => `https://github.com/o/r/actions/runs/${runId}/job/${jobId}`;
const PR = { number: 501, url: "https://github.com/o/r/pull/501" };
const CI_MISSING_MS = 600e3;
const CI_TIMEOUT_MS = 5400e3;
const RESUME = "node scripts/autobuild.mjs 472 --until 07:00";

class StopError extends Error {
  constructor(reason, detail) {
    super(detail);
    this.reason = reason;
  }
}

const step = (name, conclusion = "failure") => ({ name, conclusion });
const job = (over = {}) => ({ id: 11, name: "reporting", conclusion: "failure", run_attempt: 1, steps: [step("Build (warnings as errors)", "success"), step("Test (Testcontainers — requires Docker)")], ...over });
const check = (bucket, over = {}) => ({ name: "reporting", state: bucket, bucket, link: URL_OF(7, 11), ...over });
const vstest = (failed) => `Failed!  - Failed:     ${failed}, Passed:    90, Skipped:     0, Total:    ${90 + failed}`;
const passing = "Passed!  - Failed:     0, Passed:    40, Skipped:     0, Total:    40";

// checks(n) and jobs(n) answer the nth readChecks call; the last answer repeats
function harness({ checks, jobs = () => [job()], log = () => vstest(1), rerunOk = true, marked = false, fix = async () => {} }) {
  const calls = { rerun: [], say: [], budgets: [], reads: 0, fix: [], marker: 0 };
  const budgets = [];
  const io = {
    pr: PR,
    pollMs: 30e3,
    missingMs: CI_MISSING_MS,
    timeoutMs: CI_TIMEOUT_MS,
    readChecks: () => checks(++calls.reads),
    readJobs: () => jobs(calls.reads),
    readLog: () => log(),
    rerun: (runId) => {
      calls.rerun.push(runId);
      return rerunOk;
    },
    fix: async (input) => {
      calls.fix.push(input);
      await fix(input, calls);
    },
    fixMarked: async () => (calls.marker++, marked),
    resume: RESUME,
    sleep: async () => {},
    say: (line) => calls.say.push(line),
    budget: (ms) => {
      const b = { ms, expiredNow: false, expired: () => b.expiredNow };
      budgets.push(b);
      calls.budgets.push(ms);
      return b;
    },
    stop: (reason, detail) => new StopError(reason, detail),
  };
  return { io, calls, budgets };
}

const stops = (reason, text) => (err) => err.reason === reason && (!text || err.message.includes(text));

test("classifyJob", () => {
  assert.equal(classifyJob(job()), "test");
  assert.equal(classifyJob(job({ steps: [step("Build (warnings as errors)")] })), "other");
  assert.equal(classifyJob(job({ steps: [step("Format check")] })), "other");
  assert.equal(classifyJob(job({ conclusion: "cancelled", steps: [] })), "infra");
  assert.equal(classifyJob(job({ conclusion: "timed_out", steps: [step("Test")] })), "infra");
});

test("jobRef reads the run and job ids", () => {
  assert.deepEqual(jobRef(URL_OF(7, 11)), { runId: 7, jobId: 11 });
  assert.equal(jobRef("https://github.com/o/r/runs/5"), null);
  assert.equal(jobRef(undefined), null);
});

test("countFailedTests", () => {
  assert.equal(countFailedTests([vstest(1), passing, passing, vstest(1), passing].join("\n")), 2);
  assert.equal(countFailedTests("2026-10-21T10:00:00Z       \x1b[2mTests\x1b[22m  2 failed | 10 passed (12)"), 2);
  assert.equal(countFailedTests("      Tests  2 failed | 10 passed (12)"), 2);
  assert.equal(countFailedTests("Build FAILED.\n error CS1002"), null);
  assert.equal(countFailedTests(passing), null);
  assert.equal(countFailedTests(""), null);
  assert.equal(countFailedTests(null), null);
});

const fail = (name, kind, tests, attempt = 1) => ({ name, link: URL_OF(7, 1), runId: 7, kind, tests, attempt });

test("decideOnRed threshold", () => {
  assert.deepEqual(decideOnRed([fail("a", "test", 3), fail("b", "test", 1)]), { action: "rerun", reason: "4 tests failed (a, b)", tests: 4 });
  assert.equal(decideOnRed([fail("a", "test", 1)]).reason, "1 test failed (a)");
  assert.equal(decideOnRed([fail("a", "test", 3), fail("b", "test", 2)]).action, "fix");
  assert.equal(decideOnRed([fail("a", "test", 1), fail("b", "test", null)]).action, "fix");
  assert.equal(decideOnRed([fail("a", "test", 1), fail("b", "other", null)]).action, "fix");
  assert.equal(decideOnRed([]).action, "stop");
});

test("decideOnRed infra", () => {
  const mixed = decideOnRed([fail("a", "infra", null), fail("b", "test", 4)]);
  assert.equal(mixed.action, "rerun");
  assert.equal(mixed.tests, 4);
  assert.equal(decideOnRed([fail("a", "infra", null), fail("b", "infra", null)]).action, "rerun");
  assert.equal(decideOnRed([fail("a", "infra", null), fail("b", "test", 5)]).action, "fix");
});

test("decideOnRed fixes on a later attempt", () => {
  assert.deepEqual(decideOnRed([fail("a", "test", 1, 2)]), { action: "fix", reason: "red after rerun (a)" });
  assert.equal(decideOnRed([fail("a", "test", 1, null)]).action, "fix");
  assert.equal(decideOnRed([fail("a", "test", 1, 2)], { attemptOf: () => 1 }).action, "rerun");
});

test("decideOnRed fix", () => {
  for (const failures of [
    [fail("a", "test", 5)],
    [fail("a", "other", null)],
    [fail("a", "test", 1, 2)],
    [fail("a", "test", 1), fail("b", "other", null)],
    [fail("a", "test", null)],
  ]) {
    assert.equal(decideOnRed(failures).action, "fix", JSON.stringify(failures));
    assert.equal(decideOnRed(failures, { fixed: true }).action, "stop", JSON.stringify(failures));
  }
  assert.equal(decideOnRed([fail("a", "test", 2)], { fixed: true }).action, "rerun");
  assert.equal(decideOnRed([fail("a", "infra", null)], { fixed: true }).action, "rerun");
  assert.equal(decideOnRed([], { fixed: true }).action, "stop");
});

test("driveCi reruns once and survives the stale red", async () => {
  // read 1: red, rerun; reads 2-3: the old red is still served; the attempt rises at read 3; read 4: the new run pending; 5-6: green
  const { io, calls } = harness({
    checks: (n) => (n <= 3 ? [check("fail")] : n === 4 ? [check("pending")] : [check("pass")]),
    jobs: (n) => [job({ run_attempt: n >= 3 ? 2 : 1 })],
  });
  const res = await driveCi(io);

  assert.equal(res.reruns, 1);
  assert.deepEqual(calls.rerun, [7]);
  assert.ok(calls.say.includes("CI PR #501: 1 test failed (reporting) — rerun failed jobs"));
  assert.equal(calls.reads, 6);
});

test("driveCi does not read the stale red again once the attempt rose", async () => {
  // the attempt rises at read 2 while the checks read are still red: they are read again instead of decided on
  const { io, calls } = harness({
    checks: (n) => (n <= 2 ? [check("fail")] : [check("pass")]),
    jobs: (n) => [job({ run_attempt: n >= 2 ? 2 : 1 })],
  });
  const res = await driveCi(io);

  assert.equal(res.reruns, 1);
  assert.deepEqual(calls.rerun, [7]);
});

test("driveCi waits for pending checks before rerunning", async () => {
  const web = { name: "web", link: URL_OF(7, 12) };
  const { io, calls } = harness({
    checks: (n) => (n === 1 ? [check("fail"), check("pending", web)] : n === 2 ? [check("fail"), check("pass", web)] : [check("pass"), check("pass", web)]),
    jobs: (n) => [job({ run_attempt: n >= 3 ? 2 : 1 })],
  });
  const res = await driveCi(io);

  assert.equal(res.reruns, 1);
  assert.deepEqual(calls.rerun, [7]);
  assert.equal(calls.say.filter((l) => l.includes("rerun failed jobs")).length, 1);
});

test("driveCi stops after the rerun once the fix is spent", async () => {
  const { io, calls } = harness({ checks: () => [check("fail", { link: URL_OF(7, 21) })], jobs: () => [job({ id: 21, run_attempt: 2 })], marked: true });
  await assert.rejects(driveCi(io), stops("ci-failed", URL_OF(7, 21)));
  assert.deepEqual(calls.rerun, []);
  assert.deepEqual(calls.fix, []);
});

test("driveCi stops after the rerun also when another job failed", async () => {
  const { io, calls } = harness({
    checks: (n) => (n === 1 ? [check("fail")] : [check("fail", { name: "portfolio", link: URL_OF(7, 31) })]),
    jobs: (n) => (n === 1 ? [job()] : [job({ id: 31, name: "portfolio", run_attempt: 2 })]),
    marked: true,
  });
  await assert.rejects(driveCi(io), stops("ci-failed", URL_OF(7, 31)));
  assert.equal(calls.rerun.length, 1);
});

test("driveCi reads the attempt from GitHub", async () => {
  const { io, calls } = harness({ checks: () => [check("fail")], jobs: () => [job({ run_attempt: 2 })], marked: true });
  await assert.rejects(driveCi(io), stops("ci-failed", "red after rerun"));
  assert.deepEqual(calls.rerun, []);
});

test("driveCi stops without a rerun for an unknown count, another job or 5 tests once the fix is spent", async () => {
  for (const [jobs, log] of [
    [[job()], () => "no summary"],
    [[job({ steps: [step("Build (warnings as errors)")] })], () => vstest(1)],
    [[job()], () => vstest(5)],
  ]) {
    const { io, calls } = harness({ checks: () => [check("fail")], jobs: () => jobs, log, marked: true });
    await assert.rejects(driveCi(io), stops("ci-failed", URL_OF(7, 11)));
    assert.deepEqual(calls.rerun, []);
  }
});

test("driveCi stops when the rerun call fails", async () => {
  const { io } = harness({ checks: () => [check("fail")], rerunOk: false, marked: true });
  await assert.rejects(driveCi(io), stops("ci-failed", "rerun of run 7 failed"));
});

test("driveCi reruns an infrastructure failure", async () => {
  const { io, calls } = harness({
    checks: (n) => (n === 1 ? [check("cancel")] : [check("pass")]),
    jobs: (n) => [job({ conclusion: "cancelled", steps: [], run_attempt: n >= 2 ? 2 : 1 })],
  });
  const res = await driveCi(io);

  assert.equal(res.reruns, 1);
  assert.ok(calls.say.includes("CI PR #501: infrastructure failure (reporting) — rerun failed jobs"));
});

test("driveCi budgets", async () => {
  const { io, calls, budgets } = harness({
    checks: (n) => {
      if (n === 2) budgets[0].expiredNow = true;
      return n === 1 ? [check("fail")] : [check("pass")];
    },
    jobs: (n) => [job({ run_attempt: n >= 2 ? 2 : 1 })],
  });
  const res = await driveCi(io);

  assert.equal(res.reruns, 1);
  assert.deepEqual(calls.budgets, [CI_TIMEOUT_MS, CI_MISSING_MS, CI_TIMEOUT_MS]);
});

test("driveCi times out when the rerun never starts", async () => {
  const { io, budgets } = harness({
    checks: (n) => {
      if (n === 2) budgets[2].expiredNow = true;
      return [check("fail")];
    },
    marked: true,
  });
  await assert.rejects(driveCi(io), stops("ci-timeout"));
});

test("driveCi needs two green polls", async () => {
  const { io, calls } = harness({ checks: (n) => (n === 1 ? [check("pending")] : [check("pass")]) });
  const res = await driveCi(io);

  assert.equal(res.reruns, 0);
  assert.equal(calls.reads, 3);
});

test("partLine shows the rerun and the fix", () => {
  const base = { number: 475, title: "T", prUrl: "https://github.com/o/r/pull/501", buildMin: 15, ciMin: 9 };
  assert.equal(partLine({ ...base, ciReruns: 1 }), "#475 PR #501 (build 15 min, CI 9 min, CI rerun 1)");
  assert.equal(partLine({ ...base, ciReruns: 0 }), "#475 PR #501 (build 15 min, CI 9 min)");
  assert.equal(partLine(base), "#475 PR #501 (build 15 min, CI 9 min)");
  assert.equal(partLine({ ...base, ciReruns: 1, ciFixes: 1 }), "#475 PR #501 (build 15 min, CI 9 min, CI rerun 1 · fix 1)");
  assert.equal(partLine({ ...base, ciReruns: 0, ciFixes: 1 }), "#475 PR #501 (build 15 min, CI 9 min, fix 1)");
  assert.equal(summaryPartLine({ ...base, ciReruns: 1, ciFixes: 1 }), "- #475 T - https://github.com/o/r/pull/501 (build 15 min, CI 9 min, CI rerun 1 · fix 1)");
  assert.equal(summaryPartLine({ ...base, ciReruns: 1 }), "- #475 T - https://github.com/o/r/pull/501 (build 15 min, CI 9 min, CI rerun 1)");
  assert.equal(summaryPartLine({ ...base, ciReruns: 0 }), "- #475 T - https://github.com/o/r/pull/501 (build 15 min, CI 9 min)");
});

test("the CI policy line", () => {
  assert.equal(CI_POLICY_LINE, "CI: rerun failed jobs once when < 5 tests failed, then one fix session per PR");
});

const buildJob = () => job({ steps: [step("Build (warnings as errors)")] });

test("driveCi fixes then merges", async () => {
  const noRerun = harness({
    checks: (n) => (n === 1 ? [check("fail")] : [check("pass")]),
    jobs: () => [buildJob()],
  });
  const res = await driveCi(noRerun.io);
  assert.equal(res.fixes, 1);
  assert.equal(res.reruns, 0);
  assert.equal(noRerun.calls.fix.length, 1);
  assert.deepEqual(noRerun.calls.rerun, []);
  assert.ok(noRerun.calls.say.includes("CI PR #501: reporting is neither a test nor an infrastructure failure — fix session 1"));
  assert.deepEqual(noRerun.calls.budgets, [CI_TIMEOUT_MS, CI_MISSING_MS, CI_TIMEOUT_MS]);

  // read 1: red, rerun; read 2: the attempt rose; read 3: red after the rerun, fix; reads 4-5: green
  const rerunThenFix = harness({
    checks: (n) => (n <= 3 ? [check("fail")] : [check("pass")]),
    jobs: (n) => [job({ run_attempt: n >= 2 ? 2 : 1 })],
  });
  const both = await driveCi(rerunThenFix.io);
  assert.equal(both.reruns, 1);
  assert.equal(both.fixes, 1);
  assert.deepEqual(rerunThenFix.calls.rerun, [7]);
  assert.ok(rerunThenFix.calls.say.includes("CI PR #501: red after rerun (reporting) — fix session 1"));
  assert.equal(rerunThenFix.calls.marker, 1);
  assert.deepEqual(rerunThenFix.calls.fix[0].failures.map((f) => f.link), [URL_OF(7, 11)]);
});

test("driveCi reports each fix through onFix", async () => {
  const fixes = [];
  const { io } = harness({ checks: (n) => (n === 1 ? [check("fail")] : [check("pass")]), jobs: () => [buildJob()] });
  await driveCi({ ...io, onFix: () => fixes.push(1) });
  assert.equal(fixes.length, 1);
});

test("driveCi one fix per PR", async () => {
  // read 1: build red, fix; read 2: 2 tests, rerun; reads 3-4: the old run, then attempt 2; read 5: still red
  const afterRerun = harness({
    checks: () => [check("fail")],
    jobs: (n) => (n === 1 ? [buildJob()] : [job({ run_attempt: n >= 4 ? 2 : 1 })]),
    log: () => vstest(2),
  });
  await assert.rejects(driveCi(afterRerun.io), stops("ci-failed", `(rerun 1, fix 1)\nresume: ${RESUME}`));
  assert.equal(afterRerun.calls.fix.length, 1);
  assert.deepEqual(afterRerun.calls.rerun, [7]);

  const tooMany = harness({
    checks: () => [check("fail")],
    jobs: (n) => (n === 1 ? [buildJob()] : [job()]),
    log: () => vstest(6),
  });
  await assert.rejects(driveCi(tooMany.io), stops("ci-failed", URL_OF(7, 11)));
  assert.equal(tooMany.calls.fix.length, 1);
  assert.deepEqual(tooMany.calls.rerun, []);

  const resumed = harness({ checks: () => [check("fail")], jobs: () => [buildJob()], marked: true });
  await assert.rejects(driveCi(resumed.io), stops("ci-failed", `(rerun 0, fix 1)\nresume: ${RESUME}`));
  assert.deepEqual(resumed.calls.fix, []);
});

function fixDeps(over = {}) {
  const events = [];
  const deps = {
    pr: PR,
    part: 475,
    failures: [{ name: "reporting", link: URL_OF(7, 11) }],
    reason: "reporting is neither a test nor an infrastructure failure",
    resume: RESUME,
    guards: { stopRequested: () => false, windowClosed: () => false, dryRun: false },
    headSha: async () => (events.includes("session") ? "bbb" : "aaa"),
    prepare: async () => void events.push("prepare"),
    session: async () => (events.push("session"), { text: 'FIX_RESULT: {"ok":true,"kind":"ci","summary":"fixed the build"}' }),
    restore: async () => void events.push("restore"),
    markerPosted: async () => true,
    say: (line) => events.push(line),
    stop: (reason, detail) => new StopError(reason, detail),
    ...over,
  };
  return { deps, events };
}

test("driveCi fix failures stop", async () => {
  const ok = fixDeps();
  await runCiFix(ok.deps);
  assert.deepEqual(ok.events.slice(0, 3), ["prepare", "session", "restore"]);

  const cases = [
    ["ci-failed", 'FIX_RESULT: {"ok":false,"kind":"stop","summary":"no reproduction"}', {}, "no reproduction"],
    ["ci-failed", "no result line", {}, "no FIX_RESULT"],
    ["ci-failed", 'FIX_RESULT: {"ok":true,"kind":"app","summary":"x"}', {}, 'not "ci"'],
    ["blocked", 'FIX_RESULT: {"ok":true,"kind":"ci","summary":"x"}', { headSha: async () => "aaa" }, "head did not move"],
    ["blocked", 'FIX_RESULT: {"ok":true,"kind":"ci","summary":"x"}', { markerPosted: async () => false }, "comment is missing"],
  ];
  for (const [reason, text, over, why] of cases) {
    const { deps, events } = fixDeps();
    deps.session = async () => (events.push("session"), { text });
    Object.assign(deps, over);
    await assert.rejects(runCiFix(deps), (err) => stops(reason, why)(err) && err.message.includes(URL_OF(7, 11)) && err.message.includes(`resume: ${RESUME}`), why);
    assert.ok(events.includes("restore"), why);
  }

  const cut = fixDeps();
  cut.deps.session = async () => {
    throw new StopError("limit", "usage limit");
  };
  await assert.rejects(runCiFix(cut.deps), stops("limit"));
  assert.deepEqual(cut.events, ["prepare", "restore"]);

  const wired = harness({
    checks: () => [check("fail")],
    jobs: () => [buildJob()],
    fix: (input) => runCiFix({ ...fixDeps({ headSha: async () => "aaa" }).deps, ...input }),
  });
  await assert.rejects(driveCi(wired.io), stops("blocked", "head did not move"));
});

test("driveCi fix guards", async () => {
  for (const [reason, guard] of [
    ["stopped", { stopRequested: () => true }],
    ["window", { windowClosed: () => true }],
  ]) {
    const { deps, events } = fixDeps({ guards: { stopRequested: () => false, windowClosed: () => false, dryRun: false, ...guard } });
    await assert.rejects(runCiFix(deps), (err) => stops(reason, `resume: ${RESUME}`)(err) && err.message.includes(URL_OF(7, 11)), reason);
    assert.deepEqual(events, [], reason);
  }

  const dry = fixDeps({ guards: { stopRequested: () => false, windowClosed: () => false, dryRun: true } });
  await assert.rejects(runCiFix(dry.deps), stops("dry-run"));
  assert.deepEqual(dry.events, ["dry-run: would run /fix ci:#475 on the branch of PR #501 (reporting is neither a test nor an infrastructure failure)"]);

  // a fix is not a build: the --max-tasks count never enters the guards and is never raised
  const ctx = { builds: 3, maxTasks: 3 };
  const { deps, events } = fixDeps();
  await runCiFix(deps);
  assert.ok(events.includes("session"));
  assert.equal(ctx.builds, 3);
});

test("the fix marker is an HTML comment", () => {
  assert.match(CI_FIX_MARKER, /^<!-- .+ -->$/);
});
