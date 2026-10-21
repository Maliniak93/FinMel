#!/usr/bin/env node
// Usage: node scripts/test-timings.mjs [--only dotnet,web] [--check] [--open] [--report-only] [--wait <min>] [--timeout <min>] [--from-autobuild]  measure the time of every test suite on a quiet machine, append it to the history (skarbiec-plan/test-timings/history.json, 30 entries per suite and class) and write report.html beside it
//   --only <list>  measure only these suites (dotnet, web); the others keep their history
//   --check        only check that the machine is quiet: exit 0 quiet, 3 busy (every source listed as "działa: ...")
//   --open         open report.html in the browser when it is written
//   --report-only  render report.html from the existing history.json without preflight or measuring (exit 3 when the file is missing)
//   --wait <min>   poll the quiet check every 30 s until quiet, at most <min> minutes; still busy then: exit 3, nothing written
//   --timeout <min>  deadline for the measuring (counted after the wait): the running command is killed, finished suites are kept, the rest stay unmeasured, exit 3
//   --from-autobuild  the running autobuild.mjs is not counted busy (every other source still is)
// Exit codes: 0 measured and nothing red, 1 a red test, 3 environment (machine busy, Docker down, a suite unmeasured, another instance running)

import { spawn, spawnSync } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, renameSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { GhLimitError, REPO, exitOnGhLimit, gh } from "./gh.mjs";
import { findStack, listProcesses } from "./stop-stack.mjs";
import { renderReport } from "./test-timings-report.mjs";

const IS_WIN = process.platform === "win32";
const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const WEB_DIR = path.join(REPO_ROOT, "web");
const DATA_DIR = path.join(REPO_ROOT, "skarbiec-plan", "test-timings");
const DATA_FILE = path.join(DATA_DIR, "history.json");
const REPORT_FILE = path.join(DATA_DIR, "report.html");
const NPM = IS_WIN ? "npm.cmd" : "npm";
const COMMAND_TIMEOUT_MS = 90 * 60 * 1000;
const COMPOSE_PROJECTS = ["skarbiec-local"];
const GROUPS = ["dotnet", "web"];
const DATA_VERSION = 2;
export const HISTORY_LIMIT = 30;
export const BASELINE_SIZE = 5;
export const MIN_BASELINE = 3;
export const OUTLIER_RATIO = 0.25;

const DOTNET_PROJECTS = [
  { name: "Skarbiec.Contracts.Tests", rel: "contracts/Skarbiec.Contracts.Tests/Skarbiec.Contracts.Tests.csproj" },
  { name: "Skarbiec.Gateway.Tests", rel: "gateway/Skarbiec.Gateway.Tests/Skarbiec.Gateway.Tests.csproj" },
  { name: "Skarbiec.Identity.Tests", rel: "services/Identity/Skarbiec.Identity.Tests/Skarbiec.Identity.Tests.csproj" },
  { name: "Skarbiec.MarketData.Tests", rel: "services/MarketData/Skarbiec.MarketData.Tests/Skarbiec.MarketData.Tests.csproj" },
  { name: "Skarbiec.Portfolio.Tests", rel: "services/Portfolio/Skarbiec.Portfolio.Tests/Skarbiec.Portfolio.Tests.csproj" },
  { name: "Skarbiec.Reporting.Tests", rel: "services/Reporting/Skarbiec.Reporting.Tests/Skarbiec.Reporting.Tests.csproj" },
  { name: "Skarbiec.ServiceDefaults.Tests", rel: "Skarbiec.ServiceDefaults.Tests/Skarbiec.ServiceDefaults.Tests.csproj" },
  { name: "Skarbiec.Testing.Tests", rel: "Skarbiec.Testing.Tests/Skarbiec.Testing.Tests.csproj" },
];

export const ALL_KEYS = [...DOTNET_PROJECTS.map((p) => `dotnet:${p.name}`), "web"];

const round1 = (n) => Math.round(n * 10) / 10;
const groupOf = (key) => key.split(":")[0];
export const labelOf = (key) => (key.startsWith("dotnet:") ? key.slice("dotnet:".length) : key);
const projectOf = (key) => DOTNET_PROJECTS.find((p) => `dotnet:${p.name}` === key);

