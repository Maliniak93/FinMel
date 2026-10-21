import assert from "node:assert/strict";
import { test } from "node:test";
import { HIGHLIGHT_MS, HIGHLIGHT_PCT, classifyDelta, compareRows, escapeHtml, filterRows, renderReport } from "./test-timings-report.mjs";

const counts = (passed, failed = 0, skipped = 0) => ({ passed, failed, skipped });

const day = (n) => `2026-10-${String(n).padStart(2, "0")}T12:00:00`;

const entry = (n, testMs, over = {}) => ({
  measuredAt: day(n),
  commit: `abcdef${n}0123456`,
  dirty: false,
  status: "passed",
  setupMs: 1000,
  testMs,
  counts: counts(3),
  reliable: true,
  reasons: [],
  ...over,
});

const classEntry = (n, ms, count = 1) => ({ measuredAt: day(n), commit: `abcdef${n}0123456`, ms, count });

const suite = (over = {}) => ({
  history: [entry(10, 8000), entry(11, 8000), entry(12, 8000), entry(20, 10_000)],
  classes: {
    "N.A": [classEntry(10, 3000, 2), classEntry(11, 3000, 2), classEntry(12, 3000, 2), classEntry(20, 6000, 2)],
    "N.B": [classEntry(10, 5000), classEntry(11, 5000), classEntry(12, 5000), classEntry(20, 4000)],
  },
  tests: { "N.A.One": { ms: 2000, outcome: "passed" }, "N.A.Two": { ms: 6000, outcome: "passed" }, "N.B.Three": { ms: 4000, outcome: "passed" } },
  ...over,
});

const dataOf = (suites, over = {}) => ({
  version: 2,
  run: { startedAt: "2026-10-20T12:00:00", commit: "abcdef123456", dirty: true, branch: "feat/x", suites: Object.fromEntries(Object.keys(suites).map((k) => [k, "measured"])), reasons: {}, ...over },
  suites,
});

const sections = (html) => html.split('<details class="section"').slice(1);

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
  assert.match(html, /Względem mediany ostatnich wiarygodnych pomiarów: testy \+2\.0 s \(1 z 1 zestawów\)/);
  assert.equal(html.includes("brak wiarygodnej historii"), false);

  const first = renderReport(dataOf({ web: suite({ history: [entry(20, 10_000)] }) }, { dirty: false }));
  assert.ok(first.includes("brak wiarygodnej historii do porównania"));
  assert.equal(first.includes("<strong>dirty</strong>"), false);
});

