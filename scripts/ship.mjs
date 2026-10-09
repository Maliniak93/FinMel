#!/usr/bin/env node
// Usage: node scripts/ship.mjs <issue> --branch <b> --title <t> --json '<run report>' [--blocked] [--dry-run]
//   --blocked   only post the report, no git; --dry-run prints the commands and runs the read-only guards only
//   a shipping run first needs the tree proven green: node scripts/verify.mjs --all --cache-check
//   after a successful ship it rebuilds the local compose images from the shipped commit (result.images; never fails the ship)
//   last stdout line: SHIP_RESULT: {...}; exit 0 ok, 2 failed

import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const REPO = "Maliniak93/FinMel";
const BOOLEAN_FLAGS = new Set(["blocked", "dry-run"]);
const VERIFY_ARGV = ["scripts/verify.mjs", "--all", "--cache-check"];
const VERIFY_COMMAND = `node ${VERIFY_ARGV.join(" ")}`;
const NOT_GREEN = "the tree was not proven green by verify.mjs (cache miss)";

function run(cmd, argv) {
  const res = spawnSync(cmd, argv, { cwd: REPO_ROOT, encoding: "utf8", windowsHide: true, shell: false });
  const err = res.error?.message ?? (res.stderr ?? "").trim();
  return {
    ok: !res.error && res.status === 0,
    out: (res.stdout ?? "").trim(),
    firstErr: err.split(/\r?\n/).find(Boolean) ?? "",
    all: `${res.stdout ?? ""}\n${res.stderr ?? ""}`,
  };
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

const [issue, ...rest] = process.argv.slice(2);
let result = { ok: false };
let exitCode = 2;

function finish() {
  console.log(`SHIP_RESULT: ${JSON.stringify(result)}`);
  process.exit(exitCode);
}

function checkVerified() {
  const res = run(process.execPath, VERIFY_ARGV);
  const line = res.all.split(/\r?\n/).reverse().find((l) => l.startsWith("VERIFY_RESULT: "));
  let parsed = null;
  try {
    parsed = JSON.parse(line.slice("VERIFY_RESULT: ".length));
  } catch {
  }
  if (res.ok && parsed?.ok === true) return { ok: true };
  return {
    ok: false,
    error: parsed ? `${NOT_GREEN}; run node scripts/verify.mjs --all --fix --cache first` : `${NOT_GREEN}; ${VERIFY_COMMAND} gave no result: ${res.firstErr}`,
  };
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

try {
  if (!/^\d+$/.test(issue ?? "")) throw new Error("usage: ship.mjs <issue> --branch <b> --title <t> --json '<run report>' [--blocked] [--dry-run]");
  const flags = parseFlags(rest);
  for (const required of ["branch", "title", "json"]) if (!flags[required]) throw new Error(`--${required} is required`);
  const { branch, title } = flags;
  const report = JSON.parse(flags.json);
  result = { ok: false, branch };

  if (flags.blocked) {
    const posted = postReport({ ...report, status: "blocked" });
    result = { ok: posted.ok, branch, reportPosted: posted.ok, ...(posted.ok ? {} : { failedCommand: "gh-project.mjs report", error: posted.error }) };
    exitCode = posted.ok ? 0 : 2;
    finish();
  }

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
    if (failed) console.log(`guard failed: ${failed.error}`);
    console.log(`would run: ${VERIFY_COMMAND}`);
    if (!failed) {
      const verified = checkVerified();
      console.log(verified.ok ? "verify cache: green" : `verify cache: ${verified.error}`);
      if (!verified.ok) failed = { command: VERIFY_COMMAND, error: verified.error };
    }
    for (const s of steps) console.log(`would run: ${s.cmd} ${s.argv.map((a) => (/[\s"]/.test(a) ? JSON.stringify(a) : a)).join(" ")}`);
    console.log("would run: node scripts/compose.mjs build --ref <sha>");
    console.log(`would post: gh-project.mjs report ${issue} --json ${JSON.stringify({ ...report, status: "shipped" })}`);
    result = { ok: !failed, dryRun: true, branch, ...(failed ? { failedCommand: failed.command, error: failed.error } : {}) };
    exitCode = failed ? 2 : 0;
    finish();
  }

  if (!failed) {
    const verified = checkVerified();
    if (!verified.ok) {
      const posted = postReport({ deviations: report.deviations, status: "blocked", stage: "verify", reason: NOT_GREEN });
      result = { ok: false, branch, reportPosted: posted.ok, failedCommand: VERIFY_COMMAND, error: verified.error };
      exitCode = 2;
      finish();
    }
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
    reportPosted: posted.ok,
    ...(failed ? { failedCommand: failed.command, error: failed.error } : posted.ok ? {} : { failedCommand: "gh-project.mjs report", error: posted.error }),
  };
  exitCode = result.ok ? 0 : 2;

  if (!failed && posted.ok) {
    result.images = fullSha.ok ? buildImages(fullSha.out) : { ok: false, error: "git rev-parse HEAD failed" };
  }
} catch (err) {
  result = { ...result, ok: false, error: err.message };
  exitCode = 2;
}
finish();