const MINUTE_FLAGS = { "--wait": "wait", "--timeout": "timeout" };

export function parseArgs(argv) {
  const args = { only: null, check: false, help: false, open: false, reportOnly: false, wait: null, timeout: null, fromAutobuild: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    const [flag, inline] = a.split(/=(.*)/s);
    if (a === "--check") args.check = true;
    else if (a === "--open") args.open = true;
    else if (a === "--report-only") args.reportOnly = true;
    else if (a === "--from-autobuild") args.fromAutobuild = true;
    else if (a === "--help" || a === "-h") args.help = true;
    else if (a === "--only") args.only = String(argv[++i] ?? "");
    else if (a.startsWith("--only=")) args.only = a.slice("--only=".length);
    else if (MINUTE_FLAGS[flag]) {
      const raw = inline ?? argv[++i];
      const value = Number(raw);
      if (raw === undefined || raw === "" || !(value > 0)) return { error: `${flag} takes a positive number of minutes` };
      args[MINUTE_FLAGS[flag]] = value;
    } else return { error: `unknown argument: ${a}` };
  }
  if (args.only !== null) {
    const groups = args.only.split(",").map((s) => s.trim()).filter(Boolean);
    const unknown = groups.filter((g) => !GROUPS.includes(g));
    if (!groups.length || unknown.length) return { error: `--only takes ${GROUPS.join(",")}${unknown.length ? ` (unknown: ${unknown.join(", ")})` : ""}` };
    args.only = groups;
  }
  return args;
}

export function selectSuites(only, available = ALL_KEYS) {
  const groups = only ?? GROUPS;
  return available.filter((key) => groups.includes(groupOf(key)));
}

export const dockerRequired = (keys) => keys.some((key) => groupOf(key) === "dotnet");

const isAliveDefault = (pid) => {
  try {
    process.kill(pid, 0);
    return true;
  } catch (e) {
    return e.code === "EPERM";
  }
};

const AUTOBUILD_RE = /scripts[\\/]autobuild\.mjs/;

export function detectBusy({ processes = [], pidFiles = {}, containers = {}, runners = null, isAlive = isAliveDefault, ownPid = process.pid, ignoreAutobuild = false } = {}) {
  const busy = [];
  const warnings = [];

  const stack = findStack(processes);
  if (stack.length) {
    const kinds = new Map();
    for (const p of stack) kinds.set(p.what, (kinds.get(p.what) ?? 0) + 1);
    busy.push(`działa: lokalny stos (${[...kinds].map(([what, n]) => `${what} ×${n}`).join(", ")})`);
  }

  for (const p of ignoreAutobuild ? [] : processes) {
    if (p.pid !== ownPid && /^node(\.exe)?$/i.test(p.name ?? "") && AUTOBUILD_RE.test(p.cmd ?? "")) busy.push(`działa: autobuild.mjs (pid ${p.pid})`);
  }

  const pidSources = [
    ["verify", "verify.mjs"],
    ["timings", "inny test-timings.mjs"],
  ];
  for (const [field, label] of pidSources) {
    const info = pidFiles[field];
    if (info && Number.isInteger(info.pid) && info.pid !== ownPid && isAlive(info.pid)) busy.push(`działa: ${label} (pid ${info.pid})`);
  }

  for (const project of COMPOSE_PROJECTS) {
    const ids = containers[project] ?? [];
    if (ids.length) busy.push(`działa: kontenery ${project} (${ids.length})`);
  }

  if (runners === null || runners === undefined) warnings.push("runner nie sprawdzony");
  else {
    for (const r of runners) {
      if (r.status === "online" && r.busy === true) busy.push(`działa: runner ${r.name} (zajęty)`);
    }
  }

  return { busy, warnings };
}

export function evaluateSilence({ keys, dockerUp = true, ...busyInputs }) {
  const { busy, warnings } = detectBusy(busyInputs);
  const lines = [...busy];
  if (!dockerUp && dockerRequired(keys)) lines.push("Docker nie jest uruchomiony (wymagany dla dotnet)");
  return { lines, warnings };
}

const unquote = (s) =>
  s.replace(/&(lt|gt|amp|quot|apos);/g, (_, e) => ({ lt: "<", gt: ">", amp: "&", quot: '"', apos: "'" })[e]);

