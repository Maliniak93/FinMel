import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import {
  ALL_KEYS,
  HISTORY_LIMIT,
  assessReliability,
  classSeries,
  compareWithMedian,
  detectBusy,
  dockerRequired,
  dotnetCommands,
  emitReport,
  evaluateSilence,
  exitCodeFor,
  median,
  mergeSuite,
  parseArgs,
  parseDataFile,
  parseTrx,
  parseVitestJson,
  progressLine,
  reportOnly,
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
    pidFiles: { verify: { pid: 30 } },
    containers: { "skarbiec-local": ["a", "b"] },
    runners: [
      { name: "wsl-1", status: "online", busy: true },
      { name: "wsl-2", status: "online", busy: false },
      { name: "gone", status: "offline", busy: true },
    ],
    isAlive: alive(30),
  });

  assert.equal(busy.length, 5);
  assert.ok(busy.every((l) => l.startsWith("działa: ")));
  assert.ok(busy.some((l) => l.includes("lokalny stos") && l.includes("dotnet run") && l.includes("service")));
  assert.ok(busy.some((l) => l.includes("autobuild.mjs (pid 22)")));
  assert.ok(busy.some((l) => l.includes("verify.mjs (pid 30)")));
  assert.ok(busy.some((l) => l.includes("kontenery skarbiec-local (2)")));
  assert.ok(busy.some((l) => l.includes("runner wsl-1")));
});

