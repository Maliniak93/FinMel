#!/usr/bin/env node
// Usage: node scripts/verify.mjs [--quick] [--projects a,b] [--web] [--api] [--all] [--fix] [--deadline-min N] [--out <file>] [--await <file> [--max-min N]]
//   (no flags)        affected .NET test projects and web/api checks, picked from the files changed against master
//   --quick           format + build only; overrides everything below
//   --projects a,b    exactly these .NET test projects; web/api still need --web/--api/--all
//   --web             force the web checks
//   --api             force the generated TS client check
//   --all             every test project + web + api
//   --fix             first reformat the changed files (dotnet format, prettier, eslint --fix)
//   --deadline-min N  wall-clock budget for the whole run (default 60)
//   --out <file>      also write the result JSON to <file>
//   --await <file>    do not verify: wait (at most --max-min N, default 9) for a background run's --out file

import { spawn, spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { existsSync, readdirSync, readFileSync, renameSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { stopStack } from "./stop-stack.mjs";

const IS_WIN = process.platform === "win32";
const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const WEB_DIR = path.join(REPO_ROOT, "web");
const NPM = IS_WIN ? "npm.cmd" : "npm";
const NPX = IS_WIN ? "npx.cmd" : "npx";
const DEFAULT_TIMEOUT_MS = 60 * 60 * 1000;
const DEFAULT_DEADLINE_MIN = 60;

let deadlineAt = Infinity;
let outFile = null;

function parseArgs(argv) {
  const args = {
    quick: false,
    all: false,
    web: false,
    api: false,
    fix: false,
    projects: null,
    deadlineMin: DEFAULT_DEADLINE_MIN,
    out: null,
    await: null,
    maxMin: 9,
  };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === "--quick") args.quick = true;
    else if (a === "--all") args.all = true;
    else if (a === "--web") args.web = true;
    else if (a === "--api") args.api = true;
    else if (a === "--fix") args.fix = true;
    else if (a === "--deadline-min" || a.startsWith("--deadline-min=")) {
      const val = a.includes("=") ? a.slice(a.indexOf("=") + 1) : argv[++i];
      const n = Number(val);
      if (!(n > 0)) fatal("--deadline-min requires a positive number of minutes");
      args.deadlineMin = n;
    } else if (a === "--out" || a.startsWith("--out=")) {
      const val = a.includes("=") ? a.slice(a.indexOf("=") + 1) : argv[++i];
      if (!val) fatal("--out requires a file path");
      args.out = path.resolve(val);
    } else if (a === "--await" || a.startsWith("--await=")) {
      const val = a.includes("=") ? a.slice(a.indexOf("=") + 1) : argv[++i];
      if (!val) fatal("--await requires a file path");
      args.await = path.resolve(val);
    } else if (a === "--max-min" || a.startsWith("--max-min=")) {
      const val = a.includes("=") ? a.slice(a.indexOf("=") + 1) : argv[++i];
      const n = Number(val);
      if (!(n > 0)) fatal("--max-min requires a positive number of minutes");
      args.maxMin = n;
    } else if (a === "--projects") {
      const val = argv[++i];
      if (!val) fatal("--projects requires a comma-separated value");
      args.projects = splitList(val);
    } else if (a.startsWith("--projects=")) {
      args.projects = splitList(a.slice("--projects=".length));
    } else {
      fatal(`unknown argument '${a}'`);
    }
  }
  return args;
}

function splitList(s) {
  return s
    .split(",")
    .map((x) => x.trim())
    .filter(Boolean);
}

function discoverServiceNames() {
  const servicesDir = path.join(REPO_ROOT, "services");
  if (!existsSync(servicesDir)) return [];
  return readdirSync(servicesDir, { withFileTypes: true })
    .filter((d) => d.isDirectory())
    .map((d) => d.name)
    .sort();
}

