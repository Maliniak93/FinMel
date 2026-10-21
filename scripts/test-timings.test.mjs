import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import {
  ALL_KEYS,
  computeDeltas,
  detectBusy,
  dockerRequired,
  dotnetCommands,
  evaluateSilence,
  exitCodeFor,
  mergeSuite,
  parseArgs,
  parseDataFile,
  parsePlaywrightJson,
  parseTrx,
  parseVitestJson,
  progressLine,
  runSuites,
  selectSuites,
  summarize,
  webCommands,
} from "./test-timings.mjs";
import { findStack, listProcesses } from "./stop-stack.mjs";

const alive = (...pids) => (pid) => pids.includes(pid);
const proc = (pid, name, cmd) => ({ pid, ppid: 1, name, cmd });
const quiet = { processes: [proc(10, "node.exe", "node other.js")], pidFiles: {}, containers: {}, runners: [], isAlive: alive() };

test("detectBusy names every busy source", () => {
  assert.deepEqual(detectBusy(quiet), { busy: [], warnings: [] });

  const { busy } = detectBusy({
    processes: [
      proc(20, "dotnet.exe", "dotnet run --project Skarbiec.AppHost"),
      proc(21, "Skarbiec.Portfolio.exe", "Skarbiec.Portfolio.exe"),
      proc(22, "node.exe", "node scripts/autobuild.mjs --items all"),
    ],
    pidFiles: { verify: { pid: 30 }, e2e: { pid: 31 } },
    containers: { "skarbiec-local": ["a", "b"], "skarbiec-e2e": ["c"] },
    runners: [
      { name: "wsl-1", status: "online", busy: true },
      { name: "wsl-2", status: "online", busy: false },
      { name: "gone", status: "offline", busy: true },
    ],
    isAlive: alive(30, 31),
  });

  assert.equal(busy.length, 7);
  assert.ok(busy.every((l) => l.startsWith("działa: ")));
  assert.ok(busy.some((l) => l.includes("lokalny stos") && l.includes("dotnet run") && l.includes("service")));
  assert.ok(busy.some((l) => l.includes("autobuild.mjs (pid 22)")));
  assert.ok(busy.some((l) => l.includes("verify.mjs (pid 30)")));
  assert.ok(busy.some((l) => l.includes("e2e.mjs (pid 31)")));
  assert.ok(busy.some((l) => l.includes("kontenery skarbiec-local (2)")));
  assert.ok(busy.some((l) => l.includes("kontenery skarbiec-e2e (1)")));
  assert.ok(busy.some((l) => l.includes("runner wsl-1")));
});

test("detectBusy ignores a dead pid file and an autobuild-like script name", () => {
  const { busy } = detectBusy({
    ...quiet,
    processes: [proc(40, "node.exe", "node --test scripts/autobuild-watch.test.mjs")],
    pidFiles: { verify: { pid: 30 }, e2e: { pid: 31 } },
    isAlive: alive(),
  });
  assert.deepEqual(busy, []);
});

test("detectBusy tolerates missing gh", () => {
  const result = detectBusy({ ...quiet, runners: null });
  assert.deepEqual(result.busy, []);
  assert.deepEqual(result.warnings, ["runner nie sprawdzony"]);
});

test("second instance is refused", () => {
  const other = detectBusy({ ...quiet, pidFiles: { timings: { pid: 50 } }, isAlive: alive(50) });
  assert.equal(other.busy.length, 1);
  assert.match(other.busy[0], /^działa: inny test-timings\.mjs \(pid 50\)/);

  const own = detectBusy({ ...quiet, pidFiles: { timings: { pid: process.pid } }, isAlive: alive(process.pid) });
  assert.deepEqual(own.busy, []);
});