function attributes(text) {
  const out = {};
  for (const m of text.matchAll(/([\w:]+)="([^"]*)"/g)) out[m[1]] = unquote(m[2]);
  return out;
}

function durationMs(text) {
  const m = /^(?:(\d+)\.)?(\d+):(\d+):(\d+(?:\.\d+)?)$/.exec(text ?? "");
  if (!m) return 0;
  return ((Number(m[1] ?? 0) * 24 + Number(m[2])) * 3600 + Number(m[3]) * 60 + Number(m[4])) * 1000;
}

const SEVERITY = { skipped: 0, passed: 1, failed: 2 };

export function aggregate(rows) {
  const counts = { passed: 0, failed: 0, skipped: 0 };
  const classes = {};
  const tests = {};
  for (const { id, cls, ms, outcome } of rows) {
    counts[outcome]++;
    const c = (classes[cls] ??= { ms: 0, count: 0 });
    c.ms += ms;
    c.count++;
    const t = tests[id];
    if (!t) tests[id] = { ms, outcome };
    else {
      t.ms += ms;
      if (SEVERITY[outcome] > SEVERITY[t.outcome]) t.outcome = outcome;
    }
  }
  for (const c of Object.values(classes)) c.ms = round1(c.ms);
  for (const t of Object.values(tests)) t.ms = round1(t.ms);
  return { counts, classes, tests };
}

const TRX_OUTCOME = { Passed: "passed", NotExecuted: "skipped", Inconclusive: "skipped", NotRunnable: "skipped" };

export function parseTrx(xml) {
  const rows = [];
  for (const m of xml.matchAll(/<UnitTestResult\b([^>]*?)\/?>/g)) {
    const a = attributes(m[1]);
    const name = a.testName ?? "";
    const paren = name.indexOf("(");
    const id = paren === -1 ? name : name.slice(0, paren);
    rows.push({ id, cls: id.includes(".") ? id.slice(0, id.lastIndexOf(".")) : id, ms: durationMs(a.duration), outcome: TRX_OUTCOME[a.outcome] ?? "failed" });
  }
  return aggregate(rows);
}

const VITEST_OUTCOME = { passed: "passed", failed: "failed" };

export function parseVitestJson(source, { root = null } = {}) {
  const report = typeof source === "string" ? JSON.parse(source) : source;
  const normalizedRoot = root ? root.replaceAll("\\", "/").replace(/\/?$/, "/") : null;
  const relative = (name) => {
    const file = String(name ?? "").replaceAll("\\", "/");
    if (normalizedRoot && file.startsWith(normalizedRoot)) return file.slice(normalizedRoot.length);
    const web = file.indexOf("/web/");
    return web === -1 ? file : file.slice(web + "/web/".length);
  };
  const rows = [];
  for (const file of report.testResults ?? []) {
    const spec = relative(file.name);
    for (const t of file.assertionResults ?? []) {
      const id = [spec, ...(t.ancestorTitles ?? []), t.title].join(" › ");
      rows.push({ id, cls: spec, ms: Number(t.duration ?? 0), outcome: VITEST_OUTCOME[t.status] ?? "skipped" });
    }
  }
  return aggregate(rows);
}

export function dotnetCommands(csproj, { trxName, resultsDir }) {
  return {
    build: { bin: "dotnet", args: ["build", csproj] },
    test: { bin: "dotnet", args: ["test", csproj, "--no-build", "--logger", `trx;LogFileName=${trxName}`, "--results-directory", resultsDir] },
  };
}

export function webCommands({ outputFile }) {
  return {
    install: { bin: NPM, args: ["install", "--no-audit", "--no-fund"] },
    test: { bin: NPM, args: ["test", "--", "--watch=false", "--reporters=json", `--output-file=${outputFile}`] },
  };
}

export function parseDataFile(text) {
  try {
    const data = JSON.parse(text);
    if (data?.version !== DATA_VERSION || typeof data.suites !== "object" || data.suites === null || Array.isArray(data.suites)) return null;
    if (!Object.values(data.suites).every((s) => Array.isArray(s?.history) && s.history.length > 0 && s.classes && s.tests)) return null;
    return data;
  } catch {
    return null;
  }
}

