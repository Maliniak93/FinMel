#!/usr/bin/env node
// Preflight for /build and /fix: checks that everything the build pipeline needs is up, and starts or
// fixes what can be fixed automatically.
//
// Usage: node scripts/preflight.mjs [--dry-run] [--no-web] [--docker-timeout <seconds>]
//
//   --dry-run                 only check — never start, stop or install anything. What would have been
//                             fixed is reported as a failure carrying the fix text.
//   --no-web                  skip the `web-deps` check (no npm ci).
//   --docker-timeout <s>      how long to wait for the Docker daemon after starting it (default 180).
//
// Checks, in this order (each yields {name, status: "ok"|"fixed"|"fail", detail, fix?}). All of them
// always run, so every problem shows up at once:
//   node      version meets the Angular 22 CLI floor (>=22.22.3 on 22.x, >=24.15 on 24.x, >=26)   report only
//   dotnet    a .NET 10 SDK is installed                                                          report only
//   gh        installed, logged in, token scopes include `repo` and `project`                     report only
//   git       inside the repo and remote `origin` exists                                          report only
//   docker    daemon reachable (Testcontainers need it); starts Docker Desktop when it is down    auto-fix
//   stack     stops a running local stack (Aspire AppHost, services, ng serve) via stop-stack.mjs auto-fix
//   web-deps  `npm ci` in web/ when node_modules is missing or older than package-lock.json       auto-fix
//
// Output: one human progress line per check on stderr; the final line of stdout is always exactly
// `PREFLIGHT_RESULT: <json>` with {"ok": bool, "checks": [...]} and nothing is printed after it.
// Exit code 0 when ok, 1 otherwise (2 on a bad command line).
//
// Node >= 22, ESM, zero npm dependencies. Runs from any cwd: the repo root comes from this file's
// location and every child process gets an explicit cwd. Also importable:
// `import { preflight } from "./preflight.mjs"` (synchronous; progress goes to opts.log, default stderr).

import { spawn, spawnSync } from "node:child_process";
import { existsSync, statSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { stopStack } from "./stop-stack.mjs";

const IS_WIN = process.platform === "win32";
const IS_MAC = process.platform === "darwin";
const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const WEB_DIR = path.join(REPO_ROOT, "web");
const NPM = IS_WIN ? "npm.cmd" : "npm";
const DEFAULT_DOCKER_TIMEOUT_S = 180;
const DOCKER_INFO_TIMEOUT_MS = 20_000;
const NPM_CI_TIMEOUT_MS = 10 * 60 * 1000;

const sleep = (ms) => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);

const ok = (name, detail) => ({ name, status: "ok", detail });
const fixed = (name, detail) => ({ name, status: "fixed", detail });
const fail = (name, detail, fix) => ({ name, status: "fail", detail, ...(fix ? { fix } : {}) });

// Runs a command without a shell (gh, dotnet, docker, git are real executables everywhere).
function run(cmd, args, { cwd = REPO_ROOT, timeout = 30_000 } = {}) {
  const res = spawnSync(cmd, args, { cwd, encoding: "utf8", timeout, windowsHide: true, maxBuffer: 16 * 1024 * 1024 });
  return {
    status: res.status,
    out: `${res.stdout ?? ""}${res.stderr ?? ""}`,
    missing: res.error?.code === "ENOENT",
    timedOut: res.error?.code === "ETIMEDOUT",
    error: res.error,
  };
}

const firstLine = (s) => s.split(/\r?\n/).map((l) => l.trim()).find(Boolean) ?? "";
const lastLines = (s, n = 8) => s.split(/\r?\n/).filter((l) => l.trim()).slice(-n).join("\n");

// ---------------------------------------------------------------------------------------------
// Report-only checks
// ---------------------------------------------------------------------------------------------

function checkNode() {
  const v = process.versions.node;
  const [major, minor, patch] = v.split(".").map(Number);
  const cmp = (a, b) => a[0] - b[0] || a[1] - b[1] || a[2] - b[2];
  let good;
  if (major === 22) good = cmp([major, minor, patch], [22, 22, 3]) >= 0;
  else if (major === 24) good = cmp([major, minor, patch], [24, 15, 0]) >= 0;
  else good = major >= 26;
  if (good) return ok("node", `v${v}`);
  return fail(
    "node",
    `v${v} is below the Angular 22 CLI floor (>= 22.22.3, >= 24.15 or >= 26; 23.x and 25.x are unsupported)`,
    "install Node 26 (or 24.15+ / 22.22.3+) and put it first on PATH",
  );
}

