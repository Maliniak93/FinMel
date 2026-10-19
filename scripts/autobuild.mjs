#!/usr/bin/env node
// Usage: node scripts/autobuild.mjs [<n>] [--items N|all] [--include-manual|--skip-manual] [--start now|MIN|HH:MM] [--until HH:MM] [--max-tasks N] [--on-limit sleep|wait|exit] [--max-wait HOURS] [--shutdown|--no-shutdown] [--dry-run]
//   <n>               build only that epic or standalone issue (manual or not); without it the whole build queue, item by item
//   --items N|all     take the first N queue items (default all)
//   --include-manual  also take an In progress item that is not marked autobuild (default --skip-manual)
//   --start           keep the PC awake from launch but start the work (preflight included) at that time: now, in MIN minutes or at the next HH:MM; the stop file ends the wait
//   --until HH:MM     no new claude session starts at or after the next HH:MM; the one running finishes, then the run ends with reason `window`
//   --max-tasks N     stop before the (N+1)th build of this run (0 = preflight only)
//   --on-limit        a build that hits the Claude usage limit: sleep (default; suspend under 1 h, hibernate from 1 h, awake up to 15 min or without wake timers), wait (awake) or exit (ends the run with reason `limit`)
//   --max-wait HOURS  a reset further away than this ends the run with `limit-too-far` (default 12)
//   --shutdown        ask with a 10 min Tak/Nie popup when the run ends, then shut down (default; no answer shuts down); --no-shutdown does not
//   --dry-run         print the plan, the start time and what would happen per part; no preflight script, builds, merges, comments or shutdown
//   in a terminal (stdin and stdout a TTY) a start wizard asks every setting no flag answered, shows a summary and the equivalent command and asks `Start? [T/n]`; n exits 0, Ctrl+C 130, nothing starts
//   node scripts/autobuild.mjs --stop   creates the one stop file; the running driver stops before the next build
//   node scripts/autobuild.mjs --sleep-test MIN | --hibernate-test MIN   suspend or hibernate with a wake timer MIN minutes ahead (no popup), print the planned and actual wake time and `powercfg /lastwake`, exit
//   an unknown reset ends the run with `limit-unknown`; a PC that wakes but is not ready within 15 min with `wake-not-ready`; every line also goes to skarbiec-plan/autobuild-runs/<timestamp>.log
//   an e2e part blocked at `e2e` (app) is repaired unattended: `/fix e2e:#<n>` publishes fix part(s) ahead of it, corrects the epic's plan, or comments a test-defect diagnosis on the part; the loop builds and merges fix parts, then rebuilds the e2e part; after 3 app blocks on one part, or a repair that stops, the run ends with reason `e2e app`
//   an e2e part blocked at `e2e` (environment) is rebuilt once per run after stop-stack; a second block ends the run with reason `e2e environment`
//   a build session that ends without shipping a ready workflow run is shipped here from the run's record (ship.mjs --from-run)
//   transient GitHub errors (5xx, timeouts) are retried for ~8 min before the run stops
//   a GitHub rate limit (primary or secondary) is waited out with no upper bound, then the same call or step reruns (a child ending on the GITHUB_LIMIT marker or ship.mjs exit 4 included); the wait does not count towards the build or CI timeouts, and writes already done (merge, close, summary comment) are skipped
//   exit 0 when done (or a dry run is fine), 2 otherwise, 130 on Ctrl+C

import { spawn, spawnSync } from "node:child_process";
import { appendFileSync, existsSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { findReplay, latestRunRecord } from "./build-replay.mjs";
import { E2E_BLOCKED, appBlocks, parseFixResult, repairOutcome } from "./e2e-repair.mjs";
import { closeUnlessClosed, createTimeoutBudget, limitFromStreamEvent, mergeUnlessMerged, postOnce, runStepWithLimit } from "./autobuild-gh.mjs";
import { postRunVerify } from "./autobuild-verify.mjs";
import { awaitReady, buildWithResume, finishShutdown, handleLimit } from "./autobuild-limit.mjs";
import { areaWarning, mayStart, planFrom, planLines, runQueue, startAt, startWork, windowEnd } from "./autobuild-queue.mjs";
import { UsageError, WizardCancelled, parseFlags, runWizard } from "./autobuild-wizard.mjs";
import { confirmPower, suspendUntil, wakeTimers } from "./power.mjs";
import { itemTag, queueOf } from "./queue.mjs";
import { REPO, TRANSIENT_RE, checkBuckets, hasRunMarker, limitFromGhFailure, limitFromText, limitWait, GhLimitError, rateLimit, spawnGh, usageLine } from "./gh.mjs";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const IS_WIN = process.platform === "win32";
const BUILD_TIMEOUT_MS = 4 * 3600e3;
const CI_POLL_MS = 30e3;
const CI_MISSING_MS = 10 * 60e3;
const CI_TIMEOUT_MS = 90 * 60e3;
const NODE = process.execPath;
const MAX_E2E_REPAIRS = 3;
const FIX_MODEL = "claude-opus-5-5";

class Stop extends Error {
  constructor(reason, detail) {
    super(detail);
    this.reason = reason;
  }
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const minutes = (ms) => Math.round(ms / 600) / 100;
const clock = (date = new Date()) => date.toTimeString().slice(0, 5);
const pad = (n) => String(n).padStart(2, "0");

let logPath = null;
let runStamp = String(Date.now());
let lastSayAt = Date.now();

function say(text) {
  const [first, ...more] = String(text).split(/\r?\n/);
  const out = [`${clock()}  ${first}`, ...more.map((l) => `       ${l}`)].join("\n");
  console.log(out);
  if (logPath) appendFileSync(logPath, `${out}\n`);
  lastSayAt = Date.now();
}

function sayUsage(label) {
  try {
    say(`${usageLine(rateLimit())} · ${label}`);
  } catch (err) {
    say(`GitHub: limit unreadable (${String(err.message).split(/\r?\n/)[0].slice(0, 100)}) · ${label}`);
  }
}

function openLog() {
  const dir = path.join(REPO_ROOT, "skarbiec-plan", "autobuild-runs");
  mkdirSync(dir, { recursive: true });
  const d = new Date();
  const stamped = `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}-${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}`;
  runStamp = stamped;
  logPath = path.join(dir, `${stamped}.log`);
  say(`autobuild · log ${path.relative(REPO_ROOT, logPath).replaceAll("\\", "/")}`);
}

function outcome(res) {
  const err = res.error?.message ?? (res.stderr ?? "").trim();
  return { ok: !res.error && res.status === 0, out: (res.stdout ?? "").trim(), err, firstErr: err.split(/\r?\n/).find(Boolean) ?? "", all: `${res.stdout ?? ""}\n${res.stderr ?? ""}` };
}

function run(cmd, argv, options = {}) {
  return outcome(spawnSync(cmd, argv, { cwd: REPO_ROOT, encoding: "utf8", windowsHide: true, shell: false, maxBuffer: 64 * 1024 * 1024, ...options }));
}

const RETRY_DELAYS_S = [15, 30, 60, 120, 240];
const waitBudgets = new Set();

// A child node process does the sleeping so that Ctrl+C (delivered to the whole console group) ends it and the wait with it
function blockingSleep(ms) {
  const res = spawnSync(NODE, ["-e", `setTimeout(() => {}, ${Math.ceil(ms)})`], { windowsHide: true, stdio: "ignore" });
  if (res.error) Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);
  else if (res.status !== 0) interrupt();
}

function limitSleep(ms) {
  const until = Date.now() + ms;
  while (Date.now() < until) {
    blockingSleep(Math.min(HEARTBEAT_MS, until - Date.now()));
    if (Date.now() < until) say(`still waiting for the GitHub limit, ${Math.round((until - Date.now()) / 60e3)} min left`);
  }
}

function waitUntil(at) {
  const until = at.getTime();
  say(`start: the work starts at ${clock(at)} (${minutes(until - Date.now())} min)`);
  let nextBeat = Date.now() + HEARTBEAT_MS;
  while (Date.now() < until) {
    blockingSleep(Math.min(60e3, until - Date.now()));
    if (existsSync(stopFile())) throw new Stop("stopped", "stop file found during the wait for the start");
    if (Date.now() >= nextBeat && Date.now() < until) {
      say(`start: ${Math.round((until - Date.now()) / 60e3)} min left`);
      nextBeat += HEARTBEAT_MS;
    }
  }
  say("start time reached - starting");
}

const excludeFromBudgets = (ms) => waitBudgets.forEach((budget) => budget.exclude(ms));
const stepWait = { log: say, sleep: limitSleep, now: Date.now, onWait: excludeFromBudgets };

function limitOf(res) {
  const marker = limitFromText(res.all);
  if (marker) return new GhLimitError(marker.kind, marker.resetAt);
  return res.err ? limitFromGhFailure(res.err) : null;
}

function waitOutLimit(limit, attempt) {
  const { ms, until } = limitWait(limit, attempt, Date.now());
  say(`limit GitHuba (${limit.kind}), czekam do ${until}`);
  excludeFromBudgets(ms);
  limitSleep(ms);
  say("limit GitHuba: wznawiam");
}

function retryTransient(label, fn, { alreadyDone } = {}) {
  let res = fn();
  let transient = 0;
  let limits = 0;
  while (!res.ok) {
    const limit = limitOf(res);
    if (limit) {
      waitOutLimit(limit, ++limits);
      if (alreadyDone?.()) return { ok: true, out: "", err: "", firstErr: "", all: "" };
      res = fn();
    } else if (transient < RETRY_DELAYS_S.length && TRANSIENT_RE.test(res.err)) {
      const s = RETRY_DELAYS_S[transient++];
      say(`github: ${label} failed (${short(res.firstErr, 100)}), retry ${transient}/${RETRY_DELAYS_S.length} in ${s}s`);
      Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, s * 1000);
      res = fn();
    } else break;
  }
  return res;
}