const totalTests = (counts) => counts.passed + counts.failed + counts.skipped;

export function median(values) {
  if (!values.length) return null;
  const sorted = [...values].sort((a, b) => a - b);
  const mid = sorted.length >> 1;
  return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
}

export const baselineOf = (entries) => entries.filter((e) => e.reliable).slice(-BASELINE_SIZE);

export function assessReliability(history, { dirty, counts, testMs }) {
  const reasons = [];
  if (dirty) reasons.push("dirty-tree");
  if (counts.failed > 0) reasons.push("red-test");
  const baseline = baselineOf(history);
  if (baseline.length >= MIN_BASELINE) {
    const mid = median(baseline.map((e) => e.testMs));
    if (mid > 0 && Math.abs(testMs - mid) > mid * OUTLIER_RATIO) reasons.push("outlier");
  }
  return { reliable: reasons.length === 0, reasons };
}

export function compareWithMedian(entries, field = "testMs") {
  const current = entries.at(-1);
  if (!current?.reliable) return null;
  const baseline = baselineOf(entries.slice(0, -1));
  if (!baseline.length) return null;
  const mid = median(baseline.map((e) => e[field]));
  return { ms: current[field], baselineMs: mid, deltaMs: round1(current[field] - mid), baselineSize: baseline.length };
}

export function classSeries(suite, name) {
  const reliableAt = new Map(suite.history.map((e) => [e.measuredAt, e.reliable]));
  return (suite.classes[name] ?? []).map((c) => ({ ...c, reliable: reliableAt.get(c.measuredAt) ?? false }));
}

export function mergeSuite(data, key, outcome, meta) {
  const base = data ?? { version: DATA_VERSION, run: null, suites: {} };
  if (!outcome.measured) return { data: base, status: "unmeasured", reason: outcome.reason };
  if (totalTests(outcome.counts) === 0) return { data: base, status: "unmeasured", reason: "0 testów" };

  const old = base.suites[key] ?? { history: [], classes: {}, tests: {} };
  const testMs = Math.round(outcome.testMs);
  const entry = {
    measuredAt: meta.measuredAt,
    commit: meta.commit,
    dirty: meta.dirty,
    status: outcome.counts.failed > 0 ? "failed" : "passed",
    setupMs: Math.round(outcome.setupMs),
    testMs,
    counts: outcome.counts,
    ...assessReliability(old.history, { dirty: meta.dirty, counts: outcome.counts, testMs }),
  };
  const classes = {};
  for (const [name, c] of Object.entries(outcome.classes)) {
    classes[name] = [...(old.classes[name] ?? []), { measuredAt: meta.measuredAt, commit: meta.commit, ms: c.ms, count: c.count }].slice(-HISTORY_LIMIT);
  }
  const suite = { history: [...old.history, entry].slice(-HISTORY_LIMIT), classes, tests: outcome.tests };
  return { data: { ...base, suites: { ...base.suites, [key]: suite } }, status: entry.status };
}

const pad2 = (n) => String(n).padStart(2, "0");

export function formatDuration(ms) {
  const s = Math.round(Math.abs(ms) / 1000);
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = s % 60;
  return h > 0 ? `${h}:${pad2(m)}:${pad2(sec)}` : `${m}:${pad2(sec)}`;
}

export const formatDelta = (ms) => `${ms < 0 ? "-" : "+"}${formatDuration(ms)}`;

const formatCount = (n) => String(n).replace(/\B(?=(\d{3})+(?!\d))/g, " ");

export function progressLine(index, total, key, outcome) {
  const prefix = `[${index}/${total}] ${key.startsWith("dotnet:") ? `dotnet ${labelOf(key)}` : key}`;
  if (outcome.status === "unmeasured") return `${prefix} — niezmierzony: ${outcome.reason}`;
  const { entry } = outcome;
  const flag = entry.reliable ? "" : `, niewiarygodny (${entry.reasons.join(", ")})`;
  return `${prefix} — setup ${formatDuration(entry.setupMs)}, testy ${formatDuration(entry.testMs)}, ${formatCount(totalTests(entry.counts))} testów, ${entry.counts.failed} czerwonych${flag}`;
}

