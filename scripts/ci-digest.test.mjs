import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import { buildDigest, digestJob, failedStepOf, formatDigest, parseFailedTests, tailOf } from "./ci-digest.mjs";

const SCRIPT = path.join(path.dirname(fileURLToPath(import.meta.url)), "ci-digest.mjs");
const URL_OF = (runId, jobId) => `https://github.com/o/r/actions/runs/${runId}/job/${jobId}`;
const stamp = (line) => `2026-10-21T10:00:00.1234567Z ${line}`;

const VSTEST_LOG = [
  "Starting test execution",
  "  Failed Skarbiec.Reporting.Tests.Dashboard.Total_Sums [412 ms]",
  "  Error Message:",
  "   Assert.Equal() Failure",
  "  Failed Skarbiec.Reporting.Tests.Dashboard.Total_Empty [3 ms]",
  "Failed!  - Failed:     2, Passed:    90, Skipped:     0, Total:    92",
  "##[error]Process completed with exit code 1.",
  "Post job cleanup.",
].map(stamp).join("\n");

const job = (over = {}) => ({ id: 11, name: "reporting", conclusion: "failure", run_attempt: 1, steps: [{ name: "Build", conclusion: "success" }, { name: "Test (Testcontainers)", conclusion: "failure" }], ...over });
const run = (over = {}) => ({ name: "reporting", conclusion: "failure", html_url: URL_OF(7, 11), ...over });

test("tailOf ends at the last error and keeps the last lines", () => {
  const lines = Array.from({ length: 300 }, (_, i) => stamp(`line ${i}`));
  lines.push(stamp("##[error]boom"), stamp("Post job cleanup."));
  const tail = tailOf(lines.join("\n"), 200).split("\n");
  assert.equal(tail.length, 200);
  assert.equal(tail.at(-1), "##[error]boom");
  assert.equal(tail[0], "line 101");
  assert.equal(tailOf("a\nb\nc", 2), "b\nc");
  assert.equal(tailOf("\x1b[31mred\x1b[0m"), "red");
  assert.equal(tailOf(null), "");
});

test("parseFailedTests reads VSTest and Vitest names once", () => {
  assert.deepEqual(parseFailedTests(VSTEST_LOG), ["Skarbiec.Reporting.Tests.Dashboard.Total_Sums", "Skarbiec.Reporting.Tests.Dashboard.Total_Empty"]);
  const vitest = [" FAIL  src/app/total.spec.ts > Total > sums 12ms", " FAIL  src/app/total.spec.ts > Total > sums 12ms", "      Tests  1 failed | 10 passed (11)"].join("\n");
  assert.deepEqual(parseFailedTests(vitest), ["src/app/total.spec.ts > Total > sums"]);
  assert.deepEqual(parseFailedTests("Build FAILED.\n error CS1002"), []);
  assert.deepEqual(parseFailedTests(undefined), []);
});

test("failedStepOf names the first failed step", () => {
  assert.equal(failedStepOf(job()), "Test (Testcontainers)");
  assert.equal(failedStepOf(job({ steps: [] })), null);
  assert.equal(failedStepOf(null), null);
});

test("digestJob marks an unreadable log unknown", () => {
  const check = { name: "reporting", link: URL_OF(7, 11) };
  const readable = digestJob({ check, job: job(), log: VSTEST_LOG });
  assert.equal(readable.step, "Test (Testcontainers)");
  assert.equal(readable.tests.length, 2);
  assert.ok(readable.tail.endsWith("##[error]Process completed with exit code 1."));

  for (const log of [null, "", "   "]) {
    const unreadable = digestJob({ check, job: job(), log });
    assert.equal(unreadable.tests, "unknown");
    assert.equal(unreadable.tail, "unknown");
  }
  assert.equal(digestJob({ check, job: null, log: null }).step, "unknown");
});

test("buildDigest reads the head, the failed checks, their jobs and logs", async () => {
  const calls = { jobs: [], logs: [] };
  const digests = await buildDigest({
    pr: "501",
    readHead: async () => "abc",
    readChecks: async (sha) => {
      assert.equal(sha, "abc");
      return [run(), run({ name: "web", conclusion: "success", html_url: URL_OF(7, 12) }), run({ name: "portfolio", conclusion: "cancelled", html_url: URL_OF(7, 13) }), run({ name: "odd", html_url: "https://example.com/x" })];
    },
    readJobs: async (runId) => (calls.jobs.push(runId), [job(), job({ id: 13, name: "portfolio", conclusion: "cancelled", steps: [] })]),
    readLog: async (jobId) => (calls.logs.push(jobId), jobId === 11 ? VSTEST_LOG : null),
  });

  assert.deepEqual(calls.jobs, [7]);
  assert.deepEqual(calls.logs, [11, 13]);
  assert.deepEqual(digests.map((d) => [d.name, d.step, d.conclusion]), [
    ["reporting", "Test (Testcontainers)", "failure"],
    ["portfolio", "unknown", "cancelled"],
    ["odd", "unknown", "failure"],
  ]);
  assert.equal(digests[1].tests, "unknown");
  assert.equal(digests[2].tail, "unknown");
});

test("formatDigest prints job, step, tests and the log tail", () => {
  const check = { name: "reporting", link: URL_OF(7, 11) };
  const text = formatDigest("501", [digestJob({ check, job: job(), log: VSTEST_LOG }), digestJob({ check: { name: "web", link: URL_OF(7, 12) }, job: null, log: null })]);
  assert.match(text, /^PR #501: 2 failed job\(s\)/);
  assert.match(text, /## reporting — failure, attempt 1/);
  assert.ok(text.includes(`link: ${URL_OF(7, 11)}`));
  assert.ok(text.includes("failed step: Test (Testcontainers)"));
  assert.ok(text.includes("  - Skarbiec.Reporting.Tests.Dashboard.Total_Sums"));
  assert.ok(text.includes("failed tests: unknown"));
  assert.equal(formatDigest("501", []), "PR #501: no failed job on the head commit");
});

test("the CLI needs a PR number", () => {
  const res = spawnSync(process.execPath, [SCRIPT], { encoding: "utf8", windowsHide: true });
  assert.equal(res.status, 2);
  assert.match(res.stderr, /usage: ci-digest\.mjs <pr>/);
});
