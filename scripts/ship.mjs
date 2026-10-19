#!/usr/bin/env node
// Usage: node scripts/ship.mjs <issue> (--from-run | --branch <b> --title <t> --json '<run report>' [--blocked]) [--dry-run]
//   --from-run  branch, title, report and --blocked come from the newest build-feature workflow record of <issue>
//   --blocked   only post the report, no git; --dry-run prints the commands and runs the read-only guards only
//   a shipping run first needs the tree proven green: node scripts/verify.mjs --all --cache-check
//   an e2e part ("e2e": true in --json) instead needs node scripts/verify.mjs --projects e2e --cache-check and a green node scripts/e2e.mjs check
//   --blocked at stage e2e also saves the uncommitted diff to skarbiec-plan/issues/<issue>.e2e.patch, restores the tree and switches to master
//   after a successful ship it rebuilds the local compose images from the shipped commit (result.images; never fails the ship)
//   last stdout line: SHIP_RESULT: {...}; exit 0 ok, 2 failed, 4 GitHub limit exhausted (stderr: reset time and a GITHUB_LIMIT line; rerun after the reset)

import { spawnSync } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { latestRunRecord } from "./build-replay.mjs";
import { GH_LIMIT_EXIT, GhLimitError, limitFromGhFailure, limitFromText, limitText, spawnGh } from "./gh.mjs";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const REPO = "Maliniak93/FinMel";
const BOOLEAN_FLAGS = new Set(["blocked", "dry-run", "from-run"]);
const VERIFY_ARGV = ["scripts/verify.mjs", "--cache-check"];
const VERIFY_ALL_ARGV = ["scripts/verify.mjs", "--all", "--cache-check"];
const E2E_VERIFY_ARGV = ["scripts/verify.mjs", "--projects", "e2e", "--cache-check"];
const E2E_CHECK_ARGV = ["scripts/e2e.mjs", "check"];
const NOT_GREEN = "the tree was not proven green by verify.mjs (cache miss)";
const NOT_E2E_GREEN = "the e2e gate failed: no green whole-suite e2e run of this tree";

function run(cmd, argv) {
  const res = cmd === "gh" ? spawnGh(argv) : spawnSync(cmd, argv, { cwd: REPO_ROOT, encoding: "utf8", windowsHide: true, shell: false });
  const err = res.error?.message ?? (res.stderr ?? "").trim();
  const out = {
    ok: !res.error && res.status === 0,
    out: (res.stdout ?? "").trim(),
    firstErr: err.split(/\r?\n/).find(Boolean) ?? "",
    all: `${res.stdout ?? ""}\n${res.stderr ?? ""}`,
  };
  if (!out.ok && !res.error) throwOnLimit(cmd, argv, res, out.all);
  return out;
}

function throwOnLimit(cmd, argv, res, all) {
  if (cmd === "gh") {
    const limit = limitFromGhFailure(res.stderr);
    if (limit) throw limit;
  } else if (argv[0]?.endsWith("gh-project.mjs") && res.status === GH_LIMIT_EXIT) {
    const limit = limitFromText(all);
    if (limit) throw new GhLimitError(limit.kind, limit.resetAt);
  }
}

function parseFlags(argv) {
  const flags = {};
  for (let i = 0; i < argv.length; i++) {
    if (!argv[i].startsWith("--")) throw new Error(`unexpected argument: ${argv[i]}`);
    const key = argv[i].slice(2);
    flags[key] = BOOLEAN_FLAGS.has(key) ? true : argv[++i];
  }
  return flags;
}

let issue;
let result = { ok: false };
let exitCode = 2;

function finish() {
  console.log(`SHIP_RESULT: ${JSON.stringify(result)}`);
  process.exit(exitCode);
}

function verifyArgv({ e2e, lastBuild }) {
  if (e2e) return E2E_VERIFY_ARGV;
  return lastBuild ? VERIFY_ALL_ARGV : VERIFY_ARGV;
}

function checkVerified(gate) {
  const argv = verifyArgv(gate);
  const res = run(process.execPath, argv);
  const line = res.all.split(/\r?\n/).reverse().find((l) => l.startsWith("VERIFY_RESULT: "));
  let parsed = null;
  try {
    parsed = JSON.parse(line.slice("VERIFY_RESULT: ".length));
  } catch {
  }
  if (res.ok && parsed?.ok === true) return { ok: true };
  return {
    ok: false,
    error: parsed
      ? `${NOT_GREEN}; run node scripts/verify.mjs ${gate.e2e ? "--projects e2e " : gate.lastBuild ? "--all " : ""}--fix --cache first`
      : `${NOT_GREEN}; node ${argv.join(" ")} gave no result: ${res.firstErr}`,
  };
}