function buildProjectMap(serviceNames) {
  const map = new Map();
  const add = (shortName, rel) => {
    if (existsSync(path.join(REPO_ROOT, rel))) map.set(shortName.toLowerCase(), { shortName, rel });
  };
  for (const svc of serviceNames) {
    add(svc, `services/${svc}/Skarbiec.${svc}.Tests/Skarbiec.${svc}.Tests.csproj`);
  }
  add("Gateway", "gateway/Skarbiec.Gateway.Tests/Skarbiec.Gateway.Tests.csproj");
  add("Contracts", "contracts/Skarbiec.Contracts.Tests/Skarbiec.Contracts.Tests.csproj");
  add("ServiceDefaults", "Skarbiec.ServiceDefaults.Tests/Skarbiec.ServiceDefaults.Tests.csproj");
  add("Testing", "Skarbiec.Testing.Tests/Skarbiec.Testing.Tests.csproj");
  return map;
}

function resolveProjectsArg(tokens, projectMap) {
  const rels = [];
  const unknown = [];
  for (const token of tokens) {
    const hit = projectMap.get(token.toLowerCase());
    if (hit) {
      rels.push(hit.rel);
      continue;
    }
    const asRel = normalizeSlashes(token);
    if (existsSync(path.join(REPO_ROOT, asRel)) || existsSync(token)) {
      rels.push(asRel);
    } else {
      unknown.push(token);
    }
  }
  return { rels, unknown };
}

function normalizeSlashes(p) {
  return p.replace(/\\/g, "/");
}

function splitNonEmptyLines(text) {
  return text
    .split(/\r?\n/)
    .map((s) => s.trim())
    .filter(Boolean);
}

function gitOutput(args) {
  const res = runSync("git", args, { cwd: REPO_ROOT, timeoutMs: 15_000 });
  return res.ok ? res.stdout : null;
}

function getChangedFiles() {
  for (const base of ["master...HEAD", "origin/master...HEAD"]) {
    const out = gitOutput(["diff", "--name-only", base]);
    if (out !== null) {
      const diffFiles = splitNonEmptyLines(out);
      const statusOut = gitOutput(["status", "--porcelain"]) ?? "";
      const statusFiles = parsePorcelainStatus(statusOut);
      return { files: [...new Set([...diffFiles, ...statusFiles])], treatAsAll: false, base };
    }
  }
  return { files: [], treatAsAll: true, base: null };
}

function parsePorcelainStatus(output) {
  const files = [];
  for (const line of output.split(/\r?\n/)) {
    if (!line) continue;
    const status = line.slice(0, 2);
    const rest = line.slice(3);
    if (status[0] === "R" || status[0] === "C" || status[1] === "R" || status[1] === "C") {
      const arrow = rest.indexOf(" -> ");
      if (arrow !== -1) {
        files.push(unquotePath(rest.slice(0, arrow)));
        files.push(unquotePath(rest.slice(arrow + 4)));
        continue;
      }
    }
    files.push(unquotePath(rest));
  }
  return files.filter(Boolean);
}

function unquotePath(raw) {
  const s = raw.trim();
  if (s.startsWith('"') && s.endsWith('"')) {
    try {
      return JSON.parse(s);
    } catch {
    }
  }
  return s;
}

const ROOT_BUILD_FILES = new Set(["directory.build.props", "directory.packages.props", "skarbiec.slnx"]);