const gh = (argv) => outcome(spawnGh(argv));
const git = (argv) => run("git", argv);
const project = (argv) => run(NODE, ["scripts/gh-project.mjs", ...argv]);

function readFlags(argv) {
  try {
    return parseFlags(argv);
  } catch (err) {
    throw err instanceof UsageError ? new Stop("usage", err.message) : err;
  }
}

const stopFile = () => path.join(os.tmpdir(), "skarbiec-autobuild.stop");

function getIssue(number) {
  const res = retryTransient(`project get ${number}`, () => project(["get", String(number)]));
  if (!res.ok) throw new Stop("github", `gh-project.mjs get ${number}: ${res.firstErr}`);
  return JSON.parse(res.out);
}

function openPr(branch) {
  const res = retryTransient("pr list", () => gh(["pr", "list", "--repo", REPO, "--head", branch, "--state", "open", "--json", "number,url", "--limit", "1"]));
  if (!res.ok) throw new Stop("github", `gh pr list: ${res.firstErr}`);
  return JSON.parse(res.out)[0] ?? null;
}

const currentBranch = () => git(["rev-parse", "--abbrev-ref", "HEAD"]).out;
const dirtyFiles = () => git(["status", "--porcelain"]).out.split(/\r?\n/).filter(Boolean);

function lastComment(number) {
  const res = retryTransient(`issue view ${number}`, () => gh(["issue", "view", String(number), "--repo", REPO, "--json", "comments", "--jq", '.comments[-1].body // ""']));
  if (!res.ok) throw new Stop("github", `gh issue view ${number}: ${res.firstErr}`);
  return res.out;
}

// ship.mjs posts a report on every run, so a comment newer than the build start means the relay did ship (or block)
function commentedSince(number, since) {
  const iso = new Date(since).toISOString().replace(/\.\d+Z$/, "Z");
  const jq = `[.comments[] | select(.createdAt >= ${JSON.stringify(iso)})] | length`;
  const res = retryTransient(`issue view ${number}`, () => gh(["issue", "view", String(number), "--repo", REPO, "--json", "comments", "--jq", jq]));
  if (!res.ok) throw new Stop("github", `gh issue view ${number}: ${res.firstErr}`);
  return Number(res.out) > 0;
}

const SHIP_BLOCKED = "**Blocked** at `ship`";

function e2eAppBlocks(number) {
  const res = retryTransient(`issue view ${number}`, () => gh(["issue", "view", String(number), "--repo", REPO, "--json", "comments", "--jq", "[.comments[] | {body}]"]));
  if (!res.ok) throw new Stop("github", `gh issue view ${number}: ${res.firstErr}`);
  return appBlocks(JSON.parse(res.out));
}

// "ship" retries a failed ship step, "resume" continues an interrupted build on the tree on disk, "build" starts fresh
// (a part blocked at e2e commits nothing and ship cleaned the tree: prepare recuts its branch from master)
function partPlan(part, dirtyCount) {
  const onBranch = currentBranch() === part.branch;
  if (!onBranch && dirtyCount) return "build";
  const last = lastComment(part.number);
  if (last.startsWith(SHIP_BLOCKED)) return "ship";
  if (last.startsWith(E2E_BLOCKED)) return "build";
  return onBranch && dirtyCount ? "resume" : "build";
}