export function summarize(measured) {
  if (!measured.length) return "Nic nie zmierzono.";
  const sum = (list, f) => list.reduce((acc, x) => acc + f(x), 0);
  const nowMs = sum(measured, ({ entry }) => entry.setupMs + entry.testMs);
  const tests = sum(measured, ({ entry }) => totalTests(entry.counts));
  const red = sum(measured, ({ entry }) => entry.counts.failed);
  const compared = measured.filter(({ comparison }) => comparison);
  const tail = `testów ${formatCount(tests)}, ${red} czerwonych.`;

  let line;
  if (!compared.length) line = `Razem ${formatDuration(nowMs)} (brak wiarygodnej historii do porównania), ${tail}`;
  else {
    const delta = sum(compared, ({ comparison }) => comparison.deltaMs);
    line = `Razem ${formatDuration(nowMs)} (testy ${formatDelta(delta)} vs mediana), ${tail}`;
    const grew = compared
      .filter(({ comparison }) => comparison.deltaMs > 0)
      .sort((a, b) => b.comparison.deltaMs - a.comparison.deltaMs)[0];
    if (grew) line += ` Najwięcej urosło: ${labelOf(grew.key)} ${formatDelta(grew.comparison.deltaMs)}`;
  }
  const unreliable = measured.filter(({ entry }) => !entry.reliable).map(({ key, entry }) => `${labelOf(key)} (${entry.reasons.join(", ")})`);
  if (unreliable.length) line += `${line.endsWith(".") ? "" : "."} Pomiar niewiarygodny: ${unreliable.join(", ")}`;
  return line;
}

export function exitCodeFor(results) {
  if (results.some((r) => r.status === "failed")) return 1;
  if (results.some((r) => r.status === "unmeasured")) return 3;
  return 0;
}

export function buildRun({ startedAt, git, keys, results, pendingReason }) {
  const suites = {};
  const reasons = {};
  for (const key of ALL_KEYS) suites[key] = "not-selected";
  for (const key of keys) {
    const result = results.find((r) => r.key === key);
    if (result && result.status !== "unmeasured") suites[key] = "measured";
    else {
      suites[key] = "unmeasured";
      reasons[key] = result?.reason ?? pendingReason;
    }
  }
  return { startedAt, commit: git.commit, dirty: git.dirty, branch: git.branch, suites, reasons };
}

export const POLL_INTERVAL_MS = 30_000;
const TIMEOUT_REASON = "limit czasu przebiegu (--timeout)";

export async function waitForQuiet({ check, waitMs, now = () => new Date(), sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms)), onBusy = () => {} }) {
  const deadline = now().getTime() + waitMs;
  for (;;) {
    const lines = await check();
    if (!lines.length) return [];
    const left = deadline - now().getTime();
    if (left <= 0) return lines;
    onBusy(lines, left);
    await sleep(Math.min(POLL_INTERVAL_MS, left));
  }
}

export async function measureWhenQuiet({ waitMs = 0, check, measureRun, ...clock }) {
  if (waitMs > 0) {
    const lines = await waitForQuiet({ check, waitMs, ...clock });
    if (lines.length) return { refused: lines };
  }
  return { refused: null, outcome: await measureRun() };
}