function checkE2e() {
  const res = run(process.execPath, E2E_CHECK_ARGV);
  const line = res.all.split(/\r?\n/).reverse().find((l) => l.startsWith("E2E_CHECK: "));
  let parsed = null;
  try {
    parsed = JSON.parse(line.slice("E2E_CHECK: ".length));
  } catch {
  }
  if (res.ok && parsed?.ok === true) return { ok: true, total: parsed.total, flaky: parsed.flaky };
  return { ok: false, error: `${NOT_E2E_GREEN}; ${parsed?.error ?? `node ${E2E_CHECK_ARGV.join(" ")} gave no result: ${res.firstErr}`}` };
}

function checkGate(gate) {
  const verified = checkVerified(gate);
  if (!gate.e2e) return verified;
  const suite = checkE2e();
  if (verified.ok && suite.ok) return suite;
  return { ok: false, error: [verified.ok ? null : verified.error, suite.ok ? null : suite.error].filter(Boolean).join("; ") };
}

export function gateCommand(gate) {
  return `node ${verifyArgv(gate).join(" ")}${gate.e2e ? ` && node ${E2E_CHECK_ARGV.join(" ")}` : ""}`;
}

function readCard() {
  const res = run(process.execPath, ["scripts/gh-project.mjs", "get", issue]);
  if (!res.ok) throw new Error(`gh-project.mjs get ${issue} failed: ${res.firstErr}`);
  return JSON.parse(res.out.split(/\r?\n/).pop());
}

function outsideOf(area) {
  if (!area) return [];
  const res = run(process.execPath, ["scripts/areas.mjs", "outside", area]);
  if (!res.ok) throw new Error(`areas.mjs outside ${area} failed: ${res.firstErr}`);
  return res.out.split(/\r?\n/).filter(Boolean);
}

function blockedE2e(issue, branch, report, dryRun) {
  const patch = `skarbiec-plan/issues/${issue}.e2e.patch`;
  const withPatch = { ...report, status: "blocked", patch };
  const cleanup = [
    ["git", ["reset", "--hard", "HEAD"]],
    ["git", ["clean", "-fd"]],
    ["git", ["switch", "master"]],
  ];
  if (dryRun) {
    console.log(`e2e blocked (${report.class ?? "?"}): ${report.reason ?? ""}`);
    for (const f of report.failed ?? []) console.log(`  test: ${f.test} | step: ${f.step ?? "?"} | trace: ${f.trace ?? "none"}`);
    if (report.reportPath) console.log(`  report: ${report.reportPath}`);
    console.log(`would write: ${patch} (the uncommitted diff, untracked files included)`);
    console.log(`would post: gh-project.mjs report ${issue} --json ${JSON.stringify(withPatch)}`);
    for (const [cmd, argv] of cleanup) console.log(`would run: ${cmd} ${argv.join(" ")}`);
    return { ok: true, dryRun: true, branch, patch };
  }
  const current = run("git", ["rev-parse", "--abbrev-ref", "HEAD"]);
  if (!current.ok || current.out !== branch || !/^(feat|fix|chore)\//.test(branch)) {
    return { ok: false, branch, failedCommand: "guard", error: `current branch is ${current.out}, expected ${branch}: the tree was left as it is` };
  }
  const diff = spawnSync(process.execPath, ["scripts/review-diff.mjs"], { cwd: REPO_ROOT, encoding: "utf8", windowsHide: true, shell: false, maxBuffer: 256 * 1024 * 1024 });
  if (diff.error || diff.status !== 0) return { ok: false, branch, failedCommand: "review-diff.mjs", error: diff.error?.message ?? (diff.stderr ?? "").trim() };
  const patchFile = path.join(REPO_ROOT, patch);
  mkdirSync(path.dirname(patchFile), { recursive: true });
  writeFileSync(patchFile, diff.stdout);
  const posted = postReport(withPatch);
  if (!posted.ok) return { ok: false, branch, patch, reportPosted: false, failedCommand: "gh-project.mjs report", error: posted.error };
  for (const [cmd, argv] of cleanup) {
    const res = run(cmd, argv);
    if (!res.ok) return { ok: false, branch, patch, reportPosted: true, failedCommand: `${cmd} ${argv.join(" ")}`, error: res.firstErr };
  }
  return { ok: true, branch, patch, reportPosted: true, cleaned: true };
}