// "e2e app: <trace>" / "e2e environment: <trace>" when the part's last comment is the e2e block, else null
function e2eBlock(number) {
  const last = lastComment(number);
  if (!last.startsWith(E2E_BLOCKED)) return null;
  const cls = /^\*\*Blocked\*\* at `e2e` \((\w+)\)/.exec(last)?.[1] ?? "app";
  const trace = /trace: `([^`]+)`/.exec(last)?.[1];
  const report = /report: `([^`]+)`/.exec(last)?.[1];
  return { cls, reason: `e2e ${cls}`, where: trace ? `trace ${trace}` : report ? `report ${report}` : "see the report on the issue" };
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

async function retryShip(part) {
  say(`retrying the ship step of #${part.number} on ${part.branch}`);
  if (currentBranch() !== part.branch) {
    const switched = git(["switch", part.branch]);
    if (!switched.ok) throw new Stop("ship-failed", `git switch ${part.branch}: ${switched.firstErr}`);
  }
  const json = JSON.stringify({ deviations: [], resumedShip: true });
  await ship(part.number, ["--branch", part.branch, "--title", part.title, "--json", json]);
}

// The build session can end without running nextCommand (a confused relay); a ready run is then shipped from its record.
async function shipUnshippedRun(part, since) {
  const record = latestRunRecord({ issue: part.number, since });
  if (record?.result?.status !== "ready" || commentedSince(part.number, since)) return false;
  say(`#${part.number}: the build session did not ship its ready run - shipping it from workflow ${record.runId}`);
  await ship(part.number, ["--from-run"]);
  return true;
}

async function ship(number, flags) {
  const { res } = await runStepWithLimit(async () => {
    const spawned = spawnSync(NODE, ["scripts/ship.mjs", String(number), ...flags], { cwd: REPO_ROOT, encoding: "utf8", windowsHide: true, shell: false, timeout: 3600e3, maxBuffer: 64 * 1024 * 1024 });
    return { code: spawned.status, output: `${spawned.stdout ?? ""}\n${spawned.stderr ?? ""}`, res: spawned };
  }, stepWait);
  const line = (res.stdout ?? "").split(/\r?\n/).reverse().find((l) => l.startsWith("SHIP_RESULT: "));
  let parsed = null;
  try {
    parsed = JSON.parse(line.slice("SHIP_RESULT: ".length));
  } catch {
  }
  if (parsed?.ok) return;
  const cause = res.error?.message ?? (res.stderr ?? "").trim().split(/\r?\n/).find(Boolean) ?? "";
  throw new Stop("ship-failed", parsed ? `${parsed.failedCommand ?? "ship.mjs"}: ${parsed.error ?? "failed"}` : `ship.mjs gave no result: ${cause}`);
}

let sleepGuard = null;

function startSleepGuard() {
  if (!IS_WIN) return;
  // 0x80000003 = ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED - the display staying on also keeps the idle lock off
  const script = [
    "$sig = '[DllImport(\"kernel32.dll\")] public static extern uint SetThreadExecutionState(uint esFlags);'",
    "Add-Type -MemberDefinition $sig -Name Power -Namespace Skarbiec | Out-Null",
    "[Skarbiec.Power]::SetThreadExecutionState([uint32]2147483651) | Out-Null",
    `Wait-Process -Id ${process.pid} -ErrorAction SilentlyContinue`,
  ].join("; ");
  sleepGuard = spawn("powershell.exe", ["-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command", script], { windowsHide: true, stdio: "ignore" });
  sleepGuard.on("error", () => {});
  say("sleep, display-off and idle lock blocked while the run lasts (closing the lid still sleeps)");
}

function killTree(child) {
  if (!child || child.exitCode !== null) return;
  if (IS_WIN) spawnSync("taskkill", ["/pid", String(child.pid), "/T", "/F"], { windowsHide: true });
  else child.kill("SIGKILL");
}

process.on("exit", () => killTree(sleepGuard));

let buildChild = null;

const LIMIT_TEXT = /hit your (session|weekly) limit|usage limit/i;
const ACTIVE_STATES = new Set(["start", "progress"]);
const HEARTBEAT_MS = 10 * 60e3;

const short = (text, max = 70) => {
  const flat = String(text ?? "").replace(/\s+/g, " ").trim();
  return flat.length > max ? `${flat.slice(0, max - 1)}…` : flat;
};
const modelName = (model) => String(model ?? "?").replace(/^claude-/, "");
const kTokens = (n) => `${Math.round((n ?? 0) / 1000)}k`;
const span = (ms) => {
  const s = Math.round(ms / 1000);
  return s < 60 ? `${s}s` : `${Math.floor(s / 60)}m ${s % 60}s`;
};
// resetsAt is epoch seconds in the stream; accept milliseconds as well
const resetClock = (t) => (t ? clock(new Date(t > 1e12 ? t : t * 1000)) : "unknown");

function createReporter(number) {
  const rep = { limitHit: false, resetsAt: null, sessionId: null, result: null, ghLimit: null };
  const toolNames = new Map();
  const agents = new Map();
  const phases = new Set();
  const bgTasks = new Set();
  let limitAnnounced = false;
  let latest = null;

  const markLimit = (resetsAt) => {
    rep.limitHit = true;
    rep.resetsAt = resetsAt ?? rep.resetsAt;
    if (limitAnnounced) return;
    limitAnnounced = true;
    say(`LIMIT reached · resets ${resetClock(rep.resetsAt)}`);
  };

  const onAgent = (a) => {
    const prev = agents.get(a.agentId);
    agents.set(a.agentId, a);
    if (!prev) {
      if (!phases.has(a.phaseTitle)) {
        phases.add(a.phaseTitle);
        say(`── ${a.phaseTitle}`);
      }
      say(`   ${a.agentType} (${modelName(a.model)}) start · ${a.label}`);
    }
    if (a.state !== prev?.state && !ACTIVE_STATES.has(a.state)) {
      say(`   ${a.agentType} ${a.state} · ${kTokens(a.tokens)} tokens · ${a.toolCalls ?? 0} tools · ${span(a.durationMs ?? 0)}`);
    }
  };

  const onProgress = (obj) => {
    for (const entry of obj.workflow_progress ?? []) if (entry.type === "workflow_agent") onAgent(entry);
    const running = [...agents.values()].filter((a) => ACTIVE_STATES.has(a.state)).sort((a, b) => (b.lastProgressAt ?? 0) - (a.lastProgressAt ?? 0))[0];
    latest = { phase: String(obj.description ?? "").split(":")[0], agent: running?.agentType, tokens: obj.usage?.total_tokens, tools: obj.usage?.tool_uses };
  };

  const onAssistant = (obj) => {
    if (obj.error === "rate_limit") markLimit();
    for (const block of obj.message?.content ?? []) {
      if (block.type !== "tool_use") continue;
      toolNames.set(block.id, block.name);
      if (obj.parent_tool_use_id) continue;
      const input = block.input ?? {};
      if (block.name === "Bash") say(`run: ${short(input.description ?? input.command)}`);
      else if (block.name === "Workflow") say(`workflow: ${input.name ?? input.scriptPath ?? /name:\s*'([^']+)'/.exec(input.script ?? "")?.[1] ?? "script"}`);
      else if (block.name === "Skill") say(`skill: ${input.skill}`);
    }
  };

  const onSystem = (obj) => {
    if (obj.subtype === "init") {
      if (rep.sessionId !== obj.session_id) say(`#${number} session ${obj.session_id} (full view later: claude --resume ${obj.session_id})`);
      rep.sessionId = obj.session_id;
    } else if (obj.subtype === "task_progress") onProgress(obj);
    else if (obj.subtype === "task_started" && obj.task_type === "local_bash" && obj.is_backgrounded) {
      bgTasks.add(obj.task_id);
      say(`bg: ${short(obj.description)}`);
    } else if (obj.subtype === "task_notification" && bgTasks.delete(obj.task_id)) {
      say(`bg ${obj.status === "completed" ? "done" : "failed"}: ${short(obj.summary)}`);
    } else if (obj.subtype === "permission_check_status" && !["checking", "done", "allowed"].includes(obj.status)) {
      say(`DENIED: ${toolNames.get(obj.tool_use_id) ?? obj.tool_use_id} (${obj.status})`);
    }
  };

  const onResult = (obj) => {
    rep.sessionId ??= obj.session_id;
    if (obj.is_error && LIMIT_TEXT.test(String(obj.result ?? ""))) markLimit();
    rep.result = obj;
  };

  rep.handle = (obj) => {
    if (obj.type === "assistant") onAssistant(obj);
    else if (obj.type === "system") onSystem(obj);
    else if (obj.type === "rate_limit_event" && obj.rate_limit_info?.status === "rejected") markLimit(obj.rate_limit_info.resetsAt);
    else if (obj.type === "result") onResult(obj);
    else if (obj.type === "user") rep.ghLimit = limitFromStreamEvent(obj) ?? rep.ghLimit;
  };

  rep.heartbeat = () => {
    if (!latest || Date.now() - lastSayAt < HEARTBEAT_MS) return;
    say(`   still: ${[latest.phase, latest.agent, `${kTokens(latest.tokens)} tokens`, `${latest.tools ?? 0} tools`].filter(Boolean).join(" · ")}`);
  };

  return rep;
}

function reportBuildEnd(number, code, result, ms) {
  const min = Math.round(ms / 60e3);
  if (!result) return say(`#${number} build ended without a result · ${min} min (exit ${code})`);
  const cost = `$${(result.total_cost_usd ?? 0).toFixed(2)}`;
  if (result.is_error) return say(`#${number} build error: ${short(String(result.result ?? "").split(/\r?\n/).find(Boolean) ?? result.subtype, 140)} · ${min} min · ${cost}`);
  say(`#${number} build finished · ${min} min · ${cost} · ${result.permission_denials?.length ?? 0} denials`);
}

function build(claude, number, title) {
  return runClaude(claude, number, `${title} — build`, `/build #${number}`, "haiku");
}

function runClaude(claude, number, heading, prompt, model) {
  const startedAt = Date.now();
  const reporter = createReporter(number);
  say(`══ #${number} ${heading}`);

  const spec = claudeSpawn(claude, ["-p", prompt, "--model", model, "--permission-mode", "auto", "--output-format", "stream-json", "--verbose"]);
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
    const beat = setInterval(reporter.heartbeat, 60e3);

    const handleLine = (line) => {
      if (!line.trim()) return;
      try {
        reporter.handle(JSON.parse(line));
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
      clearInterval(beat);
      handleLine(buffer);
      buildChild = null;
      reportBuildEnd(number, code, reporter.result, Date.now() - startedAt);
      if (code !== 0 && errTail.trim()) say(`exit ${code}: ${errTail.trim().split(/\r?\n/).slice(-3).join(" | ")}`);
      resolve({ code, timedOut, limitHit: reporter.limitHit, resetsAt: reporter.resetsAt, ghLimit: reporter.ghLimit, sessionId: reporter.sessionId, text: String(reporter.result?.result ?? ""), ms: Date.now() - startedAt });
    });
  });
}

