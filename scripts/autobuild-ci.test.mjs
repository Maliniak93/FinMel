import assert from "node:assert/strict";
import { test } from "node:test";
import { CI_POLICY_LINE, classifyJob, countFailedTests, decideOnRed, driveCi, jobRef, partLine, summaryPartLine } from "./autobuild-ci.mjs";

const URL_OF = (runId, jobId) => `https://github.com/o/r/actions/runs/${runId}/job/${jobId}`;
const PR = { number: 501, url: "https://github.com/o/r/pull/501" };
const CI_MISSING_MS = 600e3;
const CI_TIMEOUT_MS = 5400e3;

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
function harness({ checks, jobs = () => [job()], log = () => vstest(1), rerunOk = true }) {
  const calls = { rerun: [], say: [], budgets: [], reads: 0 };
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
  assert.equal(decideOnRed([fail("a", "test", 3), fail("b", "test", 2)]).action, "stop");
  assert.equal(decideOnRed([fail("a", "test", 1), fail("b", "test", null)]).action, "stop");
  assert.equal(decideOnRed([fail("a", "test", 1), fail("b", "other", null)]).action, "stop");
  assert.equal(decideOnRed([]).action, "stop");
});

test("decideOnRed infra", () => {
  const mixed = decideOnRed([fail("a", "infra", null), fail("b", "test", 4)]);
  assert.equal(mixed.action, "rerun");
  assert.equal(mixed.tests, 4);
  assert.equal(decideOnRed([fail("a", "infra", null), fail("b", "infra", null)]).action, "rerun");
  assert.equal(decideOnRed([fail("a", "infra", null), fail("b", "test", 5)]).action, "stop");
});

test("decideOnRed stops on a later attempt", () => {
  assert.equal(decideOnRed([fail("a", "test", 1, 2)]).action, "stop");
  assert.equal(decideOnRed([fail("a", "test", 1, null)]).action, "stop");
  assert.equal(decideOnRed([fail("a", "test", 1, 2)], { attemptOf: () => 1 }).action, "rerun");
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

test("driveCi stops after the rerun", async () => {
  const { io, calls } = harness({ checks: () => [check("fail", { link: URL_OF(7, 21) })], jobs: () => [job({ id: 21, run_attempt: 2 })] });
  await assert.rejects(driveCi(io), stops("ci-failed", URL_OF(7, 21)));
  assert.deepEqual(calls.rerun, []);
});

test("driveCi stops after the rerun also when another job failed", async () => {
  const { io, calls } = harness({
    checks: (n) => (n === 1 ? [check("fail")] : [check("fail", { name: "portfolio", link: URL_OF(7, 31) })]),
    jobs: (n) => (n === 1 ? [job()] : [job({ id: 31, name: "portfolio", run_attempt: 2 })]),
  });
  await assert.rejects(driveCi(io), stops("ci-failed", URL_OF(7, 31)));
  assert.equal(calls.rerun.length, 1);
});

test("driveCi reads the attempt from GitHub", async () => {
  const { io, calls } = harness({ checks: () => [check("fail")], jobs: () => [job({ run_attempt: 2 })] });
  await assert.rejects(driveCi(io), stops("ci-failed", "already ran twice"));
  assert.deepEqual(calls.rerun, []);
});

test("driveCi stops without a rerun for an unknown count, another job or 5 tests", async () => {
  for (const [jobs, log] of [
    [[job()], () => "no summary"],
    [[job({ steps: [step("Build (warnings as errors)")] })], () => vstest(1)],
    [[job()], () => vstest(5)],
  ]) {
    const { io, calls } = harness({ checks: () => [check("fail")], jobs: () => jobs, log });
    await assert.rejects(driveCi(io), stops("ci-failed", URL_OF(7, 11)));
    assert.deepEqual(calls.rerun, []);
  }
});

test("driveCi stops when the rerun call fails", async () => {
  const { io } = harness({ checks: () => [check("fail")], rerunOk: false });
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
  });
  await assert.rejects(driveCi(io), stops("ci-timeout"));
});

test("driveCi needs two green polls", async () => {
  const { io, calls } = harness({ checks: (n) => (n === 1 ? [check("pending")] : [check("pass")]) });
  const res = await driveCi(io);

  assert.equal(res.reruns, 0);
  assert.equal(calls.reads, 3);
});

test("partLine shows the rerun", () => {
  const base = { number: 475, title: "T", prUrl: "https://github.com/o/r/pull/501", buildMin: 15, ciMin: 9 };
  assert.equal(partLine({ ...base, ciReruns: 1 }), "#475 PR #501 (build 15 min, CI 9 min, CI rerun 1)");
  assert.equal(partLine({ ...base, ciReruns: 0 }), "#475 PR #501 (build 15 min, CI 9 min)");
  assert.equal(partLine(base), "#475 PR #501 (build 15 min, CI 9 min)");
  assert.equal(summaryPartLine({ ...base, ciReruns: 1 }), "- #475 T - https://github.com/o/r/pull/501 (build 15 min, CI 9 min, CI rerun 1)");
  assert.equal(summaryPartLine({ ...base, ciReruns: 0 }), "- #475 T - https://github.com/o/r/pull/501 (build 15 min, CI 9 min)");
});

test("the CI policy line", () => {
  assert.equal(CI_POLICY_LINE, "CI: rerun failed jobs once when < 5 tests failed");
});