export async function runSuites({ keys, data, check, measure, write, git, now = () => new Date(), onSuite = () => {}, deadlineAt = null }) {
  const startedAt = now().toISOString();
  const results = [];
  let current = data;
  const record = () => {
    current = { ...(current ?? { version: DATA_VERSION, suites: {} }), run: buildRun({ startedAt, git, keys, results, pendingReason: "przebieg w toku lub przerwany" }) };
    write(current);
  };

  const abandon = (from, reason) => {
    if (from >= keys.length) return;
    for (const rest of keys.slice(from)) {
      const result = { key: rest, status: "unmeasured", reason };
      results.push(result);
      onSuite(results.length, keys.length, rest, result);
    }
    record();
  };

  for (let i = 0; i < keys.length; i++) {
    const key = keys[i];
    if (deadlineAt !== null && now().getTime() >= deadlineAt) {
      abandon(i, TIMEOUT_REASON);
      return { data: current, results, refused: null, interrupted: null, timedOut: true };
    }
    const lines = await check(keys.slice(i), i);
    if (lines.length) {
      if (i === 0) return { data, results, refused: lines, interrupted: null };
      abandon(i, `maszyna zajęta: ${lines.join("; ")}`);
      return { data: current, results, refused: null, interrupted: lines };
    }

    const outcome = await measure(key);
    const merged = mergeSuite(current, key, outcome, { measuredAt: now().toISOString(), ...git });
    current = merged.data;
    const history = merged.status === "unmeasured" ? null : current.suites[key].history;
    const result = { key, status: merged.status, reason: merged.reason, entry: history?.at(-1), comparison: history ? compareWithMedian(history) : null };
    results.push(result);
    record();
    onSuite(i + 1, keys.length, key, result);
    if (outcome.timedOut) {
      abandon(i + 1, TIMEOUT_REASON);
      return { data: current, results, refused: null, interrupted: null, timedOut: true };
    }
  }
  return { data: current, results, refused: null, interrupted: null, timedOut: false };
}