// REST on purpose: the core quota is separate from the GraphQL one the project reads spend. Returns null when the read failed.
function readChecks(pr) {
  const head = retryTransient("pr head", () => gh(["api", `repos/${REPO}/pulls/${pr}`, "--jq", ".head.sha"]));
  if (!head.ok || !head.out) {
    say(`CI PR #${pr}: could not read the head commit (${short(head.firstErr, 120)})`);
    return null;
  }
  const res = retryTransient("check runs", () => gh(["api", `repos/${REPO}/commits/${head.out}/check-runs?per_page=100`, "--jq", ".check_runs"]));
  try {
    if (!res.ok) throw new Error(res.firstErr);
    return checkBuckets(JSON.parse(res.out));
  } catch (err) {
    say(`CI PR #${pr}: could not read the check runs (${short(err.message, 120)})`);
    return null;
  }
}

async function waitForCi(pr) {
  const startedAt = Date.now();
  const budgets = [];
  const track = (ms) => {
    const budget = createTimeoutBudget(ms);
    waitBudgets.add(budget);
    budgets.push(budget);
    return budget;
  };
  const total = track(CI_TIMEOUT_MS);
  let missing = track(CI_MISSING_MS);
  let lastStatus = "";
  let greenPolls = 0;
  try {
    while (true) {
      const checks = readChecks(pr.number);
      if (!checks) {
        greenPolls = 0;
        if (total.expired()) throw new Stop("ci-timeout", `PR ${pr.url}: checks not readable or not green after 90 min`);
        await sleep(CI_POLL_MS);
        continue;
      }
      if (checks.length) missing = null;
      else missing ??= track(CI_MISSING_MS);
      const count = (...buckets) => checks.filter((c) => buckets.includes(c.bucket)).length;
      const failed = checks.filter((c) => ["fail", "cancel"].includes(c.bucket));
      const status = `pass ${count("pass")} · skipping ${count("skipping")} · pending ${count("pending")} · fail ${failed.length}`;
      if (checks.length && status !== lastStatus) say(`CI PR #${pr.number}: ${status}`);
      lastStatus = status;

      if (failed.length) throw new Stop("ci-failed", `PR ${pr.url}: ${failed.map((c) => `${c.name} (${c.link})`).join(", ")}`);
      if (missing?.expired()) throw new Stop("ci-missing", `PR ${pr.url}: no checks after 10 min - the runner did not pick up the job`);
      greenPolls = checks.length && checks.every((c) => ["pass", "skipping"].includes(c.bucket)) ? greenPolls + 1 : 0;
      // two green polls in a row: a late-registered check must not slip past the merge
      if (greenPolls >= 2) return Date.now() - startedAt;
      if (total.expired()) throw new Stop("ci-timeout", `PR ${pr.url}: checks not green after 90 min`);
      await sleep(CI_POLL_MS);
    }
  } finally {
    budgets.forEach((budget) => waitBudgets.delete(budget));
  }
}

