import assert from "node:assert/strict";
import { test } from "node:test";
import { HIGHLIGHT_MS, HIGHLIGHT_PCT, classifyDelta, compareRows, escapeHtml, filterRows, renderReport } from "./test-timings-report.mjs";

const counts = (passed, failed = 0, skipped = 0) => ({ passed, failed, skipped });

const suite = (over = {}) => ({
  kind: "web",
  measuredAt: "2026-10-20T12:00:00",
  commit: "abc",
  dirty: false,
  branch: "feat/x",
  status: "passed",
  setupMs: 1000,
  testMs: 10_000,
  counts: counts(3),
  classes: { "N.A": { ms: 6000, count: 2 }, "N.B": { ms: 4000, count: 1 } },
  tests: { "N.A.One": { ms: 2000, outcome: "passed" }, "N.A.Two": { ms: 6000, outcome: "passed" }, "N.B.Three": { ms: 4000, outcome: "passed" } },
  previous: {
    kind: "web",
    measuredAt: "2026-10-13T12:00:00",
    status: "passed",
    setupMs: 1500,
    testMs: 8000,
    counts: counts(3),
    classes: { "N.A": { ms: 3000, count: 2 }, "N.B": { ms: 5000, count: 1 } },
    tests: { "N.A.One": { ms: 1000, outcome: "passed" }, "N.A.Two": { ms: 2000, outcome: "passed" }, "N.B.Three": { ms: 5000, outcome: "passed" } },
  },
  ...over,
});

const dataOf = (suites, over = {}) => ({
  version: 1,
  run: { startedAt: "2026-10-20T12:00:00", commit: "abcdef123456", dirty: true, branch: "feat/x", suites: Object.fromEntries(Object.keys(suites).map((k) => [k, "measured"])), reasons: {}, ...over },
  suites,
});

const sections = (html) => html.split("<details").slice(1);