test("docker is required only for dotnet and e2e", () => {
  assert.equal(dockerRequired(["web"]), false);
  assert.equal(dockerRequired(["dotnet:Skarbiec.Contracts.Tests"]), true);
  assert.equal(dockerRequired(["web", "e2e"]), true);

  assert.deepEqual(evaluateSilence({ keys: ["web"], dockerUp: false, ...quiet }).lines, []);
  for (const keys of [["dotnet:Skarbiec.Contracts.Tests"], ["e2e"], ALL_KEYS]) {
    const { lines } = evaluateSilence({ keys, dockerUp: false, ...quiet });
    assert.equal(lines.length, 1);
    assert.match(lines[0], /^Docker nie jest uruchomiony/);
  }
  assert.deepEqual(evaluateSilence({ keys: ALL_KEYS, dockerUp: true, ...quiet }).lines, []);
});

const meta = { commit: "abc", dirty: false, branch: "feat/x" };
const outcome = (ms, over = {}) => ({
  measured: true,
  kind: "dotnet",
  setupMs: 1000,
  testMs: ms,
  counts: { passed: 2, failed: 0, skipped: 0 },
  classes: { "N.A": { ms, count: 2 } },
  tests: { "N.A.One": { ms: ms / 2, outcome: "passed" }, "N.A.Two": { ms: ms / 2, outcome: "passed" } },
  ...over,
});