test("detectBusy ignores a dead pid file and an autobuild-like script name", () => {
  const { busy } = detectBusy({
    ...quiet,
    processes: [proc(40, "node.exe", "node --test scripts/autobuild-watch.test.mjs")],
    pidFiles: { verify: { pid: 30 } },
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

test("docker is required only for dotnet", () => {
  assert.equal(dockerRequired(["web"]), false);
  assert.equal(dockerRequired(["dotnet:Skarbiec.Contracts.Tests"]), true);
  assert.equal(dockerRequired(["web", "dotnet:Skarbiec.Contracts.Tests"]), true);

  assert.deepEqual(evaluateSilence({ keys: ["web"], dockerUp: false, ...quiet }).lines, []);
  for (const keys of [["dotnet:Skarbiec.Contracts.Tests"], ALL_KEYS]) {
    const { lines } = evaluateSilence({ keys, dockerUp: false, ...quiet });
    assert.equal(lines.length, 1);
    assert.match(lines[0], /^Docker nie jest uruchomiony/);
  }
  assert.deepEqual(evaluateSilence({ keys: ALL_KEYS, dockerUp: true, ...quiet }).lines, []);
});

const meta = { commit: "abc", dirty: false, branch: "feat/x" };
const outcome = (ms, over = {}) => ({
  measured: true,
  setupMs: 1000,
  testMs: ms,
  counts: { passed: 2, failed: 0, skipped: 0 },
  classes: { "N.A": { ms, count: 2 } },
  tests: { "N.A.One": { ms: ms / 2, outcome: "passed" }, "N.A.Two": { ms: ms / 2, outcome: "passed" } },
  ...over,
});

test("busy in the middle stops the run", async () => {
  const keys = ALL_KEYS;
  assert.equal(keys.length, 9);
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
  assert.equal(result.results.length, 9);
  assert.deepEqual(result.results.slice(0, 3).map((r) => r.status), ["passed", "passed", "passed"]);
  for (const r of result.results.slice(3)) {
    assert.equal(r.status, "unmeasured");
    assert.match(r.reason, /działa: runner wsl-1/);
  }
  assert.deepEqual(progress.map((p) => p[0]), [1, 2, 3, 4, 5, 6, 7, 8, 9]);
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
    keys: ["web", "dotnet:Skarbiec.Contracts.Tests"],
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

const day = (n) => `2026-09-${String(n).padStart(2, "0")}T10:00:00Z`;
const calm = { passed: 2, failed: 0, skipped: 0 };
const entryAt = (n, testMs, over = {}) => ({
  measuredAt: day(n),
  commit: `commit${n}`,
  dirty: false,
  status: "passed",
  setupMs: 1000,
  testMs,
  counts: calm,
  reliable: true,
  reasons: [],
  ...over,
});
const withHistory = (history, classes = {}, tests = {}) => ({ history, classes, tests });

test("mergeSuite keeps going on red", () => {
  const red = outcome(4000, { counts: { passed: 1, failed: 1, skipped: 0 } });
  const first = mergeSuite(null, "web", red, { measuredAt: "2026-10-13T10:00:00Z", ...meta });
  assert.equal(first.status, "failed");
  assert.equal(first.data.version, 2);
  assert.equal(first.data.suites.web.history[0].status, "failed");
  assert.equal(first.data.suites.web.history[0].reliable, false);
  assert.deepEqual(first.data.suites.web.history[0].reasons, ["red-test"]);

  const second = mergeSuite(first.data, "web", outcome(3000), { measuredAt: "2026-10-14T10:00:00Z", ...meta });
  assert.equal(second.status, "passed");
  assert.deepEqual(second.data.suites.web.history.map((e) => e.testMs), [4000, 3000]);
  assert.equal(second.data.suites.web.history[1].reliable, true);
});

test("a run appends one entry per suite", async () => {
  const keys = ["web", "dotnet:Skarbiec.Contracts.Tests"];
  const run = (data, at) =>
    runSuites({ keys, data, git: { ...meta, dirty: true }, check: async () => [], measure: async () => outcome(2000), write: () => {}, now: () => new Date(at) });
  const first = await run(null, "2026-10-13T10:00:00Z");
  const second = await run(first.data, "2026-10-14T10:00:00Z");

  for (const key of keys) {
    const history = second.data.suites[key].history;
    assert.equal(history.length, 2);
    assert.deepEqual(Object.keys(history[1]).sort(), ["commit", "counts", "dirty", "measuredAt", "reasons", "reliable", "setupMs", "status", "testMs"]);
    assert.deepEqual(history[1], {
      measuredAt: "2026-10-14T10:00:00.000Z",
      commit: "abc",
      dirty: true,
      status: "passed",
      setupMs: 1000,
      testMs: 2000,
      counts: { passed: 2, failed: 0, skipped: 0 },
      reliable: false,
      reasons: ["dirty-tree"],
    });
  }
  const source = readFileSync(fileURLToPath(import.meta.url).replace(".test.mjs", ".mjs"), "utf8");
  assert.equal(source.includes("last-run.json"), false);
});

test("history keeps the last 30 entries", () => {
  assert.equal(HISTORY_LIMIT, 30);
  let data = null;
  for (let i = 1; i <= 31; i++) data = mergeSuite(data, "web", outcome(2000), { measuredAt: `t${String(i).padStart(2, "0")}`, ...meta }).data;
  const suite = data.suites.web;
  assert.equal(suite.history.length, 30);
  assert.equal(suite.history[0].measuredAt, "t02");
  assert.equal(suite.history.at(-1).measuredAt, "t31");
  assert.equal(suite.classes["N.A"].length, 30);
  assert.equal(suite.classes["N.A"][0].measuredAt, "t02");
});

test("class history follows the classes of the latest measurement", () => {
  const first = mergeSuite(null, "web", outcome(2000, { classes: { "N.A": { ms: 1500, count: 2 }, "N.B": { ms: 500, count: 1 } } }), { measuredAt: "t1", ...meta });
  const second = mergeSuite(first.data, "web", outcome(2000, { classes: { "N.B": { ms: 600, count: 3 }, "N.C": { ms: 100, count: 1 } } }), { measuredAt: "t2", commit: "def", dirty: false, branch: "x" });
  const classes = second.data.suites.web.classes;
  assert.deepEqual(Object.keys(classes).sort(), ["N.B", "N.C"]);
  assert.deepEqual(classes["N.B"], [
    { measuredAt: "t1", commit: "abc", ms: 500, count: 1 },
    { measuredAt: "t2", commit: "def", ms: 600, count: 3 },
  ]);
  assert.deepEqual(classes["N.C"], [{ measuredAt: "t2", commit: "def", ms: 100, count: 1 }]);
});

test("tests are kept for the latest measurement only", () => {
  const first = mergeSuite(null, "web", outcome(2000, { tests: { "N.A.Old": { ms: 5, outcome: "passed" } } }), { measuredAt: "t1", ...meta });
  const second = mergeSuite(first.data, "web", outcome(3000), { measuredAt: "t2", ...meta });
  const suite = second.data.suites.web;
  assert.deepEqual(suite.tests, outcome(3000).tests);
  assert.deepEqual(Object.keys(suite).sort(), ["classes", "history", "tests"]);
  assert.equal(JSON.stringify(second.data).includes("N.A.Old"), false);
  assert.ok(suite.history.every((e) => !("tests" in e) && !("classes" in e)));
});

test("mergeSuite leaves an unmeasured suite alone", () => {
  const good = mergeSuite(null, "web", outcome(3000), { measuredAt: "t1", ...meta }).data;
  const again = mergeSuite(good, "web", outcome(2000), { measuredAt: "t2", ...meta }).data;
  for (const bad of [{ measured: false, reason: "build nie przeszedł" }, outcome(1000, { counts: { passed: 0, failed: 0, skipped: 0 } })]) {
    const result = mergeSuite(again, "web", bad, { measuredAt: "t3", ...meta });
    assert.equal(result.status, "unmeasured");
    assert.ok(result.reason);
    assert.deepEqual(result.data.suites.web, again.suites.web);
    assert.equal(result.data.suites.web.history.length, 2);
  }
});

test("partial run merges per suite", () => {
  let data = mergeSuite(null, "web", outcome(3000), { measuredAt: "2026-10-13T10:00:00Z", ...meta }).data;
  data = mergeSuite(data, "dotnet:Skarbiec.Contracts.Tests", outcome(9000), { measuredAt: "2026-10-13T10:05:00Z", ...meta }).data;

  const next = mergeSuite(data, "web", outcome(2500), { measuredAt: "2026-10-20T10:00:00Z", ...meta }).data;
  assert.deepEqual(next.suites.web.history.map((e) => e.measuredAt), ["2026-10-13T10:00:00Z", "2026-10-20T10:00:00Z"]);
  assert.deepEqual(next.suites["dotnet:Skarbiec.Contracts.Tests"], data.suites["dotnet:Skarbiec.Contracts.Tests"]);
});

test("partial run records the other suites as not selected", async () => {
  const writes = [];
  await runSuites({ keys: ["web"], data: null, git: meta, check: async () => [], measure: async () => outcome(1000), write: (d) => writes.push(d) });
  const run = writes.at(-1).run;
  assert.equal(run.suites.web, "measured");
  assert.equal(run.suites["dotnet:Skarbiec.Portfolio.Tests"], "not-selected");
  assert.equal(run.branch, "feat/x");
  assert.deepEqual(Object.keys(writes.at(-1).suites), ["web"]);
});

test("corrupt file means no history", () => {
  assert.equal(parseDataFile("{ nope"), null);
  assert.equal(parseDataFile(""), null);
  assert.equal(parseDataFile(JSON.stringify({ version: 1, suites: {} })), null);
  assert.equal(parseDataFile(JSON.stringify({ version: 2, suites: [] })), null);
  assert.equal(parseDataFile(JSON.stringify({ version: 2, suites: { web: { history: [], classes: {}, tests: {} } } })), null);

  const merged = mergeSuite(parseDataFile("{ nope"), "web", outcome(3000), { measuredAt: "t", ...meta });
  assert.equal(merged.data.suites.web.history.length, 1);
  assert.equal(parseDataFile(JSON.stringify(merged.data)).suites.web.history[0].testMs, 3000);
});

test("median", () => {
  assert.equal(median([]), null);
  assert.equal(median([5]), 5);
  assert.equal(median([9, 1, 5]), 5);
  assert.equal(median([4, 1, 3, 2]), 2.5);
});

test("reliability flags", () => {
  const steady = [entryAt(1, 10_000), entryAt(2, 10_000), entryAt(3, 10_000)];
  const clean = { dirty: false, counts: calm, testMs: 10_000 };
  const redCounts = { passed: 1, failed: 1, skipped: 0 };
  const cases = [
    ["a steady run is reliable", steady, clean, []],
    ["a first measurement is reliable", [], clean, []],
    ["a dirty tracked tree", steady, { ...clean, dirty: true }, ["dirty-tree"]],
    ["a red test", steady, { ...clean, counts: redCounts }, ["red-test"]],
    ["dirty and red give both reasons", steady, { ...clean, dirty: true, counts: redCounts }, ["dirty-tree", "red-test"]],
    ["25 percent above is still fine", steady, { ...clean, testMs: 12_500 }, []],
    ["just over 25 percent above", steady, { ...clean, testMs: 12_501 }, ["outlier"]],
    ["25 percent below is still fine", steady, { ...clean, testMs: 7500 }, []],
    ["just over 25 percent below", steady, { ...clean, testMs: 7499 }, ["outlier"]],
    ["two reliable entries never flag an outlier", steady.slice(1), { ...clean, testMs: 30_000 }, []],
    ["unreliable entries do not count towards the three", [entryAt(1, 10_000), entryAt(2, 50_000, { reliable: false }), entryAt(3, 10_000)], { ...clean, testMs: 30_000 }, []],
    ["the median ignores unreliable entries", [...steady, entryAt(4, 90_000, { reliable: false })], { ...clean, testMs: 12_000 }, []],
    ["only the last five reliable entries count", [entryAt(1, 100_000), ...[2, 3, 4, 5, 6].map((n) => entryAt(n, 10_000))], clean, []],
    ["a slow outlier on a long history", [1, 2, 3, 4, 5, 6].map((n) => entryAt(n, 10_000)), { ...clean, testMs: 20_000 }, ["outlier"]],
  ];
  for (const [name, history, input, reasons] of cases) {
    assert.deepEqual(assessReliability(history, input), { reliable: reasons.length === 0, reasons }, name);
  }
});

test("comparison uses the median of reliable entries", () => {
  const history = [
    entryAt(1, 99_000),
    entryAt(2, 10_000),
    entryAt(3, 12_000),
    entryAt(4, 50_000, { reliable: false }),
    entryAt(5, 11_000),
    entryAt(6, 13_000),
    entryAt(7, 14_000),
    entryAt(8, 16_000),
  ];
  assert.deepEqual(compareWithMedian(history), { ms: 16_000, baselineMs: 12_000, deltaMs: 4000, baselineSize: 5 });
  assert.deepEqual(compareWithMedian(history, "setupMs"), { ms: 1000, baselineMs: 1000, deltaMs: 0, baselineSize: 5 });
  assert.equal(compareWithMedian([entryAt(1, 10_000), entryAt(2, 12_000)]).baselineMs, 10_000);
});

test("unreliable current entry has no comparison", () => {
  const steady = [entryAt(1, 10_000), entryAt(2, 10_000), entryAt(3, 10_000)];
  assert.equal(compareWithMedian([...steady, entryAt(4, 30_000, { reliable: false, reasons: ["outlier"] })]), null);
  assert.equal(compareWithMedian([entryAt(1, 10_000)]), null);
  assert.equal(compareWithMedian([entryAt(1, 10_000, { reliable: false }), entryAt(2, 10_000)]), null);
  assert.equal(compareWithMedian([]), null);
});

test("class history is as reliable as the suite entry of the same measurement", () => {
  const suite = withHistory([entryAt(1, 10_000), entryAt(2, 10_000, { reliable: false })], {
    "N.A": [
      { measuredAt: day(1), commit: "c1", ms: 5, count: 1 },
      { measuredAt: day(2), commit: "c2", ms: 6, count: 1 },
    ],
  });
  assert.deepEqual(classSeries(suite, "N.A").map((c) => c.reliable), [true, false]);
  assert.deepEqual(classSeries(suite, "N.Gone"), []);
});

const keyed = (key, entry, comparison = null) => ({ key, entry, comparison });
const cmp = (deltaMs) => ({ ms: 0, baselineMs: 0, deltaMs, baselineSize: 3 });

test("summarize", () => {
  const a = entryAt(2, 72_000, { setupMs: 21_000, counts: { passed: 1000, failed: 0, skipped: 0 } });
  const b = entryAt(2, 750_000, { setupMs: 30_000, counts: { passed: 284, failed: 0, skipped: 0 } });
  const line = summarize([keyed("dotnet:Skarbiec.Portfolio.Tests", a, cmp(28_000)), keyed("web", b, cmp(4000))]);
  assert.equal(line, "Razem 14:33 (testy +0:32 vs mediana), testów 1 284, 0 czerwonych. Najwięcej urosło: Skarbiec.Portfolio.Tests +0:28");

  const first = summarize([keyed("web", entryAt(1, 71_000, { counts: { passed: 1500, failed: 2, skipped: 0 } }))]);
  assert.equal(first, "Razem 1:12 (brak wiarygodnej historii do porównania), testów 1 502, 2 czerwonych.");
  assert.equal(summarize([]), "Nic nie zmierzono.");

  const unreliable = summarize([keyed("web", entryAt(1, 71_000, { reliable: false, reasons: ["dirty-tree", "red-test"], counts: { passed: 2, failed: 1, skipped: 0 } }))]);
  assert.match(unreliable, /1 czerwonych\. Pomiar niewiarygodny: web \(dirty-tree, red-test\)$/);
});

test("progressLine", () => {
  const entry = entryAt(1, 72_000, { setupMs: 21_000, counts: { passed: 312, failed: 0, skipped: 0 } });
  assert.equal(progressLine(3, 9, "dotnet:Skarbiec.Portfolio.Tests", { status: "passed", entry }), "[3/9] dotnet Skarbiec.Portfolio.Tests — setup 0:21, testy 1:12, 312 testów, 0 czerwonych");
  assert.equal(progressLine(9, 9, "web", { status: "unmeasured", reason: "0 testów" }), "[9/9] web — niezmierzony: 0 testów");
  const flagged = entryAt(1, 72_000, { reliable: false, reasons: ["outlier"] });
  assert.match(progressLine(1, 9, "web", { status: "passed", entry: flagged }), /, niewiarygodny \(outlier\)$/);
});

test("exitCodeFor", () => {
  assert.equal(exitCodeFor([{ status: "passed" }, { status: "passed" }]), 0);
  assert.equal(exitCodeFor([{ status: "passed" }, { status: "unmeasured" }]), 3);
  assert.equal(exitCodeFor([{ status: "failed" }, { status: "passed" }]), 1);
  assert.equal(exitCodeFor([{ status: "failed" }, { status: "unmeasured" }]), 1);
});

test("arguments select suites", () => {
  assert.deepEqual(parseArgs([]), { only: null, check: false, help: false, open: false, reportOnly: false });
  assert.deepEqual(parseArgs(["--only", "web,dotnet", "--check"]), { only: ["web", "dotnet"], check: true, help: false, open: false, reportOnly: false });
  assert.equal(parseArgs(["--open", "--report-only"]).open, true);
  assert.equal(parseArgs(["--report-only"]).reportOnly, true);
  assert.match(parseArgs(["--only", "web,nope"]).error, /--only takes dotnet,web \(unknown: nope\)/);
  assert.ok(parseArgs(["--wat"]).error);
  assert.deepEqual(selectSuites(["web"]), ["web"]);
  assert.deepEqual(selectSuites(null), ALL_KEYS);
  assert.equal(selectSuites(["dotnet"]).length, ALL_KEYS.length - 1);
  assert.equal(ALL_KEYS.at(-1), "web");
});

test("stop-stack exports the process detection", () => {
  assert.equal(typeof listProcesses, "function");
  assert.deepEqual(findStack([proc(1, "node.exe", "node x.js")]), []);
  assert.equal(findStack([proc(2, "node.exe", "node ng serve")]).length, 1);
});

test("the script is documented once and its data directory is ignored", () => {
  const root = fileURLToPath(new URL("..", import.meta.url));
  const claude = readFileSync(`${root}CLAUDE.md`, "utf8").split("\n");
  const row = claude.find((l) => l.includes("scripts/test-timings.mjs"));
  assert.equal(claude.filter((l) => l.includes("scripts/test-timings.mjs")).length, 1);
  assert.ok(readFileSync(`${root}.gitignore`, "utf8").split("\n").includes("skarbiec-plan/test-timings/"));
  const header = readFileSync(fileURLToPath(import.meta.url).replace(".test.mjs", ".mjs"), "utf8").split("\n").slice(0, 6).join("\n");
  for (const text of ["--only dotnet,web", "--check", "--open", "--report-only", "history.json"]) {
    assert.ok(header.includes(text), `header names ${text}`);
    assert.ok(row.includes(text), `CLAUDE.md row names ${text}`);
  }
});

const fixtureData = () => ({
  version: 2,
  run: { startedAt: "2026-10-20T12:00:00", commit: "abcdef123456", dirty: false, branch: "feat/x", suites: { web: "measured" }, reasons: {} },
  suites: {
    web: withHistory(
      [entryAt(1, 10_000), entryAt(2, 10_000), entryAt(3, 12_000)],
      { "N.A": [{ measuredAt: day(3), commit: "c3", ms: 100, count: 1 }] },
      { "N.A.One": { ms: 100, outcome: "passed" } },
    ),
  },
});

const sink = () => {
  const calls = { written: [], opened: [], logs: [] };
  return {
    calls,
    options: {
      file: "/tmp/skarbiec/report.html",
      write: (file, text) => calls.written.push({ file, text }),
      opener: (file) => calls.opened.push(file),
      log: (line) => calls.logs.push(line),
    },
  };
};

test("report is written after a run", () => {
  const { calls, options } = sink();
  emitReport(fixtureData(), options);
  assert.equal(calls.written.length, 1);
  assert.ok(calls.written[0].text.startsWith("<!doctype html>"));
  assert.ok(calls.logs.some((l) => l.includes("report.html")));
  assert.equal(calls.opened.length, 0);
});

test("report-only needs no measuring", () => {
  const { calls, options } = sink();
  assert.equal(reportOnly({ read: () => JSON.stringify(fixtureData()), ...options }), 0);
  assert.equal(calls.written.length, 1);
  const missing = sink();
  assert.equal(reportOnly({ read: () => null, ...missing.options }), 3);
  assert.equal(missing.calls.written.length, 0);
  assert.ok(missing.calls.logs.length > 0);
});

test("open flag calls the opener", () => {
  const { calls, options } = sink();
  emitReport(fixtureData(), { ...options, open: true });
  assert.deepEqual(calls.opened, ["/tmp/skarbiec/report.html"]);
});