test("suite table rows and badges", () => {
  const html = renderReport(
    dataOf(
      {
        "dotnet:Skarbiec.Ok.Tests": suite(),
        "dotnet:Skarbiec.Red.Tests": suite({ history: [entry(20, 10_000, { status: "failed", counts: counts(2, 1, 1), reliable: false, reasons: ["red-test"] })] }),
        "dotnet:Skarbiec.Old.Tests": suite({ history: [entry(1, 10_000)] }),
      },
      { suites: { "dotnet:Skarbiec.Ok.Tests": "measured", "dotnet:Skarbiec.Red.Tests": "measured", "dotnet:Skarbiec.Old.Tests": "unmeasured" }, reasons: { "dotnet:Skarbiec.Old.Tests": "Docker nie działa" } },
    ),
  );
  const rows = html.match(/<tr data-name="dotnet:[^"]*">.*?<\/tr>/g);
  assert.equal(rows.length, 3);
  const [ok, red, untouched] = rows;
  assert.match(ok, /Skarbiec\.Ok\.Tests/);
  assert.match(ok, /badge passed">passed</);
  assert.match(ok, /\+2\.0 s \(\+25%\)/);
  assert.match(red, /badge failed">failed</);
  assert.match(red, /badge unreliable" title="red-test">niewiarygodny</);
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
  const html = renderReport(dataOf({ web: suite({ tests }) }));
  const slowest = sections(html).find((s) => s.includes('id="slowest"'));
  const rows = slowest.match(/<tr data-name="[^"]*">/g);
  assert.equal(rows.length, 20);
  assert.ok(slowest.includes('data-name="N.T.Test29"'));
  assert.ok(slowest.includes('data-name="N.T.Test10"'));
  assert.equal(slowest.includes('data-name="N.T.Test09"'), false);
});

test("changes list suites and classes only", () => {
  const classes = Object.fromEntries(
    Array.from({ length: 25 }, (_, i) => [`N.Slow${String(i).padStart(2, "0")}`, [classEntry(10, 100), classEntry(11, 100), classEntry(12, 100), classEntry(20, 100 + (i + 1) * 100)]]),
  );
  classes["N.Fast"] = [classEntry(10, 9000), classEntry(11, 9000), classEntry(12, 9000), classEntry(20, 1000)];
  const html = renderReport(dataOf({ web: suite({ classes }) }));
  const changes = sections(html).find((s) => s.includes("Klasy: największe spowolnienia"));
  assert.match(changes, /Zmiany względem mediany ostatnich wiarygodnych pomiarów \(zestawy i klasy\)/);
  assert.match(changes, /Zestawy \(1\)/);
  assert.match(changes, /Klasy: największe spowolnienia \(25\)/);
  assert.match(changes, /Klasy: największe przyspieszenia \(1\)/);
  assert.equal((changes.match(/\+5 więcej/g) ?? []).length, 1);
  assert.equal((changes.match(/<li>/g) ?? []).length, 1 + 20 + 1);
  assert.match(changes, /N\.Slow24 .*?\+2\.5 s/);
  assert.equal(/Nowe testy|Usunięte testy/.test(changes), false);
  assert.ok(changes.indexOf("N.Slow24") < changes.indexOf("N.Slow23"));
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

test("unreliable current entry is shown without a comparison", () => {
  const calm = renderReport(dataOf({ web: suite() }));
  assert.equal(calm.includes('class="warning"'), false);

  const history = [entry(10, 8000), entry(11, 8000), entry(12, 8000), entry(20, 30_000, { reliable: false, reasons: ["dirty-tree", "outlier"] })];
  const html = renderReport(dataOf({ "dotnet:Dirty.Tests": suite({ history }) }));
  assert.match(html, /<div class="warning">Pomiar niewiarygodny: Dirty\.Tests \(dirty-tree, outlier\)/);
  const row = html.match(/<tr data-name="dotnet:Dirty\.Tests">.*?<\/tr>/)[0];
  assert.match(row, /<td data-v="0">—<\/td>/);
  assert.ok(html.includes("brak wiarygodnej historii do porównania"));
});

test("only the suites section is open", () => {
  const html = renderReport(dataOf({ web: suite() }));
  const all = sections(html);
  assert.equal(all.length, 4);
  assert.deepEqual(
    all.map((s) => s.startsWith(" open>")),
    [true, false, false, false],
  );
  assert.ok(all[0].includes("<summary>Zestawy</summary>"));
  assert.equal((html.match(/<details[^>]*\sopen/g) ?? []).length, 1);
  assert.ok(all[3].includes("Zestawy (") && !all[3].includes("Nowe testy"));
});

test("charts pair current bars with the median", () => {
  const html = renderReport(dataOf({ web: suite(), "dotnet:New.Tests": suite({ history: [entry(20, 10_000)], classes: { "N.Only": [classEntry(20, 50)] } }) }));
  const all = sections(html);
  const suiteChart = all.find((s) => s.includes("Zestawy: setup i testy"));
  assert.equal((suiteChart.match(/<g class="bar current">/g) ?? []).length, 2);
  assert.equal((suiteChart.match(/<g class="bar previous">/g) ?? []).length, 1);
  assert.match(suiteChart, /mediana ostatnich wiarygodnych/);
  const widths = [...suiteChart.matchAll(/<rect class="[^"]*" x="[\d.]+" y="\d+" width="([\d.]+)"/g)].map((m) => Number(m[1]));
  assert.equal(Math.max(...widths) <= 520, true);
  assert.equal(widths.some((w) => w > 400), true);
  const classChart = all.find((s) => s.includes("Klasy i pliki: "));
  assert.equal((classChart.match(/<svg class="chart"/g) ?? []).length, 1);
  assert.equal((classChart.match(/<g class="bar current">/g) ?? []).length, 3);
  assert.equal((classChart.match(/<g class="bar previous">/g) ?? []).length, 2);
});

test("history lists carry the commit", () => {
  const html = renderReport(dataOf({ web: suite() }));
  const suiteRow = html.match(/<tr data-name="web">.*?<\/tr>/)[0];
  assert.match(suiteRow, /historia \(4\)/);
  const items = [...suiteRow.matchAll(/<li[^>]*>(.*?)<\/li>/g)].map((m) => m[1]);
  assert.equal(items.length, 4);
  assert.match(items[0], /^2026-10-20 12:00 @ abcdef2 <span class="muted">10\.0 s<\/span>$/);
  assert.match(items[3], /^2026-10-10 12:00 @ abcdef1 /);

  const classes = sections(html).find((s) => s.includes('id="classes"'));
  const classRow = classes.match(/<tr data-name="N\.A">.*?<\/tr>/)[0];
  assert.match(classRow, /historia \(4\)/);
  assert.match(classRow, /<li>2026-10-20 12:00 @ abcdef2 <span class="muted">6\.0 s<\/span><\/li>/);
});

test("unreliable history entries are greyed", () => {
  const history = [entry(10, 8000), entry(11, 8000, { reliable: false, reasons: ["dirty-tree"] }), entry(12, 8000)];
  const html = renderReport(
    dataOf({
      web: suite({
        history,
        classes: { "N.A": [classEntry(10, 5), classEntry(11, 6), classEntry(12, 7)] },
      }),
    }),
  );
  const suiteRow = html.match(/<tr data-name="web">.*?<\/tr>/)[0];
  assert.equal((suiteRow.match(/<li class="unreliable" title="dirty-tree">/g) ?? []).length, 1);
  assert.match(suiteRow, /<li class="unreliable" title="dirty-tree">2026-10-11 12:00 @ abcdef1/);
  const classRow = html.match(/<tr data-name="N\.A">.*?<\/tr>/)[0];
  assert.equal((classRow.match(/<li class="unreliable"/g) ?? []).length, 1);
});

test("suite trend chart greys unreliable points", () => {
  const history = [entry(10, 8000), entry(11, 30_000, { reliable: false, reasons: ["outlier"] }), entry(12, 8000), entry(13, 9000, { reliable: false, reasons: ["dirty-tree"] })];
  const html = renderReport(dataOf({ web: suite({ history }), "dotnet:Other.Tests": suite() }));
  const trends = [...html.matchAll(/<figure class="trend">(.*?)<\/figure>/g)].map((m) => m[1]);
  assert.equal(trends.length, 2);
  const web = trends[0];
  assert.match(web, /<figcaption>web<\/figcaption>/);
  assert.equal((web.match(/class="pt ok"/g) ?? []).length, 2);
  assert.equal((web.match(/class="pt unreliable"/g) ?? []).length, 2);
  assert.equal((web.match(/<polyline/g) ?? []).length, 1);
  assert.match(web, /<title>2026-10-11 12:00 @ abcdef1: 30\.0 s<\/title>/);
  assert.equal((html.match(/<svg class="trend"/g) ?? []).length, 2);

  const classes = sections(html).find((s) => s.includes('id="classes"'));
  assert.equal(classes.includes('class="trend"'), false);
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
  const html = renderReport(
    dataOf({
      "dotnet:Evil<Tests>": suite({
        classes: { [evil]: [classEntry(20, 1)] },
        tests: { [evil]: { ms: 1, outcome: "passed" } },
        history: [entry(20, 10_000, { commit: evil })],
      }),
    }),
  );
  assert.equal(html.includes("<img"), false);
  assert.ok(html.includes("&lt;img src=x onerror=&quot;a&amp;b&quot;&gt;"));
  assert.ok(html.includes("Evil&lt;Tests&gt;"));
  assert.equal(escapeHtml(`<&">'`), "&lt;&amp;&quot;&gt;&#39;");
});
