#!/usr/bin/env node
// Single definition of "green" for Skarbiec: format -> build -> affected tests -> web -> api.
//
// Usage: node scripts/verify.mjs [--quick] [--projects a,b] [--web] [--api] [--all]
//
//   (no flags)        auto mode — selects affected .NET test projects and web/api checks from
//                      changed files (git diff master...HEAD, falling back to origin/master...HEAD,
//                      unioned with `git status --porcelain`; if neither ref exists, treat as --all).
//   --quick           stop after `dotnet build` (format + build only). Overrides everything below.
//   --projects a,b    run exactly these .NET test projects (short names like `Portfolio`, `Gateway`,
//                      `Contracts`, `ServiceDefaults`, `Testing`, or a path to a test project/csproj).
//                      Replaces auto-detection of .NET projects; web/api still need --web/--api/--all.
//   --web             force the web checks on regardless of what changed.
//   --api             force the api (generated TS client) check on regardless of what changed.
//   --all             every test project + web + api.
//
// Steps run in order and STOP at the first failure: format, build, test (one dotnet test per
// affected project), web (typecheck/lint/format/build/test), api (gen:api + diff check).
//
// The final line of stdout is always exactly `VERIFY_RESULT: <json>` — see `finish()` below for the
// shape. Exit code 0 on success, 2 on failure (with a one-paragraph summary also written to stderr,
// so this script can be wired directly as a blocking Claude Code Stop hook). Nothing else is ever
// printed after that line.
//
// Node >= 22, ESM, zero npm dependencies — only node:fs / node:path / node:child_process / node:url.
// Runs from any cwd: the repo root is resolved from this file's own location, and every child
// process is given an explicit `cwd` rather than inheriting the caller's.
//
// Windows notes:
//  - Every command runs via `spawnSync(cmdString, { shell: true, ... })` with a single pre-quoted
//    command string (not an argv array) — Node does not itself quote array args for a Windows shell,
//    so building the string ourselves (see `quoteArg`) keeps `cmd.exe` and POSIX shells consistent.
//  - `npm` has no `.exe` on Windows (it's `npm.cmd`); we call `npm.cmd` there and plain `npm` elsewhere.
//  - Verified empirically on this repo's Angular 22 unit-test builder (`@angular/build:unit-test`,
//    which wraps Vitest): `npm test -- --watch=false` runs once and exits 0 — that is the invocation
//    used below for the `web-test` step.
//  - `dotnet format`/`dotnet build` diagnostics are locale-sensitive for the free-text MESSAGE (this
//    machine prints Polish, e.g. "Napraw znacznik końca wiersza" for ENDOFLINE) but NOT for the
//    `error`/`warning` keyword or the rule code (`ENDOFLINE`, `CS0103`, ...) — the parsers below only
//    ever key off the latter, never the message text.
//  - `dotnet test` failures are read from xUnit's own `[FAIL]` marker lines (also not localized),
//    never from a loose "Failed <word>" scan — that previously matched unrelated localized prose
//    (e.g. a Polish sentence containing "Failed to...") and reported a garbage test name. A run
//    where Docker isn't reachable is detected up front from the output itself and reported as that,
//    rather than as a misleading one-word "test".