function isApiSurfaceFile(lower) {
  if (/^services\/[^/]+\/skarbiec\.[^/]+\/features\//.test(lower)) return true;
  if (/(^|\/)[^/]*request\.cs$/.test(lower)) return true;
  if (/(^|\/)[^/]*response\.cs$/.test(lower)) return true;
  if (/^services\/[^/]+\/skarbiec\.[^/]+\/program\.cs$/.test(lower)) return true;
  return false;
}

function computeAffected(changedFiles, serviceNames) {
  const affectedKeys = new Set();
  const serviceKeys = serviceNames.map((s) => s.toLowerCase());
  let web = false;
  let api = false;

  for (const raw of changedFiles) {
    const f = normalizeSlashes(raw);
    const lower = f.toLowerCase();

    if (!f.includes("/") && ROOT_BUILD_FILES.has(lower)) {
      affectedKeys.add("__all__");
    } else if (lower.startsWith("gateway/")) {
      affectedKeys.add("gateway");
    } else if (lower.startsWith("contracts/")) {
      affectedKeys.add("contracts");
      affectedKeys.add("gateway");
      for (const k of serviceKeys) affectedKeys.add(k);
    } else if (lower.startsWith("skarbiec.servicedefaults/")) {
      affectedKeys.add("servicedefaults");
      affectedKeys.add("gateway");
      for (const k of serviceKeys) affectedKeys.add(k);
    } else if (lower.startsWith("skarbiec.testing/")) {
      affectedKeys.add("testing");
      affectedKeys.add("gateway");
      for (const k of serviceKeys) affectedKeys.add(k);
    } else {
      for (const svc of serviceKeys) {
        if (lower.startsWith(`services/${svc}/`)) {
          affectedKeys.add(svc);
          break;
        }
      }
    }

    if (lower.startsWith("web/")) web = true;
    if (isApiSurfaceFile(lower)) api = true;
  }

  return { affectedKeys, web, api };
}

function quoteArg(a) {
  const s = String(a);
  if (s === "") return '""';
  if (/[\s"&|<>^%]/.test(s)) return `"${s.replace(/"/g, '\\"')}"`;
  return s;
}

function runSync(bin, args, { cwd = REPO_ROOT, timeoutMs = DEFAULT_TIMEOUT_MS } = {}) {
  const cmdStr = [bin, ...args].map(quoteArg).join(" ");
  const startedAt = Date.now();
  const res = spawnSync(cmdStr, {
    cwd,
    shell: true,
    encoding: "utf8",
    maxBuffer: 256 * 1024 * 1024,
    timeout: timeoutMs,
  });
  const durationMs = Date.now() - startedAt;
  const timedOut = res.status === null && res.signal != null && !res.error;
  return {
    ok: !res.error && !timedOut && res.status === 0,
    status: res.status,
    signal: res.signal,
    timedOut,
    stdout: res.stdout ?? "",
    stderr: res.stderr ?? "",
    error: res.error ?? null,
    durationMs,
    cmdStr,
  };
}

function killTree(pid) {
  if (!pid) return;
  if (IS_WIN) {
    spawnSync("taskkill", ["/PID", String(pid), "/T", "/F"], { encoding: "utf8" });
    return;
  }
  try {
    process.kill(-pid, "SIGKILL");
  } catch {
  }
}

// Async so a timeout can kill the whole process tree while the shell is still alive.
function runCommand(bin, args, { cwd = REPO_ROOT, timeoutMs = DEFAULT_TIMEOUT_MS } = {}) {
  const cmdStr = [bin, ...args].map(quoteArg).join(" ");
  const startedAt = Date.now();
  const left = deadlineAt - startedAt;
  const budget = Math.min(timeoutMs, left);
  const base = { cmdStr, signal: null, error: null };
  if (budget <= 0) {
    return Promise.resolve({ ...base, ok: false, status: null, timedOut: true, deadlineHit: true, stdout: "", stderr: "", durationMs: 0 });
  }
  return new Promise((resolve) => {
    const out = [];
    const err = [];
    let settled = false;
    let timedOut = false;
    const child = spawn(cmdStr, { cwd, shell: true, windowsHide: true, detached: !IS_WIN });
    child.stdout.on("data", (d) => out.push(d));
    child.stderr.on("data", (d) => err.push(d));
    const timer = setTimeout(() => {
      timedOut = true;
      killTree(child.pid);
    }, budget);
    const done = (status, signal, error) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      resolve({
        ...base,
        ok: !error && !timedOut && status === 0,
        status,
        signal,
        error: error ?? null,
        timedOut,
        deadlineHit: timedOut && left <= timeoutMs,
        stdout: Buffer.concat(out).toString("utf8"),
        stderr: Buffer.concat(err).toString("utf8"),
        durationMs: Date.now() - startedAt,
      });
    };
    child.on("error", (e) => done(null, null, e));
    child.on("close", (code, signal) => done(code, signal, null));
  });
}

function hashOf(rel) {
  try {
    return createHash("sha1")
      .update(readFileSync(path.join(REPO_ROOT, rel)))
      .digest("hex");
  } catch {
    return null;
  }
}

// cmd.exe caps a command line at 8191 characters.
function chunks(list, size = 40) {
  const result = [];
  for (let i = 0; i < list.length; i += size) result.push(list.slice(i, i + size));
  return result;
}

const WEB_GENERATED_RE = /^web\/(node_modules|dist|openapi|src\/app\/api)\//i;

async function applyFixes(changedFiles) {
  const existing = [...new Set(changedFiles.map(normalizeSlashes))].filter((f) => existsSync(path.join(REPO_ROOT, f)));
  const cs = existing.filter((f) => f.toLowerCase().endsWith(".cs"));
  const web = existing.filter((f) => /^web\//i.test(f) && !WEB_GENERATED_RE.test(f)).map((f) => f.slice("web/".length));
  const lintable = web.filter((f) => /\.(ts|html)$/i.test(f));
  const prettierable = web.filter((f) => /\.(ts|html|scss|css|json|md|mjs|js)$/i.test(f));
  const targets = [...cs, ...prettierable.map((f) => `web/${f}`)];
  const before = new Map(targets.map((f) => [f, hashOf(f)]));

  for (const part of chunks(cs)) {
    await step(`fix: dotnet format (${part.length} .cs file(s))`, () =>
      runCommand("dotnet", ["format", "Skarbiec.slnx", "--include", ...part]),
    );
  }
  for (const part of chunks(lintable)) {
    await step(`fix: eslint --fix (${part.length} file(s))`, () => runCommand(NPX, ["eslint", "--fix", ...part], { cwd: WEB_DIR }));
  }
  for (const part of chunks(prettierable)) {
    await step(`fix: prettier --write (${part.length} file(s))`, () =>
      runCommand(NPX, ["prettier", "--write", "--ignore-unknown", ...part], { cwd: WEB_DIR }),
    );
  }

  const rewritten = targets.filter((f) => hashOf(f) !== before.get(f));
  console.log(rewritten.length ? `\nfix: reformatted ${rewritten.length} file(s): ${rewritten.join(", ")}` : "\nfix: nothing to reformat");
}

const ANSI_RE = /\x1B\[[0-?]*[ -/]*[@-~]/g;
function stripAnsi(s) {
  return s.replace(ANSI_RE, "");
}

function formatDuration(ms) {
  return ms < 1000 ? `${ms}ms` : `${(ms / 1000).toFixed(1)}s`;
}

async function step(label, fn) {
  console.log(`\n==> ${label}`);
  const result = await fn();
  console.log(`    $ ${result.cmdStr}`);
  if (result.ok) {
    console.log(`    ok (${formatDuration(result.durationMs)})`);
  } else if (result.timedOut) {
    console.log(`    FAILED — timed out after ${formatDuration(result.durationMs)}`);
  } else {
    console.log(`    FAILED (${formatDuration(result.durationMs)}, exit ${result.status ?? "n/a"})`);
    printExcerpt(result);
  }
  return result;
}

function printExcerpt(result, maxLines = 20) {
  const combined = `${result.stdout}\n${result.stderr}`;
  const lines = combined
    .split(/\r?\n/)
    .map((l) => l.trimEnd())
    .filter(Boolean);
  for (const l of lines.slice(0, maxLines)) console.log(`    | ${l}`);
  if (lines.length > maxLines) console.log(`    | ... (${lines.length - maxLines} more line(s) omitted)`);
}

function truncate(s, max = 300) {
  if (!s) return "";
  return s.length <= max ? s : `${s.slice(0, max - 1)}…`;
}

function toRel(p) {
  if (!p) return p;
  const norm = normalizeSlashes(p);
  const rootNorm = normalizeSlashes(REPO_ROOT);
  if (norm.toLowerCase().startsWith(rootNorm.toLowerCase())) {
    return norm.slice(rootNorm.length).replace(/^\//, "");
  }
  return norm;
}

function isNoiseLine(t) {
  if (!t) return true;
  if (t.startsWith("> ")) return true;
  if (/^[⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏✔✓]+$/.test(t)) return true;
  return false;
}

function firstNonEmptyLines(text, n) {
  const lines = stripAnsi(text)
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter((l) => !isNoiseLine(l));
  return lines.slice(0, n).join(" | ");
}

const LOCKED_OUTPUT_RE = /\b(?:warning|error)\s+MSB30(?:21|26|27)\b/;

// The error/warning keyword and the rule code are stable across locales; only the message is localized.
const DIAG_RE = /^(.*?)\((\d+),(\d+)\):\s+(error|warning)\s+(\S+):\s*(.*?)\s*(?:\[(.*?)\])?$/;

function extractCompilerFailure(stepName, text) {
  const label = stepName === "format" ? "formatting" : stepName === "build" ? "build" : "compiler";
  const diags = [];
  for (const line of stripAnsi(text).split(/\r?\n/)) {
    const m = line.match(DIAG_RE);
    if (m) diags.push({ file: toRel(m[1]), line: m[2], severity: m[4], code: m[5] });
  }
  const errors = diags.filter((d) => d.severity === "error");
  const use = errors.length ? errors : diags;
  if (use.length === 0) {
    return { step: stepName, summary: truncate(firstNonEmptyLines(text, 5) || `${label} failed`), file: null };
  }
  const uniqueFiles = [...new Set(use.map((d) => d.file))];
  const first = use.slice(0, 3).map((d) => `${d.code} ${d.file}:${d.line}`);
  const summary = `${use.length} ${label} issue(s) across ${uniqueFiles.length} file(s); first: ${first.join("; ")}`;
  return { step: stepName, summary: truncate(summary), file: uniqueFiles[0] ?? null };
}

// xUnit's [FAIL] marker is not localized; a loose "Failed <word>" scan matches unrelated prose.
const XUNIT_FAIL_RE = /^\s*(.+?)\s+\[FAIL\]\s*$/;
const VSTEST_FAIL_RE = /^\s*(?:Failed|Niepowodzenie)\s+([A-Za-z_][\w]*(?:\.[\w]+)+(?:\([^)]*\))?)\s+\[\d+\s*(?:ms|s)\]\s*$/;
const SUMMARY_COUNT_RE = /(?:Failed!|Niepowodzenie!).*?(\d+)/;
const DOCKER_UNREACHABLE_RE = /\b(docker|testcontainers|npipe|dockerdesktoplinuxengine)\b/i;
const STACK_FILE_RE = /in\s+(.+?):line\s+\d+/;

// Testcontainers PDBs carry their own build-time paths, which point at no file in this repo.
function looksRepoRelative(rel) {
  return !!rel && !rel.startsWith("/") && !/^[A-Za-z]:/.test(rel);
}

function extractDotnetTestFailure(text) {
  const clean = stripAnsi(text);

  if (DOCKER_UNREACHABLE_RE.test(clean)) {
    return {
      step: "test",
      summary: "Docker daemon not reachable — start Docker Desktop (Testcontainers)",
      file: null,
    };
  }

  const names = [];
  let file = null;
  for (const line of clean.split(/\r?\n/)) {
    const m = line.match(XUNIT_FAIL_RE) || line.match(VSTEST_FAIL_RE);
    if (m && !names.includes(m[1])) names.push(m[1]);
    if (!file) {
      const fm = line.match(STACK_FILE_RE);
      if (fm) {
        const rel = toRel(fm[1]);
        if (looksRepoRelative(rel)) file = rel;
      }
    }
  }

  if (names.length > 0) {
    return {
      step: "test",
      summary: truncate(`${names.length} test(s) failed: ${names.slice(0, 5).join(", ")}`),
      file,
    };
  }

  const countMatch = clean.match(SUMMARY_COUNT_RE);
  if (countMatch) {
    return {
      step: "test",
      summary: `dotnet test reported ${countMatch[1]} failed test(s) (no per-test [FAIL] marker captured)`,
      file: null,
    };
  }

  return { step: "test", summary: truncate(firstNonEmptyLines(text, 5) || "dotnet test failed"), file: null };
}

const VITEST_FAILED_RE = /^\s*[×✗]\s+(.+?)\s*$/;

function extractVitestFailure(text) {
  const names = [];
  for (const line of stripAnsi(text).split(/\r?\n/)) {
    const m = line.match(VITEST_FAILED_RE);
    if (m) names.push(m[1]);
  }
  if (names.length === 0) return extractGenericFailure("web-test", text);
  return {
    step: "web-test",
    summary: truncate(`${names.length} test(s) failed: ${names.slice(0, 5).join(", ")}`),
    file: null,
  };
}

const GENERIC_FILE_RE = /([.\w][\w.\-/\\]*\.(ts|html|scss|css|json|js|mjs|cs))(?=[:)\s]|$)/;

function extractGenericFailure(stepName, text) {
  const summary = firstNonEmptyLines(text, 6);
  const m = stripAnsi(text).match(GENERIC_FILE_RE);
  return {
    step: stepName,
    summary: truncate(summary || `${stepName} failed (no output captured)`),
    file: m ? toRel(m[1]) : null,
  };
}

function buildFailure(stepName, result) {
  if (result.deadlineHit) {
    return {
      step: "timeout",
      summary: truncate(`run deadline reached during ${stepName} after ${formatDuration(result.durationMs)}: ${result.cmdStr}`),
      file: null,
    };
  }
  if (result.timedOut) {
    return { step: stepName, summary: truncate(`timed out after ${formatDuration(result.durationMs)}: ${result.cmdStr}`), file: null };
  }
  if (result.error) {
    return { step: stepName, summary: truncate(`failed to start: ${result.error.message}`), file: null };
  }
  const text = `${result.stdout}\n${result.stderr}`;
  switch (stepName) {
    case "format":
    case "build":
    case "web-typecheck":
      return extractCompilerFailure(stepName, text);
    case "test":
      return extractDotnetTestFailure(text);
    case "web-test":
      return extractVitestFailure(text);
    default:
      return extractGenericFailure(stepName, text);
  }
}

function finish(ok, failures) {
  if (ok) {
    console.log("\nverify.mjs: all checks passed");
  } else {
    console.log(`\nverify.mjs: FAILED at step "${failures[0]?.step}"`);
  }
  const json = JSON.stringify({ ok, failures });
  if (outFile) {
    try {
      writeFileSync(`${outFile}.tmp`, json);
      renameSync(`${outFile}.tmp`, outFile);
    } catch (e) {
      process.stderr.write(`verify.mjs: could not write the --out file: ${e.message}\n`);
    }
  }
  console.log(`VERIFY_RESULT: ${json}`);
  if (!ok) {
    const paragraph = failures.map((f) => `[${f.step}] ${f.summary}${f.file ? ` (${f.file})` : ""}`).join(" ");
    process.stderr.write(`verify.mjs failed: ${paragraph}\n`);
  }
  process.exitCode = ok ? 0 : 2;
}

function fatal(message) {
  console.error(`error: ${message}`);
  finish(false, [{ step: "build", summary: truncate(message), file: null }]);
  process.exit(process.exitCode);
}

function awaitResult(file, maxMin) {
  const giveUpAt = Date.now() + maxMin * 60 * 1000;
  return new Promise((resolve) => {
    const tick = () => {
      if (existsSync(file)) {
        const json = readFileSync(file, "utf8").trim();
        let ok = false;
        try {
          ok = JSON.parse(json).ok === true;
        } catch {
        }
        console.log(`VERIFY_RESULT: ${json}`);
        process.exitCode = ok ? 0 : 2;
        return resolve();
      }
      if (Date.now() >= giveUpAt) {
        console.log(`VERIFY_PENDING: no result in ${file} after ${maxMin} min — the run is still going; call --await again`);
        process.exitCode = 3;
        return resolve();
      }
      setTimeout(tick, 2000);
    };
    tick();
  });
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.await) return awaitResult(args.await, args.maxMin);
  deadlineAt = Date.now() + args.deadlineMin * 60 * 1000;
  if (args.out) {
    rmSync(args.out, { force: true });
    outFile = args.out;
  }
  const serviceNames = discoverServiceNames();
  const projectMap = buildProjectMap(serviceNames);
  const allProjectRels = () => [...projectMap.values()].map((v) => v.rel);

  let selectedRels;
  let runWeb;
  let runApi;
  let modeDescription;

  if (args.all) {
    selectedRels = allProjectRels();
    runWeb = true;
    runApi = true;
    modeDescription = "--all: every test project + web + api";
  } else if (args.projects) {
    const includesWeb = args.projects.some((t) => t.toLowerCase() === "web");
    const dotnetTokens = args.projects.filter((t) => t.toLowerCase() !== "web");
    const { rels, unknown } = resolveProjectsArg(dotnetTokens, projectMap);
    if (unknown.length > 0) {
      fatal(
        `unknown --projects entr${unknown.length > 1 ? "ies" : "y"}: ${unknown.join(", ")} ` +
          `(known short names: ${[...projectMap.values()].map((v) => v.shortName).join(", ")}, web, or a path to a test project)`,
      );
    }
    selectedRels = rels;
    runWeb = args.web || includesWeb;
    runApi = args.api;
    modeDescription = `--projects ${args.projects.join(",")}`;
  } else {
    const { files, treatAsAll, base } = getChangedFiles();
    if (treatAsAll) {
      selectedRels = allProjectRels();
      runWeb = true;
      runApi = true;
      modeDescription = "auto: no master or origin/master ref found — treating as --all";
    } else {
      const { affectedKeys, web, api } = computeAffected(files, serviceNames);
      selectedRels = affectedKeys.has("__all__")
        ? allProjectRels()
        : [...affectedKeys].map((k) => projectMap.get(k)?.rel).filter(Boolean);
      runWeb = args.web || web;
      runApi = args.api || api;
      modeDescription = `auto (diff base: ${base}): ${files.length} changed path(s)`;
    }
  }

  selectedRels = [...new Set(selectedRels)].sort();

  console.log("Skarbiec verify.mjs");
  console.log(`mode: ${modeDescription}`);
  console.log(`quick: ${args.quick ? "yes (stop after build)" : "no"}`);
  console.log(`test projects: ${selectedRels.length ? selectedRels.join(", ") : "(none)"}`);
  console.log(`web checks: ${runWeb ? "yes" : "no"}${args.quick && runWeb ? " (skipped by --quick)" : ""}`);
  console.log(`api check: ${runApi ? "yes" : "no"}${args.quick && runApi ? " (skipped by --quick)" : ""}`);
  console.log(`fix: ${args.fix ? "yes" : "no"} · deadline: ${args.deadlineMin} min`);

  if (args.fix) {
    const { files, treatAsAll } = getChangedFiles();
    if (treatAsAll) console.log("\nnotice: no master or origin/master ref — --fix skipped");
    else await applyFixes(files);
  }

  const formatResult = await step("format", () => runCommand("dotnet", ["format", "Skarbiec.slnx", "--verify-no-changes"]));
  if (!formatResult.ok) return finish(false, [buildFailure("format", formatResult)]);

  let buildResult = await step("build", () => runCommand("dotnet", ["build", "Skarbiec.slnx"]));
  if (!buildResult.ok && LOCKED_OUTPUT_RE.test(`${buildResult.stdout}\n${buildResult.stderr}`)) {
    const { stopped, left } = stopStack();
    const names = stopped.map((p) => `${p.name} (${p.what})`).join(", ") || "nothing found";
    console.log(`\nbuild outputs are locked by the running stack; stopped: ${names}`);
    if (left?.length) console.log(`still running: ${left.map((p) => `${p.name} #${p.pid}`).join(", ")}`);
    buildResult = await step("build (retry after stopping the stack)", () => runCommand("dotnet", ["build", "Skarbiec.slnx"]));
  }
  if (!buildResult.ok) return finish(false, [buildFailure("build", buildResult)]);

  if (args.quick) return finish(true, []);

  for (const rel of selectedRels) {
    const testResult = await step(`test: ${rel}`, () => runCommand("dotnet", ["test", rel, "--no-build"]));
    if (!testResult.ok) return finish(false, [buildFailure("test", testResult)]);
  }

  if (runWeb) {
    if (!existsSync(WEB_DIR)) {
      console.log("\nnotice: web/ not found — skipping web checks");
    } else {
      // Run independent static checks in parallel (typecheck, lint, format:check),
      // then sequential checks (build, test) that may depend on earlier steps.
      const staticChecks = [
        ["web-typecheck", ["run", "typecheck"]],
        ["web-lint", ["run", "lint"]],
        ["web-format", ["run", "format:check"]],
      ];
      const sequentialSteps = [
        ["web-build", ["run", "build"]],
        ["web-test", ["test", "--", "--watch=false"]],
      ];

      // Run static checks in parallel
      console.log("\n==> web checks (parallel: typecheck, lint, format:check)");
      const staticPromises = staticChecks.map(([canonical, npmArgs]) =>
        step(canonical, () => runCommand(NPM, npmArgs, { cwd: WEB_DIR }))
      );
      const staticResults = await Promise.all(staticPromises);
      for (let i = 0; i < staticResults.length; i++) {
        const r = staticResults[i];
        if (!r.ok) {
          return finish(false, [buildFailure(staticChecks[i][0], r)]);
        }
      }

      // Run sequential checks (build, test)
      for (const [canonical, npmArgs] of sequentialSteps) {
        const r = await step(canonical, () => runCommand(NPM, npmArgs, { cwd: WEB_DIR }));
        if (!r.ok) return finish(false, [buildFailure(canonical, r)]);
      }
    }
  }

  if (runApi) {
    const openapiDir = path.join(WEB_DIR, "openapi");
    if (!existsSync(openapiDir)) {
      console.log("\nnotice: build-time OpenAPI not set up yet (spec-00) — skipping api check");
    } else {
      const genResult = await step("api: npm run gen:api", () => runCommand(NPM, ["run", "gen:api"], { cwd: WEB_DIR }));
      if (!genResult.ok) return finish(false, [buildFailure("api", genResult)]);

      const diffResult = await step("api: diff web/src/app/api", () =>
        runCommand("git", ["diff", "--exit-code", "--", "web/src/app/api"]),
      );
      if (!diffResult.ok) {
        return finish(false, [
          { step: "api", summary: "generated TS client is out of date; commit the regenerated client", file: "web/src/app/api" },
        ]);
      }
    }
  }

  return finish(true, []);
}

main().catch((e) => fatal(`verify.mjs crashed: ${e?.stack ?? e}`));