test("report is self-contained", () => {
  const html = renderReport(dataOf({ web: suite() }));
  assert.ok(html.startsWith("<!doctype html>"));
  assert.equal(/<link\b/i.test(html), false);
  assert.equal(/<script[^>]*\ssrc=/i.test(html), false);
  assert.equal(/https?:\/\//i.test(html), false);
  assert.match(html, /@media \(prefers-color-scheme: dark\)/);
});

test("header shows totals and comparison", () => {
  const html = renderReport(dataOf({ web: suite() }));
  assert.match(html, /2026-10-20 12:00/);
  assert.match(html, /abcdef12/);
  assert.match(html, /dirty/);
  assert.match(html, /feat\/x/);
  assert.match(html, /setup 1\.0 s, testy 10\.0 s/);
  assert.match(html, /testów 3/);
  assert.match(html, /z 2026-10-13: setup -500 ms, testy \+2\.0 s, liczba testów 0/);
  assert.equal(html.includes("brak poprzedniego przebiegu"), false);

  const first = renderReport(dataOf({ web: suite({ previous: null }) }, { dirty: false }));
  assert.ok(first.includes("brak poprzedniego przebiegu"));
  assert.equal(first.includes("<strong>dirty</strong>"), false);
});

test("suite table rows and badges", () => {
  const html = renderReport(
    dataOf(
      {
        "dotnet:Skarbiec.Ok.Tests": suite({ kind: "dotnet" }),
        "dotnet:Skarbiec.Red.Tests": suite({ kind: "dotnet", status: "failed", counts: counts(2, 1, 1) }),
        e2e: suite({ kind: "e2e", measuredAt: "2026-10-01T08:00:00", previous: null }),
      },
      { suites: { "dotnet:Skarbiec.Ok.Tests": "measured", "dotnet:Skarbiec.Red.Tests": "measured", e2e: "unmeasured" }, reasons: { e2e: "Docker nie działa" } },
    ),
  );
  const rows = html.match(/<tr data-name="(dotnet:[^"]*|e2e)">.*?<\/tr>/g);
  assert.equal(rows.length, 3);
  const [ok, red, untouched] = rows;
  assert.match(ok, /Skarbiec\.Ok\.Tests/);
  assert.match(ok, /badge passed">passed</);
  assert.match(ok, /\+2\.0 s \(\+25%\)/);
  assert.match(red, /badge failed">failed</);
  assert.match(red, /<td data-v="1">1<\/td>/);
  assert.match(untouched, /badge unmeasured" title="Docker nie działa">nie zmierzony</);
  assert.match(untouched, /2026-10-01/);
});

test("class table", () => {
  const html = renderReport(dataOf({ web: suite() }));
  const classes = sections(html).find((s) => s.includes('id="classes"'));
  const rows = classes.match(/<tr data-name="N\.[AB]">.*?<\/tr>/g);
  assert.equal(rows.length, 2);
  assert.match(rows[0], /6\.0 s/);
  assert.match(rows[0], /\+3\.0 s \(\+100%\)/);
  assert.match(rows[0], /delta slower/);
  assert.match(rows[1], /-1\.0 s \(-20%\)/);
  assert.match(rows[1], /delta plain/);
});

const manyTests = (n, ms) => Object.fromEntries(Array.from({ length: n }, (_, i) => [`N.T.Test${String(i).padStart(2, "0")}`, { ms: ms(i), outcome: "passed" }]));

test("top 20 slowest", () => {
  const tests = manyTests(30, (i) => 100 + i * 10);
  const html = renderReport(dataOf({ web: suite({ tests, counts: counts(30), previous: suite().previous }) }));
  const slowest = sections(html).find((s) => s.includes('id="slowest"'));
  const rows = slowest.match(/<tr data-name="[^"]*">/g);
  assert.equal(rows.length, 20);
  assert.ok(slowest.includes('data-name="N.T.Test29"'));
  assert.ok(slowest.includes('data-name="N.T.Test10"'));
  assert.equal(slowest.includes('data-name="N.T.Test09"'), false);

  const withDelta = renderReport(dataOf({ web: suite() }));
  assert.match(sections(withDelta).find((s) => s.includes('id="slowest"')), /data-name="N\.A\.Two".*?\+4\.0 s \(\+200%\)/);
});

test("new removed and slowdowns capped", () => {
  const before = manyTests(25, () => 100);
  const after = Object.fromEntries(Object.entries(manyTests(25, (i) => 100 + (i + 1) * 100)).map(([k, v]) => [k.replace("T.Test", "T.Slow"), v]));
  const same = manyTests(25, (i) => 100 + (i + 1) * 100);
  const html = renderReport(
    dataOf({
      web: suite({
        tests: { ...same, ...after },
        counts: counts(50),
        previous: { ...suite().previous, counts: counts(25), tests: before, classes: {} },
      }),
    }),
  );
  const changes = sections(html).find((s) => s.includes("Nowe testy"));
  assert.match(changes, /Nowe testy \(25\)/);
  assert.match(changes, /Usunięte testy \(0\)/);
  assert.match(changes, /Największe spowolnienia \(25\)/);
  assert.equal((changes.match(/<li>/g) ?? []).length, 20 + 20);
  assert.equal((changes.match(/\+5 więcej/g) ?? []).length, 2);

  const gone = renderReport(dataOf({ web: suite({ tests: {}, previous: { ...suite().previous, tests: manyTests(22, () => 5) } }) }));
  assert.match(gone, /Usunięte testy \(22\)/);
  assert.match(gone, /\+2 więcej/);
});

test("classifyDelta applies both thresholds", () => {
  assert.equal(HIGHLIGHT_PCT, 10);
  assert.equal(HIGHLIGHT_MS, 2000);
  assert.equal(classifyDelta(1500, 3000), "plain");
  assert.equal(classifyDelta(30_000, 600_000), "plain");
  assert.equal(classifyDelta(3000, 15_000), "slower");
  assert.equal(classifyDelta(-4000, 13_333), "faster");
  assert.equal(classifyDelta(0, 0), "plain");
});

test("uncertain comparison warns", () => {
  const calm = renderReport(dataOf({ web: suite() }));
  assert.equal(calm.includes('class="warning"'), false);
  const red = renderReport(dataOf({ "dotnet:Red.Tests": suite({ status: "failed", counts: counts(2, 1) }) }));
  assert.match(red, /<div class="warning">Porównanie niepewne: czerwone testy w Red\.Tests/);
  const prevRed = renderReport(dataOf({ web: suite({ previous: { ...suite().previous, status: "failed", counts: counts(2, 1) } }) }));
  assert.ok(prevRed.includes('class="warning"'));
});

test("charts pair current and previous bars", () => {
  const html = renderReport(dataOf({ web: suite(), "dotnet:New.Tests": suite({ previous: null }) }));
  const all = sections(html);
  const suiteChart = all.find((s) => s.includes("Zestawy: setup i testy"));
  assert.equal((suiteChart.match(/<g class="bar current">/g) ?? []).length, 2);
  assert.equal((suiteChart.match(/<g class="bar previous">/g) ?? []).length, 1);
  assert.equal((suiteChart.match(/<svg/g) ?? []).length, 1);
  const widths = [...suiteChart.matchAll(/<rect class="[^"]*" x="[\d.]+" y="\d+" width="([\d.]+)"/g)].map((m) => Number(m[1]));
  assert.equal(Math.max(...widths) <= 520, true);
  assert.equal(widths.some((w) => w > 400), true);
  const classChart = all.find((s) => s.includes("Klasy i pliki: "));
  assert.equal((classChart.match(/<g class="bar current">/g) ?? []).length, 4);
  assert.equal((classChart.match(/<g class="bar previous">/g) ?? []).length, 2);
  assert.equal(all.length, 4);
  for (const s of all) assert.ok(s.startsWith(" open>") || s.startsWith(">"));
});

const rows = [
  { name: "Beta", values: ["Beta", 30] },
  { name: "alpha", values: ["alpha", 5] },
  { name: "Gamma", values: ["Gamma", 100] },
];

test("compareRows", () => {
  assert.deepEqual([...rows].sort(compareRows(1, 1)).map((r) => r.name), ["alpha", "Beta", "Gamma"]);
  assert.deepEqual([...rows].sort(compareRows(1, -1)).map((r) => r.name), ["Gamma", "Beta", "alpha"]);
  assert.deepEqual([...rows].sort(compareRows(0, 1)).map((r) => r.name), ["alpha", "Beta", "Gamma"]);
});

test("filterRows", () => {
  assert.deepEqual(filterRows(rows, "ETA").map((r) => r.name), ["Beta"]);
  assert.deepEqual(filterRows(rows, "  a ").map((r) => r.name), ["Beta", "alpha", "Gamma"]);
  assert.equal(filterRows(rows, "").length, 3);
  assert.equal(filterRows(rows, "zzz").length, 0);
});

test("page embeds the helpers", () => {
  const html = renderReport(dataOf({ web: suite() }));
  assert.ok(html.includes(compareRows.toString()));
  assert.ok(html.includes(filterRows.toString()));
  const script = html.match(/<script>([\s\S]*)<\/script>/)[1];
  assert.doesNotThrow(() => new Function("document", script)({ querySelectorAll: () => [] }));
});

test("names are escaped", () => {
  const evil = '<img src=x onerror="a&b">';
  const html = renderReport(dataOf({ "dotnet:Evil<Tests>": suite({ classes: { [evil]: { ms: 1, count: 1 } }, tests: { [evil]: { ms: 1, outcome: "passed" } } }) }));
  assert.equal(html.includes("<img"), false);
  assert.ok(html.includes("&lt;img src=x onerror=&quot;a&amp;b&quot;&gt;"));
  assert.ok(html.includes("Evil&lt;Tests&gt;"));
  assert.equal(escapeHtml(`<&">'`), "&lt;&amp;&quot;&gt;&#39;");
});
