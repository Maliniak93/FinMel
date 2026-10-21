#!/usr/bin/env node
// Usage: node scripts/ci-digest.mjs <pr> [--lines N]  digest of the failed CI jobs on a PR's head: job, failed step, log tail of that step, failed test names
//   --lines N  log lines kept per job (default 200); the tail ends at the last ##[error] line, `unknown` marks an unreadable log
//   a /fix ci:#<n> session reads this instead of the raw logs; REST only, one call per fact

import path from "node:path";
import { fileURLToPath } from "node:url";
import { jobRef } from "./autobuild-ci.mjs";
import { GhLimitError, REPO, exitOnGhLimit, gh } from "./gh.mjs";

export const DEFAULT_LINES = 200;
const FAILED = new Set(["failure", "cancelled", "timed_out"]);
const ANSI = /\x1b\[[0-9;]*m/g;
const STAMP = /^\d{4}-\d\d-\d\dT[\d:.]+Z ?/;

const cleanLines = (log) => String(log ?? "").replace(ANSI, "").split(/\r?\n/).map((l) => l.replace(STAMP, ""));

export function tailOf(log, count = DEFAULT_LINES) {
  const lines = cleanLines(log);
  const lastError = lines.findLastIndex((l) => l.startsWith("##[error]"));
  const end = lastError >= 0 ? lastError + 1 : lines.length;
  return lines.slice(Math.max(0, end - count), end).join("\n").trim();
}

export function parseFailedTests(log) {
  const names = new Set();
  for (const line of cleanLines(log)) {
    const vstest = /^\s*Failed\s+(\S+)(?:\s+\[[^\]]*\])?\s*$/.exec(line);
    if (vstest) names.add(vstest[1]);
    const vitest = /^\s*(?:FAIL|×)\s+(.+?)(?:\s+\d+ms)?\s*$/.exec(line);
    if (vitest) names.add(vitest[1]);
  }
  return [...names];
}

export const failedStepOf = (job) => (job?.steps ?? []).find((s) => s.conclusion === "failure")?.name ?? null;

export function digestJob({ check, job, log }, count = DEFAULT_LINES) {
  const readable = typeof log === "string" && log.trim() !== "";
  return {
    name: check.name,
    link: check.link,
    conclusion: job?.conclusion ?? check.conclusion ?? "unknown",
    attempt: job?.run_attempt ?? null,
    step: failedStepOf(job) ?? "unknown",
    tests: readable ? parseFailedTests(log) : "unknown",
    tail: readable ? tailOf(log, count) : "unknown",
  };
}

export function formatDigest(pr, digests) {
  if (!digests.length) return `PR #${pr}: no failed job on the head commit`;
  const blocks = digests.map((d) => {
    const tests = d.tests === "unknown" ? "unknown" : d.tests.length ? `${d.tests.length}\n${d.tests.map((t) => `  - ${t}`).join("\n")}` : "none parsed";
    return [`## ${d.name} — ${d.conclusion}${d.attempt ? `, attempt ${d.attempt}` : ""}`, `link: ${d.link}`, `failed step: ${d.step}`, `failed tests: ${tests}`, "log tail:", "```", d.tail, "```"].join("\n");
  });
  return [`PR #${pr}: ${digests.length} failed job(s) on the head commit`, "", ...blocks].join("\n\n");
}

export async function buildDigest({ pr, readHead, readChecks, readJobs, readLog, lines = DEFAULT_LINES }) {
  const head = await readHead(pr);
  const failed = (await readChecks(head)).filter((c) => FAILED.has(c.conclusion));
  const jobsByRun = new Map();
  const digests = [];
  for (const run of failed) {
    const check = { name: run.name, link: run.html_url, conclusion: run.conclusion };
    const ref = jobRef(check.link);
    let job = null;
    let log = null;
    if (ref) {
      if (!jobsByRun.has(ref.runId)) jobsByRun.set(ref.runId, await readJobs(ref.runId));
      job = (jobsByRun.get(ref.runId) ?? []).find((j) => j.id === ref.jobId) ?? null;
      log = await readLog(ref.jobId);
    }
    digests.push(digestJob({ check, job, log }, lines));
  }
  return digests;
}

function ghOrNull(argv) {
  try {
    return gh(argv);
  } catch (err) {
    if (err instanceof GhLimitError) throw err;
    return null;
  }
}

async function main() {
  const [first, ...rest] = process.argv.slice(2);
  const pr = String(first ?? "").replace(/^#/, "");
  const linesFlag = rest[0] === "--lines" ? Number(rest[1]) : DEFAULT_LINES;
  if (!/^\d+$/.test(pr) || !Number.isInteger(linesFlag) || linesFlag < 1) {
    console.error("usage: ci-digest.mjs <pr> [--lines N]");
    process.exit(2);
  }
  try {
    const digests = await buildDigest({
      pr,
      lines: linesFlag,
      readHead: (n) => gh(["api", `repos/${REPO}/pulls/${n}`, "--jq", ".head.sha"]),
      readChecks: (sha) => JSON.parse(gh(["api", `repos/${REPO}/commits/${sha}/check-runs?per_page=100`, "--jq", ".check_runs"])),
      readJobs: (runId) => {
        const out = ghOrNull(["api", `repos/${REPO}/actions/runs/${runId}/jobs?per_page=100`, "--jq", ".jobs"]);
        return out ? JSON.parse(out) : [];
      },
      readLog: (jobId) => ghOrNull(["api", `repos/${REPO}/actions/jobs/${jobId}/logs`]),
    });
    console.log(formatDigest(pr, digests));
  } catch (err) {
    if (err instanceof GhLimitError) exitOnGhLimit(err);
    console.error(err.message);
    process.exit(2);
  }
}

if (process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1])) main();