function checkDotnet() {
  const res = run("dotnet", ["--list-sdks"]);
  if (res.missing) return fail("dotnet", "dotnet is not installed", "install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0");
  const sdks = res.out.split(/\r?\n/).map((l) => l.trim().split(/\s+/)[0]).filter((v) => /^\d+\./.test(v));
  const ten = sdks.filter((v) => v.startsWith("10."));
  if (res.status === 0 && ten.length) return ok("dotnet", `SDK ${ten[ten.length - 1]}`);
  return fail(
    "dotnet",
    sdks.length ? `no .NET 10 SDK (found ${sdks.join(", ")})` : `could not list SDKs: ${firstLine(res.out)}`,
    "install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0",
  );
}

function checkGh() {
  const res = run("gh", ["auth", "status"]);
  if (res.missing) return fail("gh", "gh CLI is not installed", "install GitHub CLI (https://cli.github.com), then `! gh auth login`");
  if (res.status !== 0) return fail("gh", `not logged in: ${firstLine(res.out)}`, "! gh auth login");
  const line = res.out.split(/\r?\n/).find((l) => /Token scopes:/i.test(l));
  if (!line) return ok("gh", "logged in (token scopes not reported)");
  const scopes = [...line.matchAll(/'([^']+)'/g)].map((m) => m[1]);
  const missing = ["repo", "project"].filter((s) => !scopes.includes(s));
  if (!missing.length) return ok("gh", `logged in, scopes: ${scopes.join(", ")}`);
  return fail("gh", `token lacks scope(s): ${missing.join(", ")}`, `! gh auth refresh -s ${missing.join(",")}`);
}

function checkGit() {
  const inside = run("git", ["rev-parse", "--is-inside-work-tree"]);
  if (inside.missing) return fail("git", "git is not installed", "install git");
  if (inside.status !== 0 || firstLine(inside.out) !== "true") {
    return fail("git", `${REPO_ROOT} is not inside a git work tree`, "run from a clone of the repo (`git clone` it)");
  }
  const origin = run("git", ["remote", "get-url", "origin"]);
  if (origin.status !== 0) return fail("git", "remote `origin` is not configured", "git remote add origin <url of the FinMel repo>");
  return ok("git", `origin ${firstLine(origin.out)}`);
}

// ---------------------------------------------------------------------------------------------
// docker
// ---------------------------------------------------------------------------------------------

const dockerInfo = () => run("docker", ["info"], { timeout: DOCKER_INFO_TIMEOUT_MS });

function launchDocker() {
  // Returns {started: true, how} or {started: false, reason, fix?}.
  if (IS_WIN) {
    // Docker Desktop CLI plugin: `docker desktop start [-d|--detach] [--timeout s]`.
    const cli = run("docker", ["desktop", "start", "--detach"], { timeout: 60_000 });
    if (cli.status === 0) return { started: true, how: "docker desktop start" };
    const exe = path.join(process.env.ProgramFiles || "C:\\Program Files", "Docker", "Docker", "Docker Desktop.exe");
    if (!existsSync(exe)) return { started: false, reason: `\`docker desktop start\` failed and ${exe} does not exist` };
    spawn(exe, [], { detached: true, stdio: "ignore" }).unref();
    return { started: true, how: "Docker Desktop.exe" };
  }
  if (IS_MAC) {
    const res = run("open", ["-a", "Docker"]);
    return res.status === 0 ? { started: true, how: "open -a Docker" } : { started: false, reason: `open -a Docker failed: ${firstLine(res.out)}` };
  }
  return { started: false, reason: "the Docker daemon is not running (not started automatically on Linux)", fix: "sudo systemctl start docker" };
}

function checkDocker({ dryRun, dockerTimeoutS }) {
  const first = dockerInfo();
  if (first.missing) return fail("docker", "docker CLI is not installed", "install Docker Desktop (https://www.docker.com/products/docker-desktop)");
  if (first.status === 0) return ok("docker", "daemon reachable");

  const startFix = IS_WIN ? "docker desktop start" : IS_MAC ? "open -a Docker" : "sudo systemctl start docker";
  if (dryRun) return fail("docker", "daemon is not reachable (dry run: not started)", startFix);

  const t0 = Date.now();
  const launched = launchDocker();
  if (!launched.started) return fail("docker", launched.reason, launched.fix ?? startFix);

  const deadline = t0 + dockerTimeoutS * 1000;
  while (Date.now() < deadline) {
    sleep(3000);
    if (dockerInfo().status === 0) {
      return fixed("docker", `started Docker Desktop via ${launched.how} (${Math.round((Date.now() - t0) / 1000)} s)`);
    }
  }
  return fail(
    "docker",
    `started Docker Desktop via ${launched.how}, but the daemon was still unreachable after ${dockerTimeoutS} s`,
    "open Docker Desktop, wait until it says \"Engine running\" (or raise --docker-timeout), then re-run",
  );
}