function prState(pr) {
  const view = retryTransient(`pr view ${pr.number}`, () => gh(["pr", "view", String(pr.number), "--repo", REPO, "--json", "state"]));
  if (!view.ok) throw new Stop("github", `gh pr view ${pr.number}: ${view.firstErr}`);
  return JSON.parse(view.out).state;
}

function closeIssue(number) {
  return closeUnlessClosed({
    state: () => getIssue(number).state,
    close: () => {
      const closed = () => getIssue(number).state === "CLOSED";
      retryTransient(`issue close ${number}`, () => gh(["issue", "close", String(number), "--repo", REPO, "--reason", "completed"]), { alreadyDone: closed });
    },
  });
}

async function mergeAndSync(pr, branch, number) {
  await mergeUnlessMerged({
    state: () => prState(pr),
    merge: () => {
      const merged = retryTransient(`pr merge ${pr.number}`, () => gh(["pr", "merge", String(pr.number), "--repo", REPO, "--squash", "--delete-branch"]), { alreadyDone: () => prState(pr) === "MERGED" });
      if (!merged.ok && prState(pr) !== "MERGED") throw new Stop("merge-failed", `PR ${pr.url}: ${merged.firstErr}`);
    },
  });
  say(`merged PR #${pr.number}`);
  ctx.merged++;
  for (const argv of [["switch", "master"], ["pull", "--ff-only", "origin", "master"]]) {
    const step = git(argv);
    if (!step.ok) throw new Stop("sync-failed", `git ${argv.join(" ")}: ${step.firstErr}`);
  }
  git(["branch", "-D", branch]);
  for (let attempt = 0; attempt < 6; attempt++) {
    if (getIssue(number).state === "CLOSED") return;
    await sleep(10e3);
  }
  await closeIssue(number);
}

function labelAsRun(argv) {
  const res = retryTransient(`gh ${argv.slice(0, 3).join(" ")}`, () => gh([...argv, "--repo", REPO]));
  if (!res.ok) throw new Stop("github", `gh ${argv.slice(0, 3).join(" ")}: ${res.firstErr}`);
}

function assertWindowOpen(ctx) {
  if (mayStart(ctx.until, new Date())) throw new Stop("window", `no new build after ${clock(ctx.until)}`);
}

function resumeCommand(ctx) {
  const { flags } = ctx;
  const args = [flags.only, flags.items && `--items ${flags.items}`, flags["include-manual"] && "--include-manual", flags["skip-manual"] && "--skip-manual", flags["max-tasks"] !== undefined && `--max-tasks ${flags["max-tasks"]}`, flags.until && `--until ${flags.until}`, flags["on-limit"] && `--on-limit ${flags["on-limit"]}`, flags["max-wait"] && `--max-wait ${flags["max-wait"]}`, flags.shutdown && "--shutdown", flags["no-shutdown"] && "--no-shutdown"];
  return ["node scripts/autobuild.mjs", ...args.filter((a) => a !== undefined && a !== false)].join(" ");
}

function awakeWait(at) {
  const until = at.getTime();
  let nextBeat = Date.now() + HEARTBEAT_MS;
  while (Date.now() < until) {
    blockingSleep(Math.min(60e3, until - Date.now()));
    if (existsSync(stopFile())) return;
    if (Date.now() >= nextBeat && Date.now() < until) {
      say(`limit Claude: ${Math.round((until - Date.now()) / 60e3)} min left`);
      nextBeat += HEARTBEAT_MS;
    }
  }
}

async function sleepUntil(at, mode) {
  const armed = await suspendUntil(at, mode);
  if (armed) say(`obudzony o ${clock()}`);
  else say(`${mode}: could not arm the wake timer - waiting awake`);
  awakeWait(at);
}

const wakeLine = (power) => (power.works ? "wake timers: on" : `wake timers: off — ${power.hint}`);

function wakeReady() {
  if (!preflightResult().ok) {
    say("wake: preflight not ready yet");
    return false;
  }
  const runners = gh(["api", `repos/${REPO}/actions/runners`, "--jq", '[.runners[] | select(.status == "online")] | length']);
  if (runners.ok && Number(runners.out) > 0) return true;
  say(`wake: ${runners.ok ? "no runner online" : `gh failed (${short(runners.firstErr, 100)})`} yet`);
  return false;
}

function limitStopDetail(number, reason, resetsAt, ctx) {
  const resets = resetsAt ? clock(new Date(resetsAt > 1e12 ? resetsAt : resetsAt * 1000)) : "unknown";
  const tail = reason === "window" ? `, after the window end ${clock(ctx.until)}` : "";
  return `#${number}: usage limit reached, resets ${resets}${tail}; resume with ${resumeCommand(ctx)}`;
}

async function onUsageLimit(limit, number) {
  const power = ctx.power;
  const decision = await handleLimit(
    { resetsAt: limit.resetsAt ? new Date(limit.resetsAt > 1e12 ? limit.resetsAt : limit.resetsAt * 1000) : null, now: new Date(), onLimit: ctx.flags["on-limit"] ?? "sleep", maxWaitHours: ctx.maxWaitHours, timersWork: power.works, hibernateAvailable: power.hibernateAvailable, windowEnd: ctx.until },
    { confirm: confirmPower, suspendUntil: sleepUntil, waitUntil: awakeWait, log: say },
  );
  if (decision.action === "end") throw new Stop(decision.reason, limitStopDetail(number, decision.reason, limit.resetsAt, ctx));
  if (existsSync(stopFile())) throw new Stop("stopped", "stop file found during the limit wait");
  assertWindowOpen(ctx);
}

async function waitForReady(number) {
  say("wake: checking preflight, GitHub and the runner");
  const ready = await awaitReady({ check: wakeReady, now: Date.now, sleep });
  if (!ready.ready) throw new Stop(ready.reason, `#${number}: the PC was not ready 15 min after the limit wait; resume with ${resumeCommand(ctx)}`);
  say("wake: ready - building the same part again");
}

function buildThroughLimit(part) {
  return buildWithResume({
    part,
    build: async () => {
      const result = await build(ctx.claude, part.number, part.title);
      return result.limitHit && !result.timedOut ? { ...result, limit: { resetsAt: result.resetsAt } } : result;
    },
    onLimit: (limit) => onUsageLimit(limit, part.number),
    awaitReady: () => waitForReady(part.number),
  });
}