test("busy in the middle stops the run", async () => {
  const keys = ALL_KEYS.slice(0, 10);
  assert.equal(keys.length, 10);
  const writes = [];
  const checked = [];
  const progress = [];
  const result = await runSuites({
    keys,
    data: null,
    git: meta,
    check: async (remaining, index) => {
      checked.push(index);
      return index === 3 ? ["działa: runner wsl-1 (zajęty)"] : [];
    },
    measure: async () => outcome(2000),
    write: (data) => writes.push(structuredClone(data)),
    onSuite: (i, total, key, r) => progress.push([i, total, key, r.status]),
  });

  assert.equal(result.refused, null);
  assert.deepEqual(result.interrupted, ["działa: runner wsl-1 (zajęty)"]);
  assert.equal(result.results.length, 10);
  assert.deepEqual(result.results.slice(0, 3).map((r) => r.status), ["passed", "passed", "passed"]);
  for (const r of result.results.slice(3)) {
    assert.equal(r.status, "unmeasured");
    assert.match(r.reason, /działa: runner wsl-1/);
  }
  assert.deepEqual(progress.map((p) => p[0]), [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
  assert.equal(exitCodeFor(result.results), 3);

  assert.equal(writes.length, 4);
  assert.deepEqual(Object.keys(writes[2].suites), keys.slice(0, 3));
  const last = writes.at(-1);
  assert.deepEqual(Object.keys(last.suites), keys.slice(0, 3));
  assert.equal(last.run.suites[keys[0]], "measured");
  assert.equal(last.run.suites[keys[5]], "unmeasured");
  assert.match(last.run.reasons[keys[5]], /maszyna zajęta/);
});

test("a busy machine at the start refuses and writes nothing", async () => {
  const writes = [];
  const result = await runSuites({
    keys: ["web", "e2e"],
    data: null,
    git: meta,
    check: async () => ["działa: verify.mjs (pid 1)"],
    measure: async () => assert.fail("must not measure"),
    write: (d) => writes.push(d),
  });
  assert.deepEqual(result.refused, ["działa: verify.mjs (pid 1)"]);
  assert.equal(writes.length, 0);
});

const trx = `<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult executionId="1" testId="a" testName="N.Calc.Add(a: 1, b: 2)" duration="00:00:00.0100000" outcome="Passed" />
    <UnitTestResult executionId="2" testId="b" testName="N.Calc.Add(a: 3, b: &quot;x&quot;)" duration="00:00:00.0250000" outcome="Passed" />
    <UnitTestResult executionId="3" testId="c" testName="N.Calc.Div" duration="00:00:01.5000000" outcome="Failed">
      <Output><ErrorInfo><Message>boom (x)</Message></ErrorInfo></Output>
    </UnitTestResult>
    <UnitTestResult executionId="4" testId="d" testName="N.Other.Skipped" duration="00:00:00.0000000" outcome="NotExecuted" />
    <UnitTestResult executionId="5" testId="e" testName="N.Calc.Add(a: 5, b: 6)" duration="00:00:00.0050000" outcome="Failed" />
  </Results>
</TestRun>`;

test("parseTrx", () => {
  const parsed = parseTrx(trx);
  assert.deepEqual(parsed.counts, { passed: 2, failed: 2, skipped: 1 });
  assert.deepEqual(Object.keys(parsed.tests).sort(), ["N.Calc.Add", "N.Calc.Div", "N.Other.Skipped"]);
  assert.deepEqual(parsed.tests["N.Calc.Add"], { ms: 40, outcome: "failed" });
  assert.deepEqual(parsed.tests["N.Calc.Div"], { ms: 1500, outcome: "failed" });
  assert.deepEqual(parsed.tests["N.Other.Skipped"], { ms: 0, outcome: "skipped" });
  assert.deepEqual(parsed.classes["N.Calc"], { ms: 1540, count: 4 });
  assert.deepEqual(parsed.classes["N.Other"], { ms: 0, count: 1 });
});

test("dotnetCommands", () => {
  const { build, test: run } = dotnetCommands("a/B.csproj", { trxName: "r.trx", resultsDir: "/tmp/x" });
  assert.deepEqual(build, { bin: "dotnet", args: ["build", "a/B.csproj"] });
  assert.deepEqual(run.args.slice(0, 3), ["test", "a/B.csproj", "--no-build"]);
  assert.ok(run.args.includes("trx;LogFileName=r.trx"));
  assert.equal(run.args[run.args.indexOf("--results-directory") + 1], "/tmp/x");
  assert.ok(run.args.includes("--logger"));

  const web = webCommands({ outputFile: "/tmp/v.json" });
  assert.deepEqual(web.install.args, ["install", "--no-audit", "--no-fund"]);
  assert.deepEqual(web.test.args, ["test", "--", "--watch=false", "--reporters=json", "--output-file=/tmp/v.json"]);
});

test("parseVitestJson", () => {
  const report = {
    testResults: [
      {
        name: "C:\\repo\\web\\src\\app\\a.spec.ts",
        assertionResults: [
          { ancestorTitles: ["AComponent", "when empty"], title: "shows hint", status: "passed", duration: 12.5 },
          { ancestorTitles: ["AComponent"], title: "breaks", status: "failed", duration: 3 },
          { ancestorTitles: [], title: "later", status: "pending", duration: null },
        ],
      },
      { name: "C:/repo/web/src/app/b.spec.ts", assertionResults: [{ ancestorTitles: ["B"], title: "ok", status: "passed", duration: 1 }] },
    ],
  };
  const parsed = parseVitestJson(JSON.stringify(report), { root: "C:\\repo\\web" });
  assert.deepEqual(parsed.counts, { passed: 2, failed: 1, skipped: 1 });
  assert.deepEqual(parsed.tests["src/app/a.spec.ts › AComponent › when empty › shows hint"], { ms: 12.5, outcome: "passed" });
  assert.deepEqual(parsed.tests["src/app/a.spec.ts › AComponent › breaks"], { ms: 3, outcome: "failed" });
  assert.deepEqual(parsed.tests["src/app/a.spec.ts › later"], { ms: 0, outcome: "skipped" });
  assert.deepEqual(parsed.classes["src/app/a.spec.ts"], { ms: 15.5, count: 3 });
  assert.deepEqual(parsed.classes["src/app/b.spec.ts"], { ms: 1, count: 1 });
});

test("parsePlaywrightJson", () => {
  const started = Date.parse("2026-10-21T10:00:00.000Z");
  const report = {
    stats: { startTime: "2026-10-21T10:03:20.500Z", duration: 61234.5 },
    suites: [
      {
        title: "specs/a.spec.ts",
        file: "specs/a.spec.ts",
        specs: [{ title: "top level", file: "specs/a.spec.ts", tests: [{ status: "expected", results: [{ duration: 1000 }] }] }],
        suites: [
          {
            title: "Dashboard",
            file: "specs/a.spec.ts",
            specs: [
              {
                title: "flaky one",
                file: "specs/a.spec.ts",
                tests: [{ status: "flaky", results: [{ duration: 9000, status: "failed" }, { duration: 2500, status: "passed" }] }],
              },
              { title: "red", file: "specs/a.spec.ts", tests: [{ status: "unexpected", results: [{ duration: 700 }] }] },
            ],
            suites: [{ title: "nested", specs: [{ title: "deep", file: "specs/a.spec.ts", tests: [{ status: "skipped", results: [] }] }] }],
          },
        ],
      },
    ],
  };
  const parsed = parsePlaywrightJson(report, { commandStartedAt: started });
  assert.equal(parsed.testMs, 61235);
  assert.equal(parsed.setupMs, 200_500);
  assert.deepEqual(parsed.counts, { passed: 2, failed: 1, skipped: 1 });
  assert.deepEqual(parsed.tests["specs/a.spec.ts › top level"], { ms: 1000, outcome: "passed" });
  assert.deepEqual(parsed.tests["specs/a.spec.ts › Dashboard › flaky one"], { ms: 2500, outcome: "passed" });
  assert.deepEqual(parsed.tests["specs/a.spec.ts › Dashboard › red"], { ms: 700, outcome: "failed" });
  assert.deepEqual(parsed.tests["specs/a.spec.ts › Dashboard › nested › deep"], { ms: 0, outcome: "skipped" });
  assert.equal(parsed.classes["specs/a.spec.ts"].count, 4);
});

test("mergeSuite keeps going on red", () => {
  const red = outcome(4000, { counts: { passed: 1, failed: 1, skipped: 0 } });
  const first = mergeSuite(null, "web", red, { measuredAt: "2026-10-13T10:00:00Z", ...meta });
  assert.equal(first.status, "failed");
  assert.equal(first.data.suites.web.status, "failed");
  assert.equal(first.data.suites.web.previous, null);

  const second = mergeSuite(first.data, "web", outcome(3000), { measuredAt: "2026-10-14T10:00:00Z", ...meta });
  assert.equal(second.status, "passed");
  assert.equal(second.data.suites.web.testMs, 3000);
  assert.equal(second.data.suites.web.previous.testMs, 4000);
  assert.equal(second.data.suites.web.previous.status, "failed");
  assert.equal("previous" in second.data.suites.web.previous, false);
});

test("mergeSuite leaves an unmeasured suite alone", () => {
  const good = mergeSuite(null, "web", outcome(3000), { measuredAt: "t1", ...meta }).data;
  const again = mergeSuite(good, "web", outcome(2000), { measuredAt: "t2", ...meta }).data;
  for (const bad of [{ measured: false, reason: "build nie przeszedł" }, outcome(1000, { counts: { passed: 0, failed: 0, skipped: 0 } })]) {
    const result = mergeSuite(again, "web", bad, { measuredAt: "t3", ...meta });
    assert.equal(result.status, "unmeasured");
    assert.ok(result.reason);
    assert.deepEqual(result.data.suites.web, again.suites.web);
    assert.equal(result.data.suites.web.previous.testMs, 3000);
  }
});

test("partial run merges per suite", () => {
  let data = mergeSuite(null, "web", outcome(3000), { measuredAt: "2026-10-13T10:00:00Z", ...meta }).data;
  data = mergeSuite(data, "e2e", outcome(9000, { kind: "e2e" }), { measuredAt: "2026-10-13T10:05:00Z", ...meta }).data;

  const next = mergeSuite(data, "web", outcome(2500), { measuredAt: "2026-10-20T10:00:00Z", ...meta }).data;
  assert.equal(next.suites.web.measuredAt, "2026-10-20T10:00:00Z");
  assert.equal(next.suites.web.previous.measuredAt, "2026-10-13T10:00:00Z");
  assert.deepEqual(next.suites.e2e, data.suites.e2e);
  assert.equal(next.suites.e2e.measuredAt, "2026-10-13T10:05:00Z");
});

test("partial run records the other suites as not selected", async () => {
  const writes = [];
  await runSuites({ keys: ["web"], data: null, git: meta, check: async () => [], measure: async () => outcome(1000), write: (d) => writes.push(d) });
  const run = writes.at(-1).run;
  assert.equal(run.suites.web, "measured");
  assert.equal(run.suites.e2e, "not-selected");
  assert.equal(run.suites["dotnet:Skarbiec.Portfolio.Tests"], "not-selected");
  assert.equal(run.branch, "feat/x");
});

test("corrupt file means no previous", () => {
  assert.equal(parseDataFile("{ nope"), null);
  assert.equal(parseDataFile(""), null);
  assert.equal(parseDataFile(JSON.stringify({ version: 2, suites: {} })), null);
  assert.equal(parseDataFile(JSON.stringify({ version: 1, suites: [] })), null);

  const merged = mergeSuite(parseDataFile("{ nope"), "web", outcome(3000), { measuredAt: "t", ...meta });
  assert.equal(merged.data.suites.web.previous, null);
  assert.equal(computeDeltas(merged.data.suites.web), null);
  assert.deepEqual(parseDataFile(JSON.stringify(merged.data)).suites.web.testMs, 3000);
});

const entryOf = (over) => ({
  status: "passed",
  setupMs: 1000,
  testMs: 10_000,
  counts: { passed: 3, failed: 0, skipped: 0 },
  classes: { "N.A": { ms: 6000, count: 2 }, "N.B": { ms: 4000, count: 1 }, "N.New": { ms: 100, count: 1 } },
  tests: {
    "N.A.One": { ms: 2000, outcome: "passed" },
    "N.A.Two": { ms: 4000, outcome: "passed" },
    "N.B.Three": { ms: 4000, outcome: "passed" },
    "N.New.Fresh": { ms: 100, outcome: "passed" },
  },
  previous: {
    status: "passed",
    setupMs: 1500,
    testMs: 8000,
    counts: { passed: 3, failed: 0, skipped: 0 },
    classes: { "N.A": { ms: 3000, count: 2 }, "N.B": { ms: 5000, count: 1 }, "N.Gone": { ms: 50, count: 1 } },
    tests: {
      "N.A.One": { ms: 1000, outcome: "passed" },
      "N.A.Two": { ms: 2000, outcome: "passed" },
      "N.B.Three": { ms: 5000, outcome: "passed" },
      "N.Gone.Old": { ms: 50, outcome: "passed" },
    },
  },
  ...over,
});

test("computeDeltas", () => {
  const d = computeDeltas(entryOf());
  assert.equal(d.uncertain, false);
  assert.equal(d.setupMs.deltaMs, -500);
  assert.equal(d.testMs.deltaMs, 2000);
  assert.deepEqual(d.testCount, { count: 3, previousCount: 3, delta: 0 });
  assert.equal(d.classes["N.A"].deltaMs, 3000);
  assert.equal(d.classes["N.B"].deltaMs, -1000);
  assert.equal("N.New" in d.classes, false);
  assert.equal(d.tests["N.A.Two"].deltaMs, 2000);
  assert.equal(d.tests["N.B.Three"].deltaMs, -1000);
  assert.deepEqual(d.newTests, ["N.New.Fresh"]);
  assert.deepEqual(d.removedTests, ["N.Gone.Old"]);
  assert.deepEqual(d.slowdowns, [
    { id: "N.A.Two", deltaMs: 2000 },
    { id: "N.A.One", deltaMs: 1000 },
  ]);
  assert.deepEqual(d.classSlowdowns, [{ id: "N.A", deltaMs: 3000 }]);

  assert.equal(computeDeltas(entryOf({ status: "failed" })).uncertain, true);
  const prevRed = entryOf();
  prevRed.previous.counts = { passed: 2, failed: 1, skipped: 0 };
  assert.equal(computeDeltas(prevRed).uncertain, true);
  assert.equal(computeDeltas(entryOf({ previous: null })), null);
});

const keyed = (key, entry) => ({ key, entry });
const ago = (over = {}) => ({ ...entryOf().previous, measuredAt: "2026-10-13T12:00:00", ...over });

test("summarize", () => {
  const a = entryOf({ setupMs: 21_000, testMs: 72_000, counts: { passed: 1000, failed: 0, skipped: 0 }, previous: ago({ setupMs: 21_000, testMs: 44_000, counts: { passed: 990, failed: 0, skipped: 0 } }) });
  const b = entryOf({ setupMs: 30_000, testMs: 750_000, counts: { passed: 284, failed: 0, skipped: 0 }, previous: ago({ setupMs: 30_000, testMs: 746_000, counts: { passed: 282, failed: 0, skipped: 0 } }) });
  const line = summarize([keyed("dotnet:Skarbiec.Portfolio.Tests", a), keyed("web", b)]);
  assert.equal(line, "Razem 14:33 (+0:32 vs 2026-10-13), testów 1 284 (+12), 0 czerwonych. Najwięcej urosło: Skarbiec.Portfolio.Tests +0:28");
  assert.match(line, /^Razem \d+:\d\d \([+-]\d+:\d\d vs \d{4}-\d\d-\d\d\), testów [\d ]+ \([+-][\d ]+\), \d+ czerwonych\. Najwięcej urosło: \S+ \+\d+:\d\d$/);

  const first = summarize([keyed("web", entryOf({ previous: null, setupMs: 1000, testMs: 71_000, counts: { passed: 1500, failed: 2, skipped: 0 } }))]);
  assert.equal(first, "Razem 1:12 (brak poprzedniego przebiegu), testów 1 502, 2 czerwonych.");
  assert.equal(summarize([]), "Nic nie zmierzono.");

  const red = summarize([keyed("web", entryOf({ status: "failed", counts: { passed: 2, failed: 1, skipped: 0 } }))]);
  assert.match(red, /Porównanie niepewne \(czerwone testy\): web$/);
  assert.match(red, /1 czerwonych/);
});

test("progressLine", () => {
  const entry = entryOf({ setupMs: 21_000, testMs: 72_000, counts: { passed: 312, failed: 0, skipped: 0 } });
  assert.equal(progressLine(3, 10, "dotnet:Skarbiec.Portfolio.Tests", { status: "passed", entry }), "[3/10] dotnet Skarbiec.Portfolio.Tests — setup 0:21, testy 1:12, 312 testów, 0 czerwonych");
  assert.equal(progressLine(9, 10, "web", { status: "unmeasured", reason: "0 testów" }), "[9/10] web — niezmierzony: 0 testów");
});

test("exitCodeFor", () => {
  assert.equal(exitCodeFor([{ status: "passed" }, { status: "passed" }]), 0);
  assert.equal(exitCodeFor([{ status: "passed" }, { status: "unmeasured" }]), 3);
  assert.equal(exitCodeFor([{ status: "failed" }, { status: "passed" }]), 1);
  assert.equal(exitCodeFor([{ status: "failed" }, { status: "unmeasured" }]), 1);
});

test("arguments select suites", () => {
  assert.deepEqual(parseArgs([]), { only: null, check: false, help: false });
  assert.deepEqual(parseArgs(["--only", "web,e2e", "--check"]), { only: ["web", "e2e"], check: true, help: false });
  assert.ok(parseArgs(["--only", "nope"]).error);
  assert.ok(parseArgs(["--wat"]).error);
  assert.deepEqual(selectSuites(["web"]), ["web"]);
  assert.deepEqual(selectSuites(null), ALL_KEYS);
  assert.equal(selectSuites(["dotnet"]).length, ALL_KEYS.length - 2);
  assert.equal(ALL_KEYS.at(-2), "web");
  assert.equal(ALL_KEYS.at(-1), "e2e");
});

test("stop-stack exports the process detection", () => {
  assert.equal(typeof listProcesses, "function");
  assert.deepEqual(findStack([proc(1, "node.exe", "node x.js")]), []);
  assert.equal(findStack([proc(2, "node.exe", "node ng serve")]).length, 1);
});

test("the script is documented once and its data directory is ignored", () => {
  const root = fileURLToPath(new URL("..", import.meta.url));
  const claude = readFileSync(`${root}CLAUDE.md`, "utf8").split("\n");
  assert.equal(claude.filter((l) => l.includes("scripts/test-timings.mjs")).length, 1);
  assert.ok(readFileSync(`${root}.gitignore`, "utf8").split("\n").includes("skarbiec-plan/test-timings/"));
  const header = readFileSync(fileURLToPath(import.meta.url).replace(".test.mjs", ".mjs"), "utf8").split("\n").slice(0, 6).join("\n");
  for (const flag of ["--only", "--check"]) assert.ok(header.includes(flag));
});
