#!/usr/bin/env node
// Usage: node scripts/e2e.mjs <run|up|down|status|report>
//   run [--headed | --ui] [--grep <pattern>] [--keep] [--detach | --await [--max-min N]]
//                  install deps, build the images from the working tree, start the isolated skarbiec-e2e stack, run Playwright, tear down
//   --headed       show Chromium; --ui opens the Playwright UI; both run with no retries
//   --grep <p>     only the tests whose title matches <p>
//   --keep         leave the stack up after the run (stop it with: node scripts/e2e.mjs down)
//   --detach       start the run as an independent process, print E2E_STARTED and exit 0 at once (log in .git/e2e-result.json.log)
//   --await        wait (at most --max-min N, default 55) for a detached run; E2E_PENDING (exit 4) while it runs
//   up             build and start the stack, leave it up      down  stop it and remove its volumes
//   status         container states of the skarbiec-e2e project   report  open the HTML report
//   run writes .git/e2e-result.json and prints E2E_RESULT: {...}; exit 0 green, 1 a test failed, 3 environment (Docker, port, stack health, deadline)
// The skarbiec-local stack, its images and Aspire are never touched: this project has its own name, images, ports and tmpfs data.

import { spawn, spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, openSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import net from "node:net";
import path from "node:path";
import { fileURLToPath } from "node:url";

const IS_WIN = process.platform === "win32";
const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const E2E_DIR = path.join(REPO_ROOT, "e2e");
const COMPOSE_DIR = path.join(REPO_ROOT, "deploy", "compose");
const COMPOSE_FILES = [path.join(COMPOSE_DIR, "compose.yaml"), path.join(COMPOSE_DIR, "compose.e2e.yaml")];
const PROJECT = "skarbiec-e2e";
const NPM = IS_WIN ? "npm.cmd" : "npm";
const PLAYWRIGHT_CLI = path.join(E2E_DIR, "node_modules", "@playwright", "test", "cli.js");
const TEST_RESULTS_DIR = path.join(E2E_DIR, "test-results");
const REPORT_DIR = path.join(E2E_DIR, "playwright-report");
const RESULTS_JSON = path.join(TEST_RESULTS_DIR, "results.json");
const PORTS = [18080, 18081, 18898];
const START_DEADLINE_SEC = 10 * 60;
const OVERALL_DEADLINE_MS = 45 * 60 * 1000;
const EXIT = { pass: 0, fail: 1, environment: 3, pending: 4 };

class EnvironmentError extends Error {}

let deadlineAt = Infinity;

function fatal(message) {
  console.error(message);
  process.exit(2);
}

function parseArgs(argv) {
  const [command, ...rest] = argv;
  const args = { command, headed: false, ui: false, grep: null, keep: false, detach: false, await: false, maxMin: 55 };
  for (let i = 0; i < rest.length; i++) {
    const a = rest[i];
    if (a === "--headed") args.headed = true;
    else if (a === "--ui") args.ui = true;
    else if (a === "--keep") args.keep = true;
    else if (a === "--detach") args.detach = true;
    else if (a === "--await") args.await = true;
    else if (a === "--grep") {
      args.grep = rest[++i];
      if (!args.grep) fatal("--grep requires a pattern");
    } else if (a === "--max-min") {
      args.maxMin = Number(rest[++i]);
      if (!(args.maxMin > 0)) fatal("--max-min requires a positive number of minutes");
    } else fatal(`unknown argument '${a}'`);
  }
  if (args.headed && args.ui) fatal("--headed and --ui are mutually exclusive");
  return args;
}

function git(argv) {
  const res = spawnSync("git", argv, { cwd: REPO_ROOT, encoding: "utf8" });
  return res.status === 0 ? res.stdout.trim() : null;
}

const gitDir = () => {
  const out = git(["rev-parse", "--git-dir"]);
  return out ? path.resolve(REPO_ROOT, out) : path.join(REPO_ROOT, ".git");
};
const resultFile = () => path.join(gitDir(), "e2e-result.json");
const pidFile = () => path.join(gitDir(), "e2e.pid");

function gitBuffer(argv) {
  const res = spawnSync("git", argv, { cwd: REPO_ROOT, maxBuffer: 1024 * 1024 * 1024 });
  return res.status === 0 ? res.stdout : null;
}

// Same recipe as verify.mjs: HEAD, the diff against it and the content of every untracked file.
function fingerprint() {
  const head = gitBuffer(["rev-parse", "HEAD"]);
  const diff = gitBuffer(["diff", "HEAD", "--binary"]);
  const untracked = gitBuffer(["ls-files", "--others", "--exclude-standard", "-z"]);
  if (!head || !diff || !untracked) return null;
  const sep = String.fromCharCode(0);
  const hash = createHash("sha1");
  hash.update("e2e").update(sep).update(head).update(sep).update(diff);
  for (const rel of untracked.toString("utf8").split(sep).filter(Boolean).sort()) {
    hash.update(sep).update(rel).update(sep);
    try {
      hash.update(readFileSync(path.join(REPO_ROOT, rel)));
    } catch {
      // A file that vanished mid-hash does not matter for the fingerprint.
    }
  }
  return hash.digest("hex");
}

function killTree(pid) {
  if (!pid) return;
  if (IS_WIN) spawnSync("taskkill", ["/PID", String(pid), "/T", "/F"], { encoding: "utf8" });
  else {
    try {
      process.kill(-pid, "SIGKILL");
    } catch {
      // Already gone.
    }
  }
}

// capture: collect output instead of streaming it. A command that outlives the overall deadline is killed and reported as an environment failure.
function exec(cmd, argv, { cwd = REPO_ROOT, env = process.env, capture = false, shell = false } = {}) {
  const left = deadlineAt - Date.now();
  if (left <= 0) return Promise.reject(new EnvironmentError("the 45-minute overall deadline passed"));
  return new Promise((resolve, reject) => {
    const chunks = [];
    const child = spawn(cmd, argv, {
      cwd,
      env,
      shell,
      windowsHide: true,
      detached: !IS_WIN,
      stdio: capture ? ["ignore", "pipe", "pipe"] : "inherit",
    });
    child.stdout?.on("data", (d) => chunks.push(d));
    child.stderr?.on("data", (d) => chunks.push(d));
    let timedOut = false;
    const timer = setTimeout(() => {
      timedOut = true;
      killTree(child.pid);
    }, left);
    child.on("error", (err) => {
      clearTimeout(timer);
      resolve({ ok: false, status: null, out: err.message });
    });
    child.on("close", (status) => {
      clearTimeout(timer);
      if (timedOut) return reject(new EnvironmentError("the 45-minute overall deadline passed"));
      resolve({ ok: status === 0, status, out: Buffer.concat(chunks).toString("utf8").trim() });
    });
  });
}

const compose = (argv, opts) => exec("docker", ["compose", "-p", PROJECT, ...COMPOSE_FILES.flatMap((f) => ["-f", f]), ...argv], opts);

async function requireDocker() {
  const info = await exec("docker", ["info", "--format", "{{.ServerVersion}}"], { capture: true });
  if (!info.ok) throw new EnvironmentError("the Docker daemon is not reachable; start Docker Desktop");
}

async function portIsTaken(port) {
  const connects = (host) =>
    new Promise((resolve) => {
      const socket = net.connect({ host, port });
      socket.setTimeout(1000);
      socket.on("connect", () => {
        socket.destroy();
        resolve(true);
      });
      socket.on("timeout", () => {
        socket.destroy();
        resolve(false);
      });
      socket.on("error", () => resolve(false));
    });
  const cannotListen = () =>
    new Promise((resolve) => {
      const server = net.createServer();
      server.once("error", () => resolve(true));
      server.listen({ port, host: "0.0.0.0", exclusive: true }, () => server.close(() => resolve(false)));
    });
  // Sequential: the listen probe would otherwise answer the connect probes.
  return (await connects("127.0.0.1")) || (await connects("::1")) || (await cannotListen());
}

async function requireFreePorts() {
  const taken = [];
  for (const port of PORTS) if (await portIsTaken(port)) taken.push(port);
  if (taken.length) throw new EnvironmentError(`port ${taken.join(", ")} is already in use; the e2e stack needs ${PORTS.join(", ")}`);
}

async function installDeps() {
  if (!existsSync(PLAYWRIGHT_CLI)) {
    console.log("\n==> e2e: npm ci");
    const ci = await exec(NPM, ["ci"], { cwd: E2E_DIR, shell: true });
    if (!ci.ok) throw new EnvironmentError("npm ci failed in e2e/");
  }
  console.log("\n==> e2e: playwright install chromium");
  const browser = await exec(process.execPath, [PLAYWRIGHT_CLI, "install", "chromium"], { cwd: E2E_DIR });
  if (!browser.ok) throw new EnvironmentError("could not install Chromium for Playwright");
}

async function buildImages() {
  console.log("\n==> e2e: docker compose build (working tree)");
  const built = await compose(["build"]);
  if (!built.ok) throw new EnvironmentError("docker compose build failed");
}

async function startStack() {
  console.log("\n==> e2e: docker compose up --wait");
  const up = await compose(["up", "-d", "--wait", "--wait-timeout", String(START_DEADLINE_SEC)]);
  if (!up.ok) throw new EnvironmentError(`the stack was not healthy within ${START_DEADLINE_SEC / 60} minutes`);
}

async function stopStack() {
  console.log("\n==> e2e: docker compose down --volumes");
  await compose(["down", "--volumes", "--remove-orphans"]).catch(() => undefined);
}

async function saveStackLogs() {
  mkdirSync(TEST_RESULTS_DIR, { recursive: true });
  const logs = await compose(["logs", "--no-color", "--timestamps"], { capture: true }).catch(() => ({ out: "(stack logs unavailable)" }));
  writeFileSync(path.join(TEST_RESULTS_DIR, "stack.log"), logs.out);
}

function deepestFailingStep(steps) {
  for (const step of steps ?? []) {
    if (!step.error) continue;
    return deepestFailingStep(step.steps) ?? step.title;
  }
  return null;
}

function firstLine(text) {
  return String(text ?? "")
    .replace(/\u001b\[[0-9;]*m/g, "")
    .split(/\r?\n/)
    .find((l) => l.trim()) ?? "";
}

function collectTests(suite, out) {
  for (const spec of suite.specs ?? []) {
    for (const test of spec.tests ?? []) out.push({ spec, test });
  }
  for (const child of suite.suites ?? []) collectTests(child, out);
}

function readOutcome() {
  if (!existsSync(RESULTS_JSON)) return null;
  let report;
  try {
    report = JSON.parse(readFileSync(RESULTS_JSON, "utf8"));
  } catch {
    return null;
  }
  const all = [];
  for (const suite of report.suites ?? []) collectTests(suite, all);
  const failed = [];
  const flaky = [];
  for (const { spec, test } of all) {
    if (test.status === "flaky") flaky.push(spec.title);
    if (test.status !== "unexpected") continue;
    const last = test.results.at(-1) ?? {};
    const trace = (last.attachments ?? []).find((a) => a.name === "trace")?.path ?? null;
    failed.push({
      title: spec.title,
      file: spec.file,
      step: deepestFailingStep(last.steps),
      error: firstLine(last.error?.message ?? last.errors?.[0]?.message),
      trace: trace ? path.relative(REPO_ROOT, trace).replaceAll("\\", "/") : null,
    });
  }
  return { total: all.length, failed, flaky };
}

function writeResult(result) {
  writeFileSync(resultFile(), JSON.stringify(result));
  console.log(`E2E_RESULT: ${JSON.stringify(result)}`);
}

async function runPlaywright(args) {
  rmSync(TEST_RESULTS_DIR, { recursive: true, force: true });
  const argv = [PLAYWRIGHT_CLI, "test"];
  if (args.headed) argv.push("--headed");
  if (args.ui) argv.push("--ui");
  if (args.headed || args.ui) argv.push("--retries=0");
  if (args.grep) argv.push("--grep", args.grep);
  console.log("\n==> e2e: playwright test");
  return exec(process.execPath, argv, { cwd: E2E_DIR });
}

async function run(args) {
  const startedAt = Date.now();
  deadlineAt = startedAt + OVERALL_DEADLINE_MS;
  claimPidFile();
  rmSync(resultFile(), { force: true });

  const result = { ok: false, class: "environment", failed: [], flaky: [], summary: "", reportPath: null, fingerprint: null, startedAt: new Date(startedAt).toISOString() };
  let stackUp = false;
  try {
    await requireDocker();
    await stopStack();
    await requireFreePorts();
    await installDeps();
    await buildImages();
    await startStack();
    stackUp = true;

    const playwright = await runPlaywright(args);
    const outcome = readOutcome();
    result.reportPath = existsSync(REPORT_DIR) ? path.relative(REPO_ROOT, REPORT_DIR).replaceAll("\\", "/") : null;
    if (!outcome) {
      result.summary = "Playwright produced no results (configuration or browser error)";
    } else if (outcome.failed.length > 0) {
      Object.assign(result, { class: "fail", failed: outcome.failed, flaky: outcome.flaky, summary: `${outcome.failed.length} of ${outcome.total} test(s) failed` });
      await saveStackLogs();
    } else if (!playwright.ok) {
      result.summary = `Playwright exited ${playwright.status} without a failed test`;
    } else {
      Object.assign(result, { ok: true, class: "pass", flaky: outcome.flaky, summary: `${outcome.total} test(s) passed` });
    }
  } catch (err) {
    if (!(err instanceof EnvironmentError)) throw err;
    result.summary = err.message;
    if (stackUp || err.message.includes("not healthy")) await saveStackLogs();
  } finally {
    if (args.keep && result.class !== "environment") console.log(`\nstack left up (--keep); stop it with: node scripts/e2e.mjs down`);
    else {
      deadlineAt = Date.now() + 5 * 60 * 1000;
      await stopStack();
    }
    result.fingerprint = fingerprint();
    result.finishedAt = new Date().toISOString();
  }
  // The pid file goes last: --await reads a missing pid file as a dead run.
  writeResult(result);
  releasePidFile();
  return EXIT[result.class];
}

function readPidInfo() {
  try {
    return JSON.parse(readFileSync(pidFile(), "utf8"));
  } catch {
    return null;
  }
}

function isAlive(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

function claimPidFile() {
  const previous = readPidInfo();
  if (previous && previous.pid !== process.pid && isAlive(previous.pid) && Date.now() < (previous.deadlineAt ?? 0)) {
    killTree(previous.pid);
    console.log(`e2e.mjs: stopped the previous run (pid ${previous.pid})`);
  }
  writeFileSync(pidFile(), JSON.stringify({ pid: process.pid, startedAt: Date.now(), deadlineAt }));
}

function releasePidFile() {
  if (readPidInfo()?.pid === process.pid) rmSync(pidFile(), { force: true });
}

function detach() {
  const file = resultFile();
  rmSync(file, { force: true });
  const logPath = `${file}.log`;
  const fd = openSync(logPath, "w");
  const argv = process.argv.slice(2).filter((a) => a !== "--detach");
  const child = spawn(process.execPath, [fileURLToPath(import.meta.url), ...argv], { cwd: process.cwd(), detached: true, stdio: ["ignore", fd, fd], windowsHide: true });
  child.unref();
  console.log(`E2E_STARTED: pid ${child.pid} — log ${logPath} — wait with: node scripts/e2e.mjs run --await`);
}

async function awaitResult(maxMin) {
  const startedAt = Date.now();
  const giveUpAt = startedAt + maxMin * 60 * 1000;
  const read = () => (existsSync(resultFile()) ? readFileSync(resultFile(), "utf8").trim() : null);
  for (;;) {
    const json = read();
    if (json) {
      console.log(`E2E_RESULT: ${json}`);
      return EXIT[JSON.parse(json).class] ?? EXIT.fail;
    }
    const info = readPidInfo();
    const started = Date.now() >= startedAt + 10_000;
    if (started && (!info || !isAlive(info.pid))) {
      const lost = { ok: false, class: "environment", failed: [], flaky: [], summary: "e2e.mjs stopped without a result (process gone)" };
      console.log(`E2E_RESULT: ${JSON.stringify(lost)}`);
      return EXIT.environment;
    }
    if (Date.now() >= giveUpAt) {
      console.log("E2E_PENDING: .git/e2e-result.json not written yet — e2e.mjs is still running — call --await again");
      return EXIT.pending;
    }
    await new Promise((r) => setTimeout(r, 2000));
  }
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  switch (args.command) {
    case "run":
      if (args.await) return awaitResult(args.maxMin);
      if (args.detach) return detach();
      return run(args);
    case "up":
      deadlineAt = Date.now() + OVERALL_DEADLINE_MS;
      try {
        await requireDocker();
        await stopStack();
        await requireFreePorts();
        await buildImages();
        await startStack();
        console.log("\ne2e stack up: web http://localhost:18080 · market stub http://localhost:18081/__requests · dashboard http://localhost:18898");
        return 0;
      } catch (err) {
        if (!(err instanceof EnvironmentError)) throw err;
        console.error(err.message);
        return EXIT.environment;
      }
    case "down":
      deadlineAt = Date.now() + 5 * 60 * 1000;
      await requireDocker();
      await stopStack();
      return 0;
    case "status":
      deadlineAt = Date.now() + 60 * 1000;
      return (await compose(["ps", "--all"])).ok ? 0 : 2;
    case "report":
      if (!existsSync(REPORT_DIR)) fatal("no HTML report yet; run: node scripts/e2e.mjs run");
      return (await exec(process.execPath, [PLAYWRIGHT_CLI, "show-report", REPORT_DIR], { cwd: E2E_DIR })).ok ? 0 : 2;
    default:
      return fatal("usage: node scripts/e2e.mjs <run [--headed|--ui] [--grep <p>] [--keep] [--detach|--await]|up|down|status|report>");
  }
}

main().then(
  (code) => process.exit(code ?? 0),
  (err) => fatal(`e2e.mjs crashed: ${err?.stack ?? err}`),
);
