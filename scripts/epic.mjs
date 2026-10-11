#!/usr/bin/env node
// Usage: node scripts/epic.mjs <epic> [--dry-run] [--max-tasks N] [--threshold 90] [--no-shutdown]
//   --dry-run      print what would happen per part and the limits; no preflight script, builds, merges, comments or shutdown
//   --max-tasks N  stop before the (N+1)th build of this run (0 = preflight only)
//   --threshold P  stop before a build when the 5h or weekly limit is at P % or more (100 skips the probe)
//   --no-shutdown  do not schedule `shutdown /s /t 600` when the run ends
//   node scripts/epic.mjs <epic> --stop   creates the stop file; the running driver stops before the next build
//   last stdout line: EPIC_RESULT: {...}; exit 0 when done (or a dry run is fine), 2 otherwise, 130 on Ctrl+C

import { spawn, spawnSync } from "node:child_process";
import { createWriteStream, existsSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const REPO = "Maliniak93/FinMel";
const IS_WIN = process.platform === "win32";
const BOOLEAN_FLAGS = new Set(["dry-run", "no-shutdown", "stop"]);
const BUILD_TIMEOUT_MS = 4 * 3600e3;
const CI_POLL_MS = 30e3;
const CI_MISSING_MS = 10 * 60e3;
const CI_TIMEOUT_MS = 90 * 60e3;
const NODE = process.execPath;

class Stop extends Error {
  constructor(reason, detail) {
    super(detail);
    this.reason = reason;
  }
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const minutes = (ms) => Math.round(ms / 600) / 100;
const local = (epochSeconds) => new Date(epochSeconds * 1000).toLocaleString();

function run(cmd, argv, options = {}) {
  const res = spawnSync(cmd, argv, { cwd: REPO_ROOT, encoding: "utf8", windowsHide: true, shell: false, maxBuffer: 64 * 1024 * 1024, ...options });
  const err = res.error?.message ?? (res.stderr ?? "").trim();
  return { ok: !res.error && res.status === 0, out: (res.stdout ?? "").trim(), err, firstErr: err.split(/\r?\n/).find(Boolean) ?? "" };
}

const gh = (argv) => run("gh", argv);
const git = (argv) => run("git", argv);
const project = (argv) => run(NODE, ["scripts/gh-project.mjs", ...argv]);

function parseFlags(argv) {
  const flags = {};
  for (let i = 0; i < argv.length; i++) {
    if (!argv[i].startsWith("--")) throw new Error(`unexpected argument: ${argv[i]}`);
    const key = argv[i].slice(2);
    flags[key] = BOOLEAN_FLAGS.has(key) ? true : argv[++i];
  }
  return flags;
}

const [epicArg, ...rest] = process.argv.slice(2);
const stopFile = (epic) => path.join(os.tmpdir(), `skarbiec-epic-${epic}.stop`);

function getIssue(number) {
  const res = project(["get", String(number)]);
  if (!res.ok) throw new Stop("github", `gh-project.mjs get ${number}: ${res.firstErr}`);
  return JSON.parse(res.out);
}

function openPr(branch) {
  const res = gh(["pr", "list", "--repo", REPO, "--head", branch, "--state", "open", "--json", "number,url", "--limit", "1"]);
  if (!res.ok) throw new Stop("github", `gh pr list: ${res.firstErr}`);
  return JSON.parse(res.out)[0] ?? null;
}

function resolveClaude() {
  if (!IS_WIN) return { cmd: "claude", shell: false };
  const found = run("where", ["claude"]).out.split(/\r?\n/).filter(Boolean);
  const exe = found.find((p) => /\.exe$/i.test(p));
  if (exe) return { cmd: exe, shell: false };
  // Node refuses to spawn a .cmd shim without a shell
  if (found.length) return { cmd: found[0], shell: true };
  throw new Stop("no-claude", "the claude CLI is not on PATH");
}

function claudeSpawn(claude, argv) {
  return { cmd: claude.cmd, argv: claude.shell ? argv.map((a) => (/\s/.test(a) ? `"${a}"` : a)) : argv, shell: claude.shell };
}

function probeLimits(claude) {
  const spec = claudeSpawn(claude, ["-p", "ok", "--model", "haiku", "--max-turns", "1", "--output-format", "stream-json", "--verbose"]);
  const res = spawnSync(spec.cmd, spec.argv, { cwd: os.tmpdir(), encoding: "utf8", windowsHide: true, shell: spec.shell, timeout: 180e3, maxBuffer: 64 * 1024 * 1024 });
  const lines = (res.stdout ?? "").split(/\r?\n/).filter((l) => l.includes('"rate_limit_event"'));
  for (const line of lines.reverse()) {
    try {
      const windows = JSON.parse(line).rate_limit_info?.unifiedWindows;
      if (windows?.five_hour && windows?.seven_day) {
        return {
          fiveHour: { utilization: windows.five_hour.utilization, resetsAt: windows.five_hour.resetsAt },
          sevenDay: { utilization: windows.seven_day.utilization, resetsAt: windows.seven_day.resetsAt },
        };
      }
    } catch {
    }
  }
  return null;
}

const percent = (w) => `${Math.round(w.utilization * 100)}%`;
const describeLimits = (l) => `5h ${percent(l.fiveHour)} (resets ${local(l.fiveHour.resetsAt)}) · 7d ${percent(l.sevenDay)} (resets ${local(l.sevenDay.resetsAt)})`;

function checkLimits(claude, threshold) {
  if (threshold >= 100) return null;
  const limits = probeLimits(claude);
  if (!limits) throw new Stop("limits-unknown", "Claude Code no longer reports rate_limit_event; pass --threshold 100 to ignore");
  console.log(`limits: ${describeLimits(limits)}`);
  if (limits.fiveHour.utilization >= threshold / 100 || limits.sevenDay.utilization >= threshold / 100) {
    throw new Stop("limit", `usage at or above ${threshold}%: ${describeLimits(limits)}`);
  }
  return limits;
}

let sleepGuard = null;

function startSleepGuard() {
  if (!IS_WIN) return;
  // 0x80000001 = ES_CONTINUOUS | ES_SYSTEM_REQUIRED
  const script = [
    "$sig = '[DllImport(\"kernel32.dll\")] public static extern uint SetThreadExecutionState(uint esFlags);'",
    "Add-Type -MemberDefinition $sig -Name Power -Namespace Skarbiec | Out-Null",
    "[Skarbiec.Power]::SetThreadExecutionState([uint32]2147483649) | Out-Null",
    `Wait-Process -Id ${process.pid} -ErrorAction SilentlyContinue`,
  ].join("; ");
  sleepGuard = spawn("powershell.exe", ["-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command", script], { windowsHide: true, stdio: "ignore" });
  sleepGuard.on("error", () => {});
  console.log("sleep blocked while the run lasts (closing the lid still sleeps)");
}

function killTree(child) {
  if (!child || child.exitCode !== null) return;
  if (IS_WIN) spawnSync("taskkill", ["/pid", String(child.pid), "/T", "/F"], { windowsHide: true });
  else child.kill("SIGKILL");
}

process.on("exit", () => killTree(sleepGuard));

let buildChild = null;

function stamp(number, startedAt) {
  const elapsed = Math.floor((Date.now() - startedAt) / 60e3);
  return `[#${number} ${String(Math.floor(elapsed / 60)).padStart(2, "0")}:${String(elapsed % 60).padStart(2, "0")}]`;
}

function toolHint(name, input = {}) {
  const text = input.command ?? input.description ?? input.name ?? input.file_path ?? input.pattern ?? input.prompt ?? "";
  return `${name} ${String(text).replace(/\s+/g, " ").slice(0, 80)}`.trim();
}

function reportEvent(obj, say, seen) {
  if (obj.type === "assistant") {
    const nested = obj.parent_tool_use_id ? "  " : "";
    for (const block of obj.message?.content ?? []) if (block.type === "tool_use") say(`${nested}${toolHint(block.name, block.input)}`);
  } else if (obj.type === "system" && /permission/i.test(obj.subtype ?? "")) {
    say(`PERMISSION DENIED ${JSON.stringify(obj).slice(0, 200)}`);
  } else if (obj.type === "rate_limit_event") {
    const w = obj.rate_limit_info?.unifiedWindows;
    const text = w?.five_hour && w?.seven_day ? `limits 5h ${percent(w.five_hour)} · 7d ${percent(w.seven_day)}` : null;
    if (text && text !== seen.limits) say(text);
    if (text) seen.limits = text;
  } else if (obj.type === "result") {
    const denials = obj.permission_denials?.length ? ` · ${obj.permission_denials.length} permission denial(s)` : "";
    say(`result ${obj.subtype} · ${obj.num_turns} turns · $${(obj.total_cost_usd ?? 0).toFixed(2)} · ${minutes(obj.duration_ms ?? 0)} min${denials}`);
  }
}

function build(claude, epic, number) {
  const startedAt = Date.now();
  const stamped = new Date().toISOString().replace(/[-:]/g, "").replace("T", "-").slice(0, 15);
  const dir = path.join(REPO_ROOT, "skarbiec-plan", "epic-runs", String(epic));
  mkdirSync(dir, { recursive: true });
  const logPath = path.join(dir, `${number}-${stamped}.jsonl`);
  const log = createWriteStream(logPath);
  const say = (text) => console.log(`${stamp(number, startedAt)} ${text}`);
  const seen = { limits: null };
  console.log(`building #${number} - log ${path.relative(REPO_ROOT, logPath)}`);

  const spec = claudeSpawn(claude, ["-p", `/build #${number}`, "--model", "haiku", "--permission-mode", "auto", "--output-format", "stream-json", "--verbose"]);
  return new Promise((resolve) => {
    const child = spawn(spec.cmd, spec.argv, {
      cwd: REPO_ROOT,
      env: { ...process.env, CLAUDE_CODE_PRINT_BG_WAIT_CEILING_MS: "14400000" },
      windowsHide: true,
      shell: spec.shell,
      stdio: ["ignore", "pipe", "pipe"],
    });
    buildChild = child;
    let buffer = "";
    let errTail = "";
    let timedOut = false;
    const timer = setTimeout(() => {
      timedOut = true;
      killTree(child);
    }, BUILD_TIMEOUT_MS);

    const handleLine = (line) => {
      if (!line.trim()) return;
      log.write(`${line}\n`);
      try {
        reportEvent(JSON.parse(line), say, seen);
      } catch {
      }
    };
    child.stdout.setEncoding("utf8");
    child.stdout.on("data", (chunk) => {
      buffer += chunk;
      const lines = buffer.split(/\r?\n/);
      buffer = lines.pop();
      lines.forEach(handleLine);
    });
    child.stderr.setEncoding("utf8");
    child.stderr.on("data", (chunk) => {
      errTail = (errTail + chunk).slice(-2000);
    });
    child.on("error", (err) => {
      errTail = err.message;
    });
    child.on("close", (code) => {
      clearTimeout(timer);
      handleLine(buffer);
      log.end();
      buildChild = null;
      if (code !== 0 && errTail.trim()) say(`exit ${code}: ${errTail.trim().split(/\r?\n/).slice(-3).join(" | ")}`);
      resolve({ code, timedOut, ms: Date.now() - startedAt });
    });
  });
}

function readChecks(pr) {
  const res = gh(["pr", "checks", String(pr), "--repo", REPO, "--json", "name,state,bucket,link"]);
  try {
    return JSON.parse(res.out);
  } catch {
    return [];
  }
}

async function waitForCi(pr) {
  const startedAt = Date.now();
  let emptySince = Date.now();
  let lastStatus = "";
  let greenPolls = 0;
  while (true) {
    const checks = readChecks(pr.number);
    if (checks.length) emptySince = null;
    else emptySince ??= Date.now();
    const count = (...buckets) => checks.filter((c) => buckets.includes(c.bucket)).length;
    const failed = checks.filter((c) => ["fail", "cancel"].includes(c.bucket));
    const status = `pass ${count("pass")} · skipping ${count("skipping")} · pending ${count("pending")} · fail ${failed.length}`;
    if (checks.length && status !== lastStatus) console.log(`CI PR #${pr.number}: ${status}`);
    lastStatus = status;

    if (failed.length) throw new Stop("ci-failed", `PR ${pr.url}: ${failed.map((c) => `${c.name} (${c.link})`).join(", ")}`);
    if (emptySince && Date.now() - emptySince > CI_MISSING_MS) throw new Stop("ci-missing", `PR ${pr.url}: no checks after 10 min - the runner did not pick up the job`);
    greenPolls = checks.length && checks.every((c) => ["pass", "skipping"].includes(c.bucket)) ? greenPolls + 1 : 0;
    // two green polls in a row: a late-registered check must not slip past the merge
    if (greenPolls >= 2) return Date.now() - startedAt;
    if (Date.now() - startedAt > CI_TIMEOUT_MS) throw new Stop("ci-timeout", `PR ${pr.url}: checks not green after 90 min`);
    await sleep(CI_POLL_MS);
  }
}

async function mergeAndSync(pr, branch, number) {
  const merged = gh(["pr", "merge", String(pr.number), "--repo", REPO, "--squash", "--delete-branch"]);
  if (!merged.ok) {
    const view = gh(["pr", "view", String(pr.number), "--repo", REPO, "--json", "state"]);
    if (!view.ok || JSON.parse(view.out).state !== "MERGED") throw new Stop("merge-failed", `PR ${pr.url}: ${merged.firstErr}`);
  }
  console.log(`merged PR #${pr.number}`);
  for (const argv of [["switch", "master"], ["pull", "--ff-only", "origin", "master"]]) {
    const step = git(argv);
    if (!step.ok) throw new Stop("sync-failed", `git ${argv.join(" ")}: ${step.firstErr}`);
  }
  git(["branch", "-D", branch]);
  for (let attempt = 0; attempt < 6; attempt++) {
    if (getIssue(number).state === "CLOSED") return;
    await sleep(10e3);
  }
  gh(["issue", "close", String(number), "--repo", REPO, "--reason", "completed"]);
}

async function handlePart(sub, ctx) {
  const part = getIssue(sub.number);
  if (!part.branch) throw new Stop("no-branch", `#${part.number} has no Branch field`);
  const entry = { number: part.number, title: part.title, prUrl: null, buildMin: null, ciMin: null };
  let pr = openPr(part.branch);

  if (!pr) {
    if (existsSync(stopFile(ctx.epic))) throw new Stop("stopped", "stop file found");
    if (ctx.builds >= ctx.maxTasks) throw new Stop("max-tasks", `${ctx.maxTasks} build(s) done in this run`);
    ctx.limits = checkLimits(ctx.claude, ctx.threshold);
    ctx.builds++;
    const built = await build(ctx.claude, ctx.epic, part.number);
    entry.buildMin = minutes(built.ms);
    if (built.timedOut) throw new Stop("timeout", `#${part.number}: build exceeded 4 h and was killed`);
    pr = openPr(part.branch);
    if (!pr) throw new Stop("blocked", `#${part.number}: no PR after the build (exit ${built.code}) - see the report on the issue`);
  }

  entry.prUrl = pr.url;
  ctx.parts.push(entry);
  entry.ciMin = minutes(await waitForCi(pr));
  await mergeAndSync(pr, part.branch, part.number);
}

function runnerOnline() {
  const res = gh(["api", `repos/${REPO}/actions/runners`, "--jq", '[.runners[] | select(.status == "online")] | length']);
  return res.ok ? Number(res.out) : 0;
}

function preChecks(epic, flags) {
  if (!flags["dry-run"]) {
    const pre = run(NODE, ["scripts/preflight.mjs"], { timeout: 30 * 60e3 });
    const line = pre.out.split(/\r?\n/).reverse().find((l) => l.startsWith("PREFLIGHT_RESULT: "));
    let parsed = null;
    try {
      parsed = JSON.parse(line.slice("PREFLIGHT_RESULT: ".length));
    } catch {
    }
    if (!parsed?.ok) throw new Stop("preflight", `preflight.mjs reported problems:\n${pre.out.split(/\r?\n/).slice(-15).join("\n")}`);
  }
  const issue = getIssue(epic);
  if (!issue.epic) throw new Stop("not-epic", `#${epic} is not an epic`);
  const dirty = git(["status", "--porcelain"]).out.split(/\r?\n/).filter(Boolean);
  if (dirty.length) {
    const listed = `the working tree is not clean:\n${dirty.slice(0, 10).join("\n")}${dirty.length > 10 ? `\n... ${dirty.length - 10} more` : ""}`;
    if (!flags["dry-run"]) throw new Stop("dirty-tree", listed);
    console.log(`warning: ${listed}`);
  }
  const runners = runnerOnline();
  if (!runners) throw new Stop("runner-offline", "no self-hosted runner is online - the WSL runner is down, see skarbiec-plan/runbooks/ci.md");
  return { issue, runners };
}

function dryRunTable(issue) {
  let first = true;
  for (const sub of issue.subIssues) {
    let action = "skip (closed)";
    if (sub.state !== "CLOSED") {
      if (!first) action = "waits";
      else {
        first = false;
        const branch = getIssue(sub.number).branch;
        const pr = branch ? openPr(branch) : null;
        action = !branch ? "stop: no Branch field" : pr ? `CI → merge PR ${pr.url}` : "build";
      }
    }
    console.log(`#${sub.number} ${sub.title} — ${sub.state} — ${action}`);
  }
}

function summary(reason, detail, parts, epic, costLine) {
  const lines = [`## Epic run: ${reason}`, ""];
  lines.push(parts.length ? "Parts handled in this run:" : "No part was merged in this run.");
  for (const p of parts) lines.push(`- #${p.number} ${p.title} - ${p.prUrl ?? "no PR"} (build ${p.buildMin ?? "-"} min, CI ${p.ciMin ?? "-"} min)`);
  if (detail) lines.push("", "Detail:", "", "```", detail, "```");
  if (costLine) lines.push("", costLine);
  lines.push("", `Resume with \`node scripts/epic.mjs ${epic}\`.`);
  return lines.join("\n");
}

function costLine(parts) {
  const cells = parts.map((p) => {
    const res = run(NODE, ["scripts/run-cost.mjs", "--issue", String(p.number), "--json"]);
    try {
      return `#${p.number} $${JSON.parse(res.out).summary.cost.toFixed(2)}`;
    } catch {
      return null;
    }
  });
  const known = cells.filter(Boolean);
  return known.length ? `Cost at list prices: ${known.join(" · ")}` : null;
}

function postComment(epic, body) {
  const file = path.join(os.tmpdir(), `skarbiec-epic-${epic}-comment.md`);
  writeFileSync(file, body, "utf8");
  const res = project(["comment", String(epic), "--body-file", file]);
  rmSync(file, { force: true });
  return res.ok ? null : res.firstErr;
}

function finishRun(reason, detail, ctx) {
  const ok = reason === "done" || reason === "dry-run";
  console.log(`\nepic #${ctx.epic ?? "?"}: ${reason}${detail ? `\n${detail}` : ""}`);
  const result = { ok, epic: ctx.epic, reason, ...(detail ? { detail } : {}), parts: ctx.parts, ...(ctx.dryRun ? { dryRun: true } : {}), ...(ctx.limits ? { limits: ctx.limits } : {}) };
  if (!ctx.dryRun && ctx.started) {
    const failure = postComment(ctx.epic, summary(reason, detail, ctx.parts, ctx.epic, ok ? costLine(ctx.parts) : null));
    if (failure) {
      result.commentError = failure;
      console.log(`could not post the epic comment: ${failure}`);
    }
    if (!ctx.flags["no-shutdown"] && IS_WIN) {
      run("shutdown", ["/s", "/t", "600", "/c", `Skarbiec epic #${ctx.epic}: ${reason}`]);
      console.log("Shutting down in 10 min — cancel with: shutdown /a");
    }
  }
  console.log(`EPIC_RESULT: ${JSON.stringify(result)}`);
  process.exit(ok ? 0 : 2);
}

async function runLoop(ctx) {
  while (true) {
    const issue = getIssue(ctx.epic);
    if (issue.state === "CLOSED") return;
    if (!issue.subIssues.length) throw new Stop("no-parts", `#${ctx.epic} has no sub-issues`);
    const next = issue.subIssues.find((s) => s.state !== "CLOSED");
    if (!next) break;
    await handlePart(next, ctx);
  }
  gh(["issue", "close", String(ctx.epic), "--repo", REPO, "--reason", "completed"]);
  const moved = project(["set", String(ctx.epic), "Status", "Done"]);
  if (!moved.ok) console.log(`could not move the epic card to Done: ${moved.firstErr}`);
}

const ctx = { epic: /^[0-9]+$/.test(epicArg ?? "") ? Number(epicArg) : null, parts: [], builds: 0, limits: null, started: false, dryRun: false, flags: {}, threshold: 90, maxTasks: Infinity, claude: null };

process.on("SIGINT", () => {
  killTree(buildChild);
  console.log("\ninterrupted (Ctrl+C) - no comment, no shutdown");
  process.exit(130);
});

try {
  if (!/^\d+$/.test(epicArg ?? "")) throw new Stop("usage", "usage: epic.mjs <epic> [--dry-run] [--max-tasks N] [--threshold 90] [--no-shutdown] | --stop");
  const flags = parseFlags(rest);
  ctx.flags = flags;
  ctx.dryRun = Boolean(flags["dry-run"]);
  if (flags.threshold !== undefined) ctx.threshold = Number(flags.threshold);
  if (flags["max-tasks"] !== undefined) ctx.maxTasks = Number(flags["max-tasks"]);
  if (!(ctx.threshold > 0 && ctx.threshold <= 100) || Number.isNaN(ctx.maxTasks)) throw new Stop("usage", "--threshold must be 1..100 and --max-tasks a number");

  if (flags.stop) {
    writeFileSync(stopFile(ctx.epic), new Date().toISOString());
    console.log(`stop file written (${stopFile(ctx.epic)}) - the driver stops before the next build`);
    process.exit(0);
  }

  ctx.claude = resolveClaude();
  const { issue, runners } = preChecks(ctx.epic, flags);
  if (issue.state === "CLOSED") finishRun("done", "the epic is already closed - nothing to do", { ...ctx, started: false });

  if (ctx.dryRun) {
    console.log(`runner online: ${runners}`);
    dryRunTable(issue);
    try {
      ctx.limits = checkLimits(ctx.claude, ctx.threshold) ?? null;
      if (!ctx.limits) console.log("limits: probe skipped (--threshold 100)");
    } catch (err) {
      if (!(err instanceof Stop)) throw err;
      console.log(`limits: ${err.message}`);
    }
    finishRun("dry-run", null, ctx);
  }

  rmSync(stopFile(ctx.epic), { force: true });
  startSleepGuard();
  ctx.started = true;
  await runLoop(ctx);
  finishRun("done", null, ctx);
} catch (err) {
  const stop = err instanceof Stop ? err : new Stop("error", err.stack ?? err.message);
  finishRun(stop.reason, stop.message, ctx);
}