function buildImages(ref) {
  const res = spawnSync(process.execPath, ["scripts/compose.mjs", "build", "--ref", ref], {
    cwd: REPO_ROOT,
    encoding: "utf8",
    windowsHide: true,
    shell: false,
    maxBuffer: 64 * 1024 * 1024,
  });
  const line = (res.stdout ?? "").split(/\r?\n/).reverse().find((l) => l.startsWith("COMPOSE_RESULT: "));
  try {
    const parsed = JSON.parse(line.slice("COMPOSE_RESULT: ".length));
    return parsed.ok ? { ok: true, revision: parsed.revision } : { ok: false, error: parsed.error };
  } catch {
    const firstErr = (res.stderr ?? "").trim().split(/\r?\n/).find(Boolean);
    return { ok: false, error: res.error?.message ?? (firstErr || "compose.mjs build gave no result") };
  }
}

function postReport(report) {
  const res = run(process.execPath, ["scripts/gh-project.mjs", "report", issue, "--json", JSON.stringify(report)]);
  return res.ok ? { ok: true } : { ok: false, error: res.firstErr };
}

function fromRun(issue) {
  const record = latestRunRecord({ issue });
  if (!record) throw new Error(`--from-run: no finished build-feature run of #${issue} with a ship report in the Claude Code history`);
  return { branch: record.args.branch, title: record.args.title, json: JSON.stringify(record.result.ship.report), blocked: Boolean(record.result.ship.blocked) };
}