async function handlePart(sub, ctx) {
  const part = getIssue(sub.number);
  if (!part.branch) throw new Stop("no-branch", `#${part.number} has no Branch field`);
  const entry = { item: ctx.item, number: part.number, title: part.title, prUrl: null, buildMin: null, ciMin: null };
  let pr = openPr(part.branch);

  if (!pr) {
    if (existsSync(stopFile())) throw new Stop("stopped", "stop file found");
    if (ctx.builds >= ctx.maxTasks) throw new Stop("max-tasks", `${ctx.maxTasks} build(s) done in this run`);
    if (partPlan(part, dirtyFiles().length) === "ship") {
      entry.shipOnly = true;
      await retryShip(part);
      pr = openPr(part.branch);
      if (!pr) throw new Stop("ship-failed", `#${part.number}: ship.mjs succeeded but no open PR exists for ${part.branch}`);
    } else {
      assertWindowOpen(ctx);
      ctx.builds++;
      const buildStart = Date.now();
      // a build that ends on the GitHub limit without a PR is rerun after the reset; the workflow journal replays what it finished
      const { built, pr: builtPr } = await runStepWithLimit(async () => {
        const result = await startWork({ item: ctx.item, part: part.number, gh: labelAsRun, build: () => buildThroughLimit(part) });
        const stopped = result.timedOut;
        const found = stopped ? null : openPr(part.branch);
        return { code: result.code, output: result.ghLimit && !found && !stopped ? `GITHUB_LIMIT ${JSON.stringify(result.ghLimit)}` : "", built: result, pr: found };
      }, stepWait);
      entry.buildMin = minutes(built.ms);
      if (built.timedOut) throw new Stop("timeout", `#${part.number}: build exceeded 4 h and was killed`);
      pr = builtPr;
      const blockedAtE2e = pr ? null : e2eBlock(part.number);
      if (blockedAtE2e) return repairE2e(part, blockedAtE2e, ctx);
      if (!pr && (await shipUnshippedRun(part, buildStart))) pr = openPr(part.branch);
      if (!pr) throw new Stop("blocked", `#${part.number}: no PR after the build (exit ${built.code}) - see the report on the issue`);
    }
  }

  entry.prUrl = pr.url;
  ctx.parts.push(entry);
  entry.ciMin = minutes(await waitForCi(pr));
  await mergeAndSync(pr, part.branch, part.number);
}

// returns after a repair so runQueue picks the next open part: a fix part placed ahead of the e2e part, or the e2e part again
async function repairE2e(part, block, ctx) {
  const giveUp = (why) => new Stop(block.reason, `#${part.number}: ${block.reason} failure, ${block.where} - ${why}, then resume with ${resumeCommand(ctx)}`);
  if (block.cls === "environment") {
    if (ctx.envRetried.has(part.number)) throw giveUp("the environment failed twice in this run; fix the machine");
    ctx.envRetried.add(part.number);
    say(`#${part.number}: e2e environment failure, ${block.where} - stopping the stack and rebuilding once`);
    run(NODE, ["scripts/stop-stack.mjs"], { timeout: 10 * 60e3 });
    return;
  }
  const blocks = e2eAppBlocks(part.number);
  if (blocks > MAX_E2E_REPAIRS) throw giveUp(`${blocks} app blocks, ${MAX_E2E_REPAIRS} unattended repairs did not make it green; fix it with /fix`);
  if (existsSync(stopFile())) throw new Stop("stopped", "stop file found");
  if (ctx.builds >= ctx.maxTasks) throw new Stop("max-tasks", `${ctx.maxTasks} build(s) done in this run`);
  assertWindowOpen(ctx);
  ctx.builds++;
  say(`#${part.number}: e2e app failure, ${block.where} - unattended repair ${blocks}/${MAX_E2E_REPAIRS}`);
  const fixed = await runClaude(ctx.claude, part.number, `${part.title} — e2e repair`, `/fix e2e:#${part.number}`, FIX_MODEL);
  if (fixed.timedOut) throw new Stop("timeout", `#${part.number}: the e2e repair exceeded 4 h and was killed`);
  if (fixed.limitHit) throw new Stop("limit", `#${part.number}: usage limit reached during the e2e repair, resets ${resetClock(fixed.resetsAt)}; resume with ${resumeCommand(ctx)}`);
  const result = parseFixResult(fixed.text);
  if (!result?.ok) throw giveUp(result ? `the repair stopped: ${result.summary}` : `the repair gave no FIX_RESULT (exit ${fixed.code})`);
  const dirty = dirtyFiles();
  if (dirty.length || currentBranch() !== "master") throw new Stop("dirty-tree", `the e2e repair left ${currentBranch()} with ${dirty.length} uncommitted file(s): ${dirty.slice(0, 5).join(", ")}`);
  const nextOpen = (getIssue(ctx.item).subIssues ?? []).find((s) => s.state !== "CLOSED")?.number;
  const outcome = repairOutcome(result, { part: part.number, nextOpen });
  if (outcome.action === "stop") throw new Stop("blocked", `#${part.number}: ${outcome.reason}`);
  if (result.kind === "test") say(`repair ${blocks}/${MAX_E2E_REPAIRS}: test defect in #${part.number} - ${short(result.summary, 120)}`);
  else say(`repair: ${short(outcome.reason, 160)}`);
}

function runnerOnline() {
  const res = retryTransient("runners", () => gh(["api", `repos/${REPO}/actions/runners`, "--jq", '[.runners[] | select(.status == "online")] | length']));
  return res.ok ? Number(res.out) : 0;
}

// an epic's sub-issues, or a standalone issue as its own single part
const partsOf = (issue) => (issue.epic ? issue.subIssues : [{ number: issue.number, title: issue.title, state: issue.state }]);

function preflightResult() {
  const pre = run(NODE, ["scripts/preflight.mjs"], { timeout: 30 * 60e3 });
  const line = pre.out.split(/\r?\n/).reverse().find((l) => l.startsWith("PREFLIGHT_RESULT: "));
  let parsed = null;
  try {
    parsed = JSON.parse(line.slice("PREFLIGHT_RESULT: ".length));
  } catch {
  }
  return { ok: Boolean(parsed?.ok), out: pre.out };
}

function runPreflight() {
  const pre = preflightResult();
  if (!pre.ok) throw new Stop("preflight", `preflight.mjs reported problems:\n${pre.out.split(/\r?\n/).slice(-15).join("\n")}`);
}

function preChecks(first, flags) {
  const dirty = dirtyFiles();
  if (dirty.length) {
    const next = partsOf(getIssue(first.number)).find((s) => s.state !== "CLOSED");
    const part = next ? getIssue(next.number) : null;
    if (part?.branch && currentBranch() === part.branch && !openPr(part.branch)) {
      const shipping = partPlan(part, dirty.length) === "ship";
      const verb = shipping ? "retrying the ship step of" : "resuming";
      const found = shipping ? null : findReplay({ issue: part.number, args: { tier: part.tier, skip: part.skipTests ? ["tests"] : [], e2e: Boolean(part.e2e) } });
      const replay = !found ? "" : found.ok ? ` - replaying ${found.steps.length} step(s), live from ${found.liveFrom ?? "ship"}` : ` - no replay: ${found.reason}`;
      say(`${flags["dry-run"] ? "dry-run: would be resumed - " : ""}${verb} #${part.number} on ${part.branch} with ${dirty.length} uncommitted file(s)${replay}`);
    } else {
      const listed = `the working tree is not clean:\n${dirty.slice(0, 10).join("\n")}${dirty.length > 10 ? `\n... ${dirty.length - 10} more` : ""}`;
      if (!flags["dry-run"]) throw new Stop("dirty-tree", listed);
      say(`warning: ${listed}\n(a real run would stop: dirty-tree)`);
    }
  }
  const runners = runnerOnline();
  if (!runners) throw new Stop("runner-offline", "no self-hosted runner is online - the WSL runner is down, see skarbiec-plan/runbooks/ci.md");
  return runners;
}