import { spawnSync } from "node:child_process";
import { existsSync, readdirSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const IS_WIN = process.platform === "win32";
const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const WEB_DIR = path.join(REPO_ROOT, "web");
const NPM = IS_WIN ? "npm.cmd" : "npm";
const DEFAULT_TIMEOUT_MS = 20 * 60 * 1000; // dotnet test can pull Testcontainers images; be generous

// ---------------------------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------------------------

function parseArgs(argv) {
  const args = { quick: false, all: false, web: false, api: false, projects: null };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === "--quick") args.quick = true;
    else if (a === "--all") args.all = true;
    else if (a === "--web") args.web = true;
    else if (a === "--api") args.api = true;
    else if (a === "--projects") {
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

// ---------------------------------------------------------------------------------------------
// Project discovery
// ---------------------------------------------------------------------------------------------

// All directory names under services/ (regardless of whether a Tests project exists yet) — used to
// recognize `services/<S>/**` paths even for a service that has no test project on disk yet.
function discoverServiceNames() {
  const servicesDir = path.join(REPO_ROOT, "services");
  if (!existsSync(servicesDir)) return [];
  return readdirSync(servicesDir, { withFileTypes: true })
    .filter((d) => d.isDirectory())
    .map((d) => d.name)
    .sort();
}

// short name (as typed on the CLI, case-insensitive) -> { shortName, rel } for every test project
// that actually exists on disk right now.
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

// ---------------------------------------------------------------------------------------------
// Change detection (auto mode)
// ---------------------------------------------------------------------------------------------

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
  const res = runCommand("git", args, { cwd: REPO_ROOT, timeoutMs: 15_000 });
  return res.ok ? res.stdout : null;
}

// Returns { files: string[], treatAsAll: boolean, base: string|null }
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
      /* fall through to raw */
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

// -> { affectedKeys: Set<lowercase short name>, web: boolean, api: boolean }
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

// ---------------------------------------------------------------------------------------------
// Process execution
// ---------------------------------------------------------------------------------------------

function quoteArg(a) {
  const s = String(a);
  if (s === "") return '""';
  if (/[\s"&|<>^%]/.test(s)) return `"${s.replace(/"/g, '\\"')}"`;
  return s;
}

function runCommand(bin, args, { cwd = REPO_ROOT, timeoutMs = DEFAULT_TIMEOUT_MS } = {}) {
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

// ---------------------------------------------------------------------------------------------
// Output helpers
// ---------------------------------------------------------------------------------------------

const ANSI_RE = /\x1B\[[0-?]*[ -/]*[@-~]/g;
function stripAnsi(s) {
  return s.replace(ANSI_RE, "");
}

function formatDuration(ms) {
  return ms < 1000 ? `${ms}ms` : `${(ms / 1000).toFixed(1)}s`;
}

function step(label, fn) {
  console.log(`\n==> ${label}`);
  const result = fn();
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
  if (t.startsWith("> ")) return true; // npm run script echo
  if (/^[⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏✔✓]+$/.test(t)) return true; // spinner/checkmark-only lines
  return false;
}

function firstNonEmptyLines(text, n) {
  const lines = stripAnsi(text)
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter((l) => !isNoiseLine(l));
  return lines.slice(0, n).join(" | ");
}

// ---------------------------------------------------------------------------------------------
// Failure extraction (best-effort; always falls back to a truthful excerpt, never throws)
// ---------------------------------------------------------------------------------------------

// MSBuild/tsc shared diagnostic shape: `file(line,col): error CODE: message [project]`. The `error`/
// `warning` keyword and the rule code are stable across locales — only `message` is localized, and
// we never read it.
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

// xUnit prints a literal (non-localized) `[FAIL]` tag after a failing test's display name, e.g.
// `    Skarbiec.Portfolio.Tests.AddAssetEndpointTests.Returns422 [FAIL]`. This is the only thing we
// trust as a real test name — never a generic "Failed <word>" scan, which can match unrelated
// (possibly localized) prose elsewhere in the output and invent a bogus name.
const XUNIT_FAIL_RE = /^\s*(.+?)\s+\[FAIL\]\s*$/;
// VSTest's per-test line on this SDK is `Failed <Fully.Qualified.Name> [12 ms]` (localized prefix,
// e.g. `Niepowodzenie` in Polish). The dotted name AND the trailing `[N ms]` timing are both
// required, which is what keeps prose like "Failed to connect to Docker endpoint" from matching.
const VSTEST_FAIL_RE = /^\s*(?:Failed|Niepowodzenie)\s+([A-Za-z_][\w]*(?:\.[\w]+)+(?:\([^)]*\))?)\s+\[\d+\s*(?:ms|s)\]\s*$/;
// Locale note: dotnet test's own run-summary line is "Failed!  - Failed: N, Passed: ..." in English
// and "Niepowodzenie!  - Niepowodzenie: N, ..." has been observed in Polish; both name the marker
// word twice with the count right after, so matching the marker then the first following number
// gets the count without needing every language's full sentence.
const SUMMARY_COUNT_RE = /(?:Failed!|Niepowodzenie!).*?(\d+)/;
// A failure that never reached a real test — Testcontainers couldn't reach the Docker daemon — is a
// far more useful and honest summary than whatever unrelated line a name/file scan would otherwise
// latch onto.
const DOCKER_UNREACHABLE_RE = /\b(docker|testcontainers|npipe|dockerdesktoplinuxengine)\b/i;
const STACK_FILE_RE = /in\s+(.+?):line\s+\d+/;

// Reject a stack-frame path that isn't actually inside this repo (e.g. Testcontainers ships PDBs
// with its own build-time paths baked in, like `/_/src/Testcontainers/Guard.Null.cs`) — reporting
// that as `file` would point straight at a NuGet package, not at anything the user can act on.
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

  // No per-test [FAIL] marker found — fall back to the run's count-only summary line. Never invent
  // a name from unrelated text.
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

// ---------------------------------------------------------------------------------------------
// Result / exit
// ---------------------------------------------------------------------------------------------

function finish(ok, failures) {
  if (ok) {
    console.log("\nverify.mjs: all checks passed");
  } else {
    console.log(`\nverify.mjs: FAILED at step "${failures[0]?.step}"`);
  }
  console.log(`VERIFY_RESULT: ${JSON.stringify({ ok, failures })}`);
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

// ---------------------------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------------------------

function main() {
  const args = parseArgs(process.argv.slice(2));
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
    // `web` is a pseudo-project: it enables the web checks rather than naming a .NET test project.
    // `--projects web` alone means "no .NET tests, just web"; mixed with real names it adds web
    // checks on top of them.
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

  // Step 1: format
  const formatResult = step("format", () => runCommand("dotnet", ["format", "Skarbiec.slnx", "--verify-no-changes"]));
  if (!formatResult.ok) return finish(false, [buildFailure("format", formatResult)]);

  // Step 2: build (warnings are errors via Directory.Build.props)
  const buildResult = step("build", () => runCommand("dotnet", ["build", "Skarbiec.slnx"]));
  if (!buildResult.ok) return finish(false, [buildFailure("build", buildResult)]);

  if (args.quick) return finish(true, []);

  // Step 3: dotnet test, one per affected project, stop at the first failure
  for (const rel of selectedRels) {
    const testResult = step(`test: ${rel}`, () => runCommand("dotnet", ["test", rel, "--no-build"]));
    if (!testResult.ok) return finish(false, [buildFailure("test", testResult)]);
  }

  // Step 4: web
  if (runWeb) {
    if (!existsSync(WEB_DIR)) {
      console.log("\nnotice: web/ not found — skipping web checks");
    } else {
      const webSteps = [
        ["web-typecheck", ["run", "typecheck"]],
        ["web-lint", ["run", "lint"]],
        ["web-format", ["run", "format:check"]],
        ["web-build", ["run", "build"]],
        // Verified empirically: `npm test -- --watch=false` runs the Angular/Vitest unit-test
        // builder once and exits (see file header).
        ["web-test", ["test", "--", "--watch=false"]],
      ];
      for (const [canonical, npmArgs] of webSteps) {
        const r = step(canonical, () => runCommand(NPM, npmArgs, { cwd: WEB_DIR }));
        if (!r.ok) return finish(false, [buildFailure(canonical, r)]);
      }
    }
  }

  // Step 5: api — generated TS client must match the build-time OpenAPI docs
  if (runApi) {
    const openapiDir = path.join(WEB_DIR, "openapi");
    if (!existsSync(openapiDir)) {
      console.log("\nnotice: build-time OpenAPI not set up yet (spec-00) — skipping api check");
    } else {
      const genResult = step("api: npm run gen:api", () => runCommand(NPM, ["run", "gen:api"], { cwd: WEB_DIR }));
      if (!genResult.ok) return finish(false, [buildFailure("api", genResult)]);

      const diffResult = step("api: diff web/src/app/api", () =>
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

main();