function main() {
  const [first, ...rest] = process.argv.slice(2);
  issue = first;
try {
  if (!/^\d+$/.test(issue ?? "")) throw new Error("usage: ship.mjs <issue> (--from-run | --branch <b> --title <t> --json '<run report>' [--blocked]) [--dry-run]");
  const flags = parseFlags(rest);
  if (flags["from-run"]) Object.assign(flags, fromRun(issue));
  for (const required of ["branch", "title", "json"]) if (!flags[required]) throw new Error(`--${required} is required`);
  const { branch, title } = flags;
  const report = JSON.parse(flags.json);
  result = { ok: false, branch };

  const e2e = Boolean(report.e2e);
  let gate = { e2e, lastBuild: false };

  if (flags.blocked && report.stage === "e2e") {
    result = blockedE2e(issue, branch, report, Boolean(flags["dry-run"]));
    exitCode = result.ok ? 0 : 2;
    finish();
  }

  if (flags.blocked) {
    const posted = postReport({ ...report, status: "blocked" });
    result = { ok: posted.ok, branch, reportPosted: posted.ok, ...(posted.ok ? {} : { failedCommand: "gh-project.mjs report", error: posted.error }) };
    exitCode = posted.ok ? 0 : 2;
    finish();
  }

  const card = readCard();
  gate = { e2e, lastBuild: Boolean(card.lastBuild) };
  const outsideArea = outsideOf(card.area);
  if (outsideArea.length) report.outsideArea = outsideArea;

  const body = `Spec: #${issue} - the build run report is on the issue.\n\n🤖 Generated with [Claude Code](https://claude.com/claude-code)`;
  const steps = [
    { label: "git add -A", cmd: "git", argv: ["add", "-A"] },
    { label: "git commit", cmd: "git", argv: ["commit", "-m", title, "-m", `Spec: #${issue}`, "-m", "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"] },
    { label: "git push", cmd: "git", argv: ["push", "-u", "origin", branch] },
    { label: "gh pr create", cmd: "gh", argv: ["pr", "create", "--repo", REPO, "--base", "master", "--head", branch, "--title", title, "--body", body] },
  ];

  const current = run("git", ["rev-parse", "--abbrev-ref", "HEAD"]);
  let failed = null;
  if (!current.ok) failed = { command: "git rev-parse --abbrev-ref HEAD", error: current.firstErr };
  else if (current.out !== branch) failed = { command: "guard", error: `current branch is ${current.out}, expected ${branch}` };
  else if (!/^(feat|fix|chore)\//.test(branch) || ["master", "main"].includes(branch)) failed = { command: "guard", error: `refusing to ship branch ${branch}` };

  if (flags["dry-run"]) {
    const existing = run("gh", ["pr", "list", "--repo", REPO, "--head", branch, "--state", "open", "--json", "url", "--limit", "1"]);
    const existingUrl = existing.ok ? JSON.parse(existing.out || "[]")[0]?.url : null;
    if (existingUrl) console.log(`an open PR exists and will be reused: ${existingUrl}`);
    if (failed) console.log(`guard failed: ${failed.error}`);
    console.log(`would run: ${gateCommand(gate)}`);
    if (!failed) {
      const verified = checkGate(gate);
      console.log(verified.ok ? `${e2e ? "verify and e2e gate" : "verify cache"}: green` : `${e2e ? "gate" : "verify cache"}: ${verified.error}`);
      if (!verified.ok) failed = { command: gateCommand(gate), error: verified.error };
    }
    for (const s of steps) console.log(`would run: ${s.cmd} ${s.argv.map((a) => (/[\s"]/.test(a) ? JSON.stringify(a) : a)).join(" ")}`);
    console.log("would run: node scripts/compose.mjs build --ref <sha>");
    for (const m of report.manualTest ?? []) console.log(`would replace: ## Test manualny in #${m.issue} from ${m.file}`);
    console.log(`would post: gh-project.mjs report ${issue} --json ${JSON.stringify({ ...report, status: "shipped" })}`);
    result = { ok: !failed, dryRun: true, branch, ...(failed ? { failedCommand: failed.command, error: failed.error } : {}) };
    exitCode = failed ? 2 : 0;
    finish();
  }

  if (!failed) {
    const verified = checkGate(gate);
    if (!verified.ok) {
      const posted = postReport({ deviations: report.deviations, status: "blocked", stage: "verify", reason: e2e ? NOT_E2E_GREEN : NOT_GREEN });
      result = { ok: false, branch, reportPosted: posted.ok, failedCommand: gateCommand(gate), error: verified.error };
      exitCode = 2;
      finish();
    }
    if (e2e) report.e2e = { passed: verified.total, total: verified.total, flaky: verified.flaky };
  }

  let prUrl = null;
  if (!failed) {
    for (const s of steps) {
      const res = run(s.cmd, s.argv);
      if (s.label === "git commit" && !res.ok) {
        const ahead = run("git", ["rev-list", "--count", "origin/master..HEAD"]);
        if (/nothing to commit|nothing added to commit/i.test(res.all) && ahead.ok && Number(ahead.out) > 0) continue;
      }
      if (s.label === "gh pr create") {
        if (res.ok) prUrl = res.out.split(/\r?\n/).pop();
        else if (/already exists/i.test(res.all)) {
          const view = run("gh", ["pr", "view", branch, "--repo", REPO, "--json", "url"]);
          if (view.ok) {
            prUrl = JSON.parse(view.out).url;
            continue;
          }
        }
      }
      if (!res.ok) {
        failed = { command: s.label, error: res.firstErr };
        break;
      }
    }
  }
  const sha = run("git", ["rev-parse", "--short", "HEAD"]);
  const fullSha = run("git", ["rev-parse", "HEAD"]);

  const posted = failed
    ? postReport({ deviations: report.deviations, status: "blocked", stage: "ship", reason: `${failed.command}: ${failed.error}` })
    : postReport({ ...report, status: "shipped" });
  result = {
    ok: !failed && posted.ok,
    branch,
    commit: sha.ok ? sha.out : null,
    prUrl,
    ...(e2e && typeof report.e2e === "object" ? { e2e: report.e2e } : {}),
    reportPosted: posted.ok,
    ...(failed ? { failedCommand: failed.command, error: failed.error } : posted.ok ? {} : { failedCommand: "gh-project.mjs report", error: posted.error }),
  };
  exitCode = result.ok ? 0 : 2;

  if (!failed && posted.ok) {
    result.images = fullSha.ok ? buildImages(fullSha.out) : { ok: false, error: "git rev-parse HEAD failed" };
  }
} catch (err) {
  if (err instanceof GhLimitError) {
    console.error(limitText(err).trimEnd());
    result = { ...result, ok: false, error: `GitHub limit (${err.kind})`, limit: { kind: err.kind, resetAt: err.resetAt } };
    exitCode = GH_LIMIT_EXIT;
  } else {
    result = { ...result, ok: false, error: err.message };
    exitCode = 2;
  }
}
finish();
}

if (process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1])) main();