function dryRunTable(issue) {
  let first = true;
  for (const sub of partsOf(issue)) {
    let action = "skip (closed)";
    if (sub.state !== "CLOSED") {
      if (!first) action = "waits";
      else {
        first = false;
        const branch = getIssue(sub.number).branch;
        const pr = branch ? openPr(branch) : null;
        const dirtyCount = dirtyFiles().length;
        const plan = branch && !pr ? partPlan({ number: sub.number, branch }, dirtyCount) : "build";
        if (!branch) action = "stop: no Branch field";
        else if (pr) action = `CI → merge PR ${pr.url}`;
        else if (plan === "ship") action = "retry ship (previous ship step failed)";
        else if (plan === "resume") action = `resume build (${dirtyCount} uncommitted file(s) on ${branch})`;
        else action = "build";
      }
    }
    say(`#${sub.number} ${sub.title} — ${sub.state} — ${action}`);
  }
}

function summary(reason, detail, parts, resume, costLine, verifyLine) {
  const lines = [`## Autobuild run: ${reason}`, ""];
  lines.push(parts.length ? "Parts handled in this run:" : "No part was merged in this run.");
  for (const p of parts) lines.push(`- #${p.number} ${p.title} - ${p.prUrl ?? "no PR"} (${p.shipOnly ? "ship retried" : `build ${p.buildMin ?? "-"} min`}, CI ${p.ciMin ?? "-"} min)`);
  if (detail) lines.push("", "Detail:", "", "```", detail, "```");
  if (verifyLine) lines.push("", verifyLine);
  if (costLine) lines.push("", costLine);
  lines.push("", `Resume with \`${resume}\`.`);
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

function epicComments(epic) {
  const res = retryTransient(`issue view ${epic}`, () => gh(["issue", "view", String(epic), "--repo", REPO, "--json", "comments", "--jq", "[.comments[] | {body}]"]));
  try {
    return res.ok ? JSON.parse(res.out) : [];
  } catch {
    return [];
  }
}

// A summary hit by the limit mid-post is not repeated: the run's marker in a comment means it was posted
async function postSummary(epic, body) {
  const marker = `<!-- skarbiec-autobuild-summary:${epic}:${runStamp} -->`;
  const file = path.join(os.tmpdir(), `skarbiec-autobuild-${epic}-comment.md`);
  let failure = null;
  await postOnce({
    comments: () => epicComments(epic),
    marker,
    body,
    post: (text) => {
      writeFileSync(file, text, "utf8");
      const res = retryTransient(`comment ${epic}`, () => project(["comment", String(epic), "--body-file", file]), { alreadyDone: () => hasRunMarker(epicComments(epic), marker) });
      rmSync(file, { force: true });
      failure = res.ok ? null : res.firstErr;
    },
  });
  return failure;
}

const prNumber = (url) => url?.split("/").pop();
const partLine = (p) => `#${p.number} PR #${prNumber(p.prUrl)} (${p.shipOnly ? "ship retried" : `build ${Math.round(p.buildMin ?? 0)} min`}, CI ${Math.round(p.ciMin ?? 0)} min)`;

async function finishRun(reason, detail, ctx) {
  const ok = reason === "done" || reason === "dry-run";
  say(`run: ${reason}${detail ? ` — ${detail}` : ""}`);
  if (ctx.itemsDone.length) say(`items done in this run: ${ctx.itemsDone.map((n) => `#${n}`).join(", ")}`);
  if (ctx.parts.length) say(`done in this run: ${ctx.parts.map(partLine).join("; ")}`);
  const resume = resumeCommand(ctx);
  if (!ok && reason !== "usage") say(`resume: ${resume}`);
  const verifyLine = await postRunVerify({
    merged: ctx.merged,
    dryRun: ctx.dryRun,
    run,
    branch: ctx.merged > 0 && !ctx.dryRun ? currentBranch() : "master",
    dirty: ctx.merged > 0 && !ctx.dryRun ? dirtyFiles().length : 0,
    log: say,
  });
  if (!ctx.dryRun && ctx.started) {
    for (const number of ctx.touched) {
      const parts = ctx.parts.filter((p) => p.item === number);
      const failure = await postSummary(number, summary(reason, detail, parts, resume, ok ? costLine(parts) : null, verifyLine));
      if (failure) say(`could not post the comment on #${number}: ${failure}`);
    }
    if (IS_WIN) await finishShutdown({ confirm: confirmPower, run, noShutdown: ctx.flags["no-shutdown"], dryRun: ctx.dryRun, log: say });
  }
  process.exit(ok ? 0 : 2);
}

async function finishEpic(item) {
  const number = item.number;
  ctx.touched.add(number);
  const issue = getIssue(number);
  if (issue.state !== "CLOSED") {
    if (!issue.subIssues.length) throw new Stop("no-parts", `#${number} has no sub-issues`);
    await closeIssue(number);
    const moved = retryTransient(`set ${number} Done`, () => project(["set", String(number), "Status", "Done"]));
    if (!moved.ok) say(`could not move the epic card to Done: ${moved.firstErr}`);
  }
  ctx.itemsDone.push(number);
}

async function buildPart(item, part, cards) {
  const number = item.number;
  if (ctx.item !== number) {
    const issue = getIssue(number);
    if (issue.state === "CLOSED") {
      say(`#${number} ${issue.title} is closed - skipped`);
      return false;
    }
    ctx.item = number;
    ctx.touched.add(number);
    say(`── item #${number} ${issue.title} [${itemTag(item)}]`);
    const warning = areaWarning(item, cards);
    if (warning) say(warning);
  }
  sayUsage(`#${part} start`);
  try {
    await handlePart({ number: part }, ctx);
  } finally {
    sayUsage(`#${part} end`);
  }
  if (!item.epic) ctx.itemsDone.push(number);
  return true;
}

function readCards() {
  const res = retryTransient("project list", () => project(["list"]));
  if (!res.ok) throw new Stop("github", `gh-project.mjs list: ${res.firstErr}`);
  return JSON.parse(res.out);
}

function readPlan(flags) {
  const cards = readCards();
  const items = flags.items === undefined || flags.items === "all" ? Infinity : Number(flags.items);
  try {
    return { cards, plan: planFrom(queueOf(cards), { only: flags.only, includeManual: Boolean(flags["include-manual"]), items }, cards) };
  } catch (err) {
    throw new Stop("usage", err.message);
  }
}

function sayPlan(plan, cards) {
  say("plan (podgląd - kolejka jest czytana ponownie przed każdą częścią):");
  for (const line of planLines(plan, cards)) say(line);
}

function readTimes(flags) {
  const now = new Date();
  let start;
  try {
    start = startAt(flags.start, now);
  } catch (err) {
    throw new Stop("usage", `--start: ${err.message.replace("use HH:MM", "use now, MIN or HH:MM")}`);
  }
  try {
    return { start, until: flags.until === undefined ? null : windowEnd(flags.until, start) };
  } catch (err) {
    throw new Stop("usage", `--until: ${err.message}`);
  }
}

const planNeeded = (flags) => flags.only === undefined && (flags.items === undefined || (!flags["include-manual"] && !flags["skip-manual"]));

async function startWizard(flags) {
  const tty = Boolean(process.stdin.isTTY && process.stdout.isTTY);
  if (!tty) return { run: true, flags };
  let plan = [];
  if (planNeeded(flags)) {
    const res = retryTransient("project list", () => project(["list"]));
    if (!res.ok) throw new Stop("github", `gh-project.mjs list: ${res.firstErr}`);
    plan = queueOf(JSON.parse(res.out));
  }
  try {
    return await runWizard(flags, { tty, plan, power: wakeTimers(), now: new Date() }, { input: process.stdin, output: process.stdout });
  } catch (err) {
    if (err instanceof WizardCancelled) interrupt();
    throw err;
  }
}

const ctx = { item: null, touched: new Set(), itemsDone: [], parts: [], merged: 0, builds: 0, envRetried: new Set(), started: false, dryRun: false, flags: {}, maxTasks: Infinity, maxWaitHours: 12, power: { works: false, hibernateAvailable: false, hint: "not read" }, until: null, claude: null };

async function powerTest(mode, minutesAhead) {
  const planned = new Date(Date.now() + minutesAhead * 60e3);
  say(`${mode}: wake timer at ${planned.toTimeString().slice(0, 8)} (${minutesAhead} min)`);
  const armed = await suspendUntil(planned, mode);
  say(`${mode}: ${armed ? "resumed" : "could not arm the wake timer"} at ${new Date().toTimeString().slice(0, 8)}, planned ${planned.toTimeString().slice(0, 8)}`);
  say(run("powercfg", ["/lastwake"]).out);
}

function interrupt() {
  killTree(buildChild);
  say("interrupted (Ctrl+C) - no comment, no shutdown");
  process.exit(130);
}

process.on("SIGINT", interrupt);

try {
  let flags = readFlags(process.argv.slice(2));
  ctx.flags = flags;
  ctx.dryRun = Boolean(flags["dry-run"]);
  if (flags["max-tasks"] !== undefined) ctx.maxTasks = Number(flags["max-tasks"]);
  if (Number.isNaN(ctx.maxTasks)) throw new Stop("usage", "--max-tasks must be a number");
  if (flags.items !== undefined && flags.items !== "all" && !/^[1-9]\d*$/.test(flags.items)) throw new Stop("usage", "--items must be a positive number or all");
  if (flags["include-manual"] && flags["skip-manual"]) throw new Stop("usage", "--include-manual and --skip-manual exclude each other");
  if (flags.shutdown && flags["no-shutdown"]) throw new Stop("usage", "--shutdown and --no-shutdown exclude each other");
  if (flags["on-limit"] !== undefined && !["sleep", "wait", "exit"].includes(flags["on-limit"])) throw new Stop("usage", "--on-limit must be sleep, wait or exit");
  if (flags["max-wait"] !== undefined) {
    ctx.maxWaitHours = Number(flags["max-wait"]);
    if (!(ctx.maxWaitHours > 0)) throw new Stop("usage", "--max-wait must be a positive number of hours");
  }
  const testMode = flags["sleep-test"] !== undefined ? "suspend" : flags["hibernate-test"] !== undefined ? "hibernate" : null;
  if (testMode) {
    const minutesAhead = Number(flags["sleep-test"] ?? flags["hibernate-test"]);
    if (flags["sleep-test"] !== undefined && flags["hibernate-test"] !== undefined) throw new Stop("usage", "--sleep-test and --hibernate-test exclude each other");
    if (!(minutesAhead > 0)) throw new Stop("usage", "--sleep-test and --hibernate-test need a positive number of minutes");
    await powerTest(testMode, minutesAhead);
    process.exit(0);
  }
  readTimes(flags);

  if (flags.stop) {
    writeFileSync(stopFile(), new Date().toISOString());
    say(`stop file written (${stopFile()}) - the driver stops before the next build`);
    process.exit(0);
  }

  const wizard = await startWizard(flags);
  if (!wizard.run) {
    console.log("Not started.");
    process.exit(0);
  }
  flags = wizard.flags;
  ctx.flags = flags;
  if (flags["max-wait"] !== undefined) ctx.maxWaitHours = Number(flags["max-wait"]);
  const times = readTimes(flags);
  ctx.until = times.until;

  if (!ctx.dryRun) {
    openLog();
    rmSync(stopFile(), { force: true });
    startSleepGuard();
    ctx.power = wakeTimers();
    say(wakeLine(ctx.power));
    if (times.start.getTime() > Date.now()) waitUntil(times.start);
  } else {
    ctx.power = wakeTimers();
    say(wakeLine(ctx.power));
  }
  ctx.claude = resolveClaude();
  if (!ctx.dryRun) runPreflight();
  const { cards, plan } = readPlan(flags);
  if (!plan.length) await finishRun("done", "kolejka pusta", { ...ctx, started: false });

  const runners = preChecks(plan[0], flags);

  if (ctx.dryRun) {
    say(`runner online: ${runners}`);
    sayUsage("now");
    say(`start: ${clock(times.start)}${flags.start === undefined || flags.start === "now" ? " (now)" : ""}`);
    say(`window end: ${times.until ? `${clock(times.until)} (${times.until.toLocaleDateString("en-CA")})` : "none"}`);
    sayPlan(plan, cards);
    for (const item of plan) {
      say(`── item #${item.number} ${item.title} [${itemTag(item)}]`);
      dryRunTable(getIssue(item.number));
    }
    await finishRun("dry-run", null, ctx);
  }

  ctx.started = true;
  await runQueue({
    items: flags.items === undefined || flags.items === "all" ? Infinity : Number(flags.items),
    includeManual: Boolean(flags["include-manual"]),
    only: flags.only ?? null,
    readCards,
    buildPart,
    isClosed: async (number) => getIssue(number).state === "CLOSED",
    finishEpic,
    log: say,
  });
  await finishRun("done", null, ctx);
} catch (err) {
  const stop = err instanceof Stop ? err : new Stop("error", err.stack ?? err.message);
  await finishRun(stop.reason, stop.message, ctx);
}