function quoteArg(a) {
  const s = String(a);
  if (s === "") return '""';
  if (/[\s"&|<>^%;]/.test(s)) return `"${s.replace(/"/g, '\\"')}"`;
  return s;
}

function killTree(pid) {
  if (!pid) return;
  if (IS_WIN) spawnSync("taskkill", ["/PID", String(pid), "/T", "/F"], { encoding: "utf8", windowsHide: true });
  else {
    try {
      process.kill(-pid, "SIGKILL");
    } catch {
      // Already gone.
    }
  }
}

let runDeadlineAt = null;

function exec(bin, args, { cwd = REPO_ROOT } = {}) {
  const startedAt = Date.now();
  const untilRunDeadline = runDeadlineAt === null ? Infinity : Math.max(0, runDeadlineAt - startedAt);
  const limitMs = Math.min(COMMAND_TIMEOUT_MS, untilRunDeadline);
  return new Promise((resolve) => {
    let tail = "";
    let cutByDeadline = false;
    const keepTail = (d) => {
      tail = (tail + d.toString("utf8")).slice(-4000);
    };
    const child = spawn([bin, ...args].map(quoteArg).join(" "), { cwd, shell: true, windowsHide: true, detached: !IS_WIN, stdio: ["ignore", "pipe", "pipe"] });
    child.stdout.on("data", keepTail);
    child.stderr.on("data", keepTail);
    const timer = setTimeout(() => {
      cutByDeadline = untilRunDeadline <= COMMAND_TIMEOUT_MS;
      killTree(child.pid);
    }, limitMs);
    const done = (status, error) => {
      clearTimeout(timer);
      resolve({ status, error, tail, ms: Date.now() - startedAt, startedAt, cutByDeadline });
    };
    child.on("error", (e) => done(null, e));
    child.on("close", (code) => done(code, null));
  });
}

function failureReason(label, res) {
  const lines = res.tail.split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
  const hint = [...lines].reverse().find((l) => /error|fail|błąd/i.test(l)) ?? lines.at(-1) ?? res.error?.message ?? "";
  return `${label} (kod ${res.status ?? "brak"})${hint ? `: ${hint.slice(0, 200)}` : ""}`;
}

const readText = (file) => (existsSync(file) ? readFileSync(file, "utf8") : null);

const timedOut = () => ({ measured: false, timedOut: true, reason: TIMEOUT_REASON });

async function measureDotnet(key) {
  const project = projectOf(key);
  const dir = mkdtempSync(path.join(tmpdir(), "test-timings-"));
  try {
    const commands = dotnetCommands(path.join(REPO_ROOT, project.rel), { trxName: "result.trx", resultsDir: dir });
    const build = await exec(commands.build.bin, commands.build.args);
    if (build.cutByDeadline) return timedOut();
    if (build.status !== 0) return { measured: false, reason: failureReason("build nie przeszedł", build) };
    const test = await exec(commands.test.bin, commands.test.args);
    if (test.cutByDeadline) return timedOut();
    const trx = readText(path.join(dir, "result.trx"));
    if (!trx || (test.status !== 0 && test.status !== 1)) return { measured: false, reason: failureReason("brak wyniku testów", test) };
    return { measured: true, setupMs: build.ms, testMs: test.ms, ...parseTrx(trx) };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

async function measureWeb() {
  const dir = mkdtempSync(path.join(tmpdir(), "test-timings-"));
  try {
    const outputFile = path.join(dir, "vitest.json");
    const commands = webCommands({ outputFile });
    const install = await exec(commands.install.bin, commands.install.args, { cwd: WEB_DIR });
    if (install.cutByDeadline) return timedOut();
    if (install.status !== 0) return { measured: false, reason: failureReason("npm install nie przeszedł", install) };
    const test = await exec(commands.test.bin, commands.test.args, { cwd: WEB_DIR });
    if (test.cutByDeadline) return timedOut();
    const json = readText(outputFile);
    if (!json || (test.status !== 0 && test.status !== 1)) return { measured: false, reason: failureReason("brak wyniku testów", test) };
    return { measured: true, setupMs: install.ms, testMs: test.ms, ...parseVitestJson(json, { root: WEB_DIR }) };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

function measure(key) {
  return key === "web" ? measureWeb() : measureDotnet(key);
}

function git(argv) {
  const res = spawnSync("git", argv, { cwd: REPO_ROOT, encoding: "utf8", windowsHide: true });
  return res.status === 0 ? res.stdout.trim() : null;
}

const gitDir = () => {
  const out = git(["rev-parse", "--git-dir"]);
  return out ? path.resolve(REPO_ROOT, out) : path.join(REPO_ROOT, ".git");
};

const pidPath = (name) => path.join(gitDir(), `${name}.pid`);

function readPidInfo(name) {
  try {
    return JSON.parse(readFileSync(pidPath(name), "utf8"));
  } catch {
    return null;
  }
}

function claimPidFile() {
  const other = readPidInfo("test-timings");
  if (other && other.pid !== process.pid && isAliveDefault(other.pid)) return;
  writeFileSync(pidPath("test-timings"), JSON.stringify({ pid: process.pid, startedAt: Date.now() }));
}

function releasePidFile() {
  if (readPidInfo("test-timings")?.pid === process.pid) rmSync(pidPath("test-timings"), { force: true });
}

function gitInfo() {
  return {
    commit: git(["rev-parse", "HEAD"]),
    branch: git(["rev-parse", "--abbrev-ref", "HEAD"]),
    dirty: Boolean(git(["status", "--porcelain", "--untracked-files=no"])),
  };
}

function dockerIsUp() {
  const res = spawnSync("docker", ["info", "--format", "{{.ServerVersion}}"], { encoding: "utf8", windowsHide: true, timeout: 30_000 });
  return res.status === 0;
}

function runningContainers(project) {
  const res = spawnSync("docker", ["ps", "-q", "--filter", `label=com.docker.compose.project=${project}`], { encoding: "utf8", windowsHide: true, timeout: 30_000 });
  return res.status === 0 ? res.stdout.split(/\s+/).filter(Boolean) : [];
}

function fetchRunners() {
  try {
    return JSON.parse(gh(["api", `repos/${REPO}/actions/runners`, "--jq", "[.runners[] | {name, status, busy}]"], { timeout: 30_000 }));
  } catch (err) {
    if (err instanceof GhLimitError) exitOnGhLimit(err);
    return null;
  }
}

function snapshot() {
  const dockerUp = dockerIsUp();
  return {
    dockerUp,
    processes: listProcesses(),
    pidFiles: { verify: readPidInfo("verify"), timings: readPidInfo("test-timings") },
    containers: dockerUp ? Object.fromEntries(COMPOSE_PROJECTS.map((p) => [p, runningContainers(p)])) : {},
    runners: fetchRunners(),
  };
}

function writeAtomically(file, data) {
  mkdirSync(path.dirname(file), { recursive: true });
  const tmp = `${file}.${process.pid}.tmp`;
  writeFileSync(tmp, `${JSON.stringify(data)}\n`);
  renameSync(tmp, file);
}

function openFile(file) {
  const [bin, args] = IS_WIN ? ["cmd", ["/c", "start", "", file]] : [process.platform === "darwin" ? "open" : "xdg-open", [file]];
  spawn(bin, args, { detached: true, stdio: "ignore", windowsHide: true }).unref();
}

export function emitReport(data, { file = REPORT_FILE, open = false, write = (f, text) => (mkdirSync(path.dirname(f), { recursive: true }), writeFileSync(f, text)), opener = openFile, log = console.log } = {}) {
  write(file, renderReport(data));
  log(`Raport: ${path.relative(REPO_ROOT, file).replaceAll("\\", "/")}`);
  if (open) opener(file);
}

export function reportOnly({ read = () => readText(DATA_FILE), log = console.log, ...options } = {}) {
  const data = parseDataFile(read() ?? "");
  if (!data) {
    log(`Brak poprawnego pliku ${path.relative(REPO_ROOT, DATA_FILE).replaceAll("\\", "/")} — najpierw zmierz testy.`);
    return 3;
  }
  emitReport(data, { log, ...options });
  return 0;
}

async function main(argv) {
  const args = parseArgs(argv);
  if (args.error) {
    console.error(`test-timings.mjs: ${args.error}`);
    return 2;
  }
  if (args.help) {
    console.log("Usage: node scripts/test-timings.mjs [--only dotnet,web] [--check] [--open] [--report-only] [--wait <min>] [--timeout <min>] [--from-autobuild]");
    return 0;
  }
  if (args.reportOnly) return reportOnly({ open: args.open });

  const present = ALL_KEYS.filter((key) => !projectOf(key) || existsSync(path.join(REPO_ROOT, projectOf(key).rel)));
  const keys = selectSuites(args.only, present);
  const warned = new Set();
  const check = async (remaining) => {
    const { lines, warnings } = evaluateSilence({ keys: remaining, ignoreAutobuild: args.fromAutobuild, ...snapshot() });
    for (const w of warnings) {
      if (!warned.has(w)) console.warn(`UWAGA: ${w}`);
      warned.add(w);
    }
    return lines;
  };

  const waitMs = args.wait ? args.wait * 60_000 : 0;
  const onBusy = (lines, left) => console.log(`czekam na ciszę (jeszcze ${Math.ceil(left / 60_000)} min): ${lines.join("; ")}`);

  if (args.check) {
    const lines = await waitForQuiet({ waitMs, check: () => check(keys), onBusy });
    if (!lines.length) {
      console.log("cisza: maszyna wolna, można mierzyć");
      return 0;
    }
    for (const line of lines) console.log(line);
    return 3;
  }

  claimPidFile();
  try {
    const { refused, outcome } = await measureWhenQuiet({
      waitMs,
      check: () => check(keys),
      onBusy,
      measureRun: () => {
        runDeadlineAt = args.timeout ? Date.now() + args.timeout * 60_000 : null;
        return runSuites({
          keys,
          data: parseDataFile(readText(DATA_FILE) ?? ""),
          check,
          measure,
          deadlineAt: runDeadlineAt,
          write: (data) => writeAtomically(DATA_FILE, data),
          git: gitInfo(),
          onSuite: (index, total, key, result) => console.log(progressLine(index, total, key, result)),
        });
      },
    });
    if (refused) {
      for (const line of refused) console.log(line);
      console.log("Nic nie zmierzono, plik z wynikami bez zmian.");
      return 3;
    }
    if (outcome.refused) {
      for (const line of outcome.refused) console.log(line);
      console.log("Nic nie zmierzono, plik z wynikami bez zmian.");
      return 3;
    }
    const measured = outcome.results.filter((r) => r.entry).map((r) => ({ key: r.key, entry: r.entry, comparison: r.comparison }));
    console.log(summarize(measured));
    console.log(`Wyniki: ${path.relative(REPO_ROOT, DATA_FILE).replaceAll("\\", "/")}`);
    if (measured.length) emitReport(outcome.data, { open: args.open });
    for (const r of outcome.results.filter((x) => x.status === "unmeasured")) console.log(`Niezmierzony: ${labelOf(r.key)} — ${r.reason}`);
    return exitCodeFor(outcome.results);
  } finally {
    releasePidFile();
  }
}

if (process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1])) {
  process.on("SIGINT", () => {
    releasePidFile();
    process.exit(130);
  });
  process.exitCode = await main(process.argv.slice(2));
}