// ---------------------------------------------------------------------------------------------
// stack + web deps
// ---------------------------------------------------------------------------------------------

const describeProcs = (list) => list.map((p) => `${p.what} ${p.name} (pid ${p.pid})`).join(", ");

function checkStack({ dryRun }) {
  let res;
  try {
    res = stopStack({ dryRun });
  } catch (e) {
    return fail("stack", `could not inspect processes: ${e.message}`, "node scripts/stop-stack.mjs");
  }
  if (!res.stopped.length) return ok("stack", "no local stack running");
  if (dryRun) return fail("stack", `local stack is running: ${describeProcs(res.stopped)}`, "node scripts/stop-stack.mjs");
  if (res.left?.length) return fail("stack", `could not stop: ${describeProcs(res.left)}`, "stop them by hand, then re-run");
  return fixed("stack", `stopped ${describeProcs(res.stopped)}`);
}

function mtime(p) {
  try {
    return statSync(p).mtimeMs;
  } catch {
    return null;
  }
}

function checkWebDeps({ dryRun, web }) {
  if (!web) return ok("web-deps", "skipped");
  const modules = path.join(WEB_DIR, "node_modules");
  const lock = mtime(path.join(WEB_DIR, "package-lock.json"));
  const installed = mtime(path.join(modules, ".package-lock.json"));
  let reason = null;
  if (!existsSync(modules)) reason = "web/node_modules is missing";
  else if (lock !== null && (installed === null || lock > installed)) reason = "web/package-lock.json is newer than node_modules";
  if (!reason) return ok("web-deps", "node_modules is up to date");
  if (dryRun) return fail("web-deps", `${reason} (dry run: not installed)`, "cd web && npm ci");

  const res = spawnSync(`${NPM} ci`, { cwd: WEB_DIR, shell: true, encoding: "utf8", timeout: NPM_CI_TIMEOUT_MS, windowsHide: true, maxBuffer: 64 * 1024 * 1024 });
  if (res.status === 0) return fixed("web-deps", `${reason} — ran npm ci`);
  const why = res.error?.code === "ETIMEDOUT" ? `npm ci timed out after ${NPM_CI_TIMEOUT_MS / 60000} min` : "npm ci failed";
  return fail("web-deps", `${why}:\n${lastLines(`${res.stdout ?? ""}${res.stderr ?? ""}`)}`, "cd web && npm ci  (stop anything holding node_modules first)");
}

// ---------------------------------------------------------------------------------------------
// Orchestration
// ---------------------------------------------------------------------------------------------

function progressLine(c) {
  const mark = c.status === "fail" ? "\u2717" : "\u2713";
  const tag = c.status === "fixed" ? " [fixed]" : "";
  let line = `${mark} ${c.name}${tag} \u2014 ${c.detail}`;
  if (c.fix) line += ` \u2192 ${c.fix}`;
  return line;
}

export function preflight({ dryRun = false, web = true, dockerTimeoutS = DEFAULT_DOCKER_TIMEOUT_S, log = (s) => process.stderr.write(`${s}\n`) } = {}) {
  const steps = [
    () => checkNode(),
    () => checkDotnet(),
    () => checkGh(),
    () => checkGit(),
    () => checkDocker({ dryRun, dockerTimeoutS }),
    () => checkStack({ dryRun }),
    () => checkWebDeps({ dryRun, web }),
  ];
  const checks = [];
  for (const step of steps) {
    let result;
    try {
      result = step();
    } catch (e) {
      result = fail("preflight", `unexpected error: ${e.message}`);
    }
    checks.push(result);
    log(progressLine(result));
  }
  return { ok: checks.every((c) => c.status !== "fail"), checks };
}

function parseArgs(argv) {
  const args = { dryRun: false, web: true, dockerTimeoutS: DEFAULT_DOCKER_TIMEOUT_S };
  const seconds = (v) => {
    const n = Number(v);
    if (!Number.isFinite(n) || n <= 0) {
      process.stderr.write(`preflight: --docker-timeout needs a positive number of seconds, got '${v}'\n`);
      process.exit(2);
    }
    return n;
  };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === "--dry-run") args.dryRun = true;
    else if (a === "--no-web") args.web = false;
    else if (a === "--docker-timeout") args.dockerTimeoutS = seconds(argv[++i]);
    else if (a.startsWith("--docker-timeout=")) args.dockerTimeoutS = seconds(a.slice("--docker-timeout=".length));
    else {
      process.stderr.write(`preflight: unknown argument '${a}'\n`);
      process.exit(2);
    }
  }
  return args;
}

if (process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1])) {
  const result = preflight(parseArgs(process.argv.slice(2)));
  console.log(`PREFLIGHT_RESULT: ${JSON.stringify(result)}`);
  process.exitCode = result.ok ? 0 : 1;
}
