// Usage: imported by test-timings.mjs - renders the self-contained report.html from last-run.json data (renderReport) plus the pure comparator, row filter and delta classifier the page script embeds; runs nothing on import

import { computeDeltas } from "./test-timings.mjs";

export const HIGHLIGHT_PCT = 10;
export const HIGHLIGHT_MS = 2000;
const LIST_CAP = 20;
const CHART_CLASSES = 20;

export function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
}

export function classifyDelta(deltaMs, previousMs) {
  const pct = previousMs > 0 ? (Math.abs(deltaMs) / previousMs) * 100 : Math.abs(deltaMs) > 0 ? Infinity : 0;
  if (Math.abs(deltaMs) <= HIGHLIGHT_MS || pct <= HIGHLIGHT_PCT) return "plain";
  return deltaMs > 0 ? "slower" : "faster";
}

export function compareRows(index, direction = 1) {
  return (a, b) => {
    const x = a.values[index];
    const y = b.values[index];
    const order = typeof x === "number" && typeof y === "number" ? x - y : String(x).localeCompare(String(y), "pl");
    return order * direction;
  };
}

export function filterRows(rows, query) {
  const needle = String(query ?? "").trim().toLowerCase();
  return rows.filter((r) => String(r.name).toLowerCase().includes(needle));
}

const pad2 = (n) => String(n).padStart(2, "0");

function localDateTime(iso) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return String(iso ?? "");
  return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())} ${pad2(d.getHours())}:${pad2(d.getMinutes())}`;
}

const localDate = (iso) => localDateTime(iso).slice(0, 10);

function formatMs(ms) {
  const abs = Math.abs(ms);
  if (abs < 1000) return `${Math.round(abs)} ms`;
  if (abs < 60_000) return `${(abs / 1000).toFixed(1)} s`;
  const s = Math.round(abs / 1000);
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  return h > 0 ? `${h}:${pad2(m)}:${pad2(s % 60)}` : `${m}:${pad2(s % 60)}`;
}

const signed = (ms) => (ms === 0 ? formatMs(0) : `${ms < 0 ? "-" : "+"}${formatMs(ms)}`);
const signedCount = (n) => (n === 0 ? "0" : `${n < 0 ? "-" : "+"}${Math.abs(n)}`);
const total = (counts) => counts.passed + counts.failed + counts.skipped;

const e = escapeHtml;

function cell(text, { value, cls } = {}) {
  const v = value === undefined ? "" : ` data-v="${e(value)}"`;
  const c = cls ? ` class="${cls}"` : "";
  return `<td${v}${c}>${e(text)}</td>`;
}

function deltaCell(ms, previousMs) {
  if (ms === null || ms === undefined) return cell("—", { value: 0 });
  const pct = previousMs > 0 ? ` (${ms >= 0 ? "+" : "-"}${Math.round((Math.abs(ms) / previousMs) * 100)}%)` : "";
  return cell(`${signed(ms)}${pct}`, { value: ms, cls: `delta ${classifyDelta(ms, previousMs)}` });
}

function countDeltaCell(delta) {
  return cell(delta === null ? "—" : signedCount(delta), { value: delta ?? 0, cls: "delta" });
}

function table(id, headers, rows, { filter = true } = {}) {
  const head = headers.map((h) => `<th title="Sortuj">${e(h)}</th>`).join("");
  const body = rows.map(({ name, cells }) => `<tr data-name="${e(name)}">${cells.join("")}</tr>`).join("");
  const box = filter ? `<input class="filter" type="search" placeholder="Szukaj po nazwie" aria-label="Szukaj po nazwie">` : "";
  return `${box}<div class="scroll"><table class="sortable" id="${id}"><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table></div>`;
}

function section(title, body, open = true) {
  return `<details${open ? " open" : ""}><summary>${e(title)}</summary>${body}</details>`;
}

function cappedList(title, items, render) {
  if (!items.length) return `<h3>${e(title)} (0)</h3><p class="muted">brak</p>`;
  const shown = items.slice(0, LIST_CAP).map(render).join("");
  const more = items.length > LIST_CAP ? `<li class="muted">+${items.length - LIST_CAP} więcej</li>` : "";
  return `<h3>${e(title)} (${items.length})</h3><ul>${shown}${more}</ul>`;
}

function chart(title, items) {
  const max = Math.max(1, ...items.flatMap((i) => [i.current.reduce((a, b) => a + b.ms, 0), i.previous ? i.previous.reduce((a, b) => a + b.ms, 0) : 0]));
  const labelW = 300;
  const barW = 520;
  const rowH = 38;
  const scale = (ms) => (ms / max) * barW;
  const stack = (segments, y, h, klass) => {
    let x = labelW;
    const rects = segments
      .map((s) => {
        const w = scale(s.ms);
        const r = `<rect class="${s.cls}" x="${x.toFixed(1)}" y="${y}" width="${w.toFixed(1)}" height="${h}"><title>${e(s.label)}: ${e(formatMs(s.ms))}</title></rect>`;
        x += w;
        return r;
      })
      .join("");
    return `<g class="bar ${klass}">${rects}</g>`;
  };
  const rows = items
    .map((item, i) => {
      const y = i * rowH;
      const label = item.name.length > 44 ? `…${item.name.slice(-43)}` : item.name;
      const sum = (s) => s.reduce((a, b) => a + b.ms, 0);
      const current = stack(item.current, y + 4, 14, "current");
      const previous = item.previous ? stack(item.previous, y + 20, 10, "previous") : "";
      const value = `<text class="value" x="${(labelW + scale(sum(item.current)) + 6).toFixed(1)}" y="${y + 15}">${e(formatMs(sum(item.current)))}</text>`;
      return `<g><text class="label" x="0" y="${y + 18}"><title>${e(item.name)}</title>${e(label)}</text>${current}${previous}${value}</g>`;
    })
    .join("");
  const height = Math.max(rowH, items.length * rowH);
  const legend = `<p class="legend"><span class="swatch setup"></span>setup <span class="swatch tests"></span>testy <span class="swatch previous"></span>poprzedni przebieg</p>`;
  return `<h3>${e(title)}</h3>${legend}<svg class="chart" viewBox="0 0 ${labelW + barW + 90} ${height}" role="img" aria-label="${e(title)}">${rows}</svg>`;
}

function suiteLabel(key) {
  return key.startsWith("dotnet:") ? key.slice("dotnet:".length) : key;
}

const CSS = `
:root{--bg:#fff;--fg:#1c2330;--muted:#667085;--line:#d9dee7;--card:#f6f8fb;--red:#c62828;--green:#2e7d32;--setup:#7b8cde;--tests:#2f6fed;--previous:#b8bfcc;--warn-bg:#fff4ce;--warn-fg:#6b4e00}
@media (prefers-color-scheme: dark){:root{--bg:#14181f;--fg:#e4e8ef;--muted:#98a2b3;--line:#2b3340;--card:#1c222c;--red:#ff7b72;--green:#5fd068;--setup:#6a78c9;--tests:#4c8dff;--previous:#58606e;--warn-bg:#3a2f0b;--warn-fg:#ffd866}}
*{box-sizing:border-box}
body{margin:0 auto;max-width:1200px;padding:24px;font:14px/1.45 system-ui,sans-serif;background:var(--bg);color:var(--fg)}
h1{margin:0 0 4px;font-size:22px}h3{margin:16px 0 4px;font-size:14px}
.muted{color:var(--muted)}
.header{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:12px 16px;margin:12px 0}
.header p{margin:4px 0}
.warning{background:var(--warn-bg);color:var(--warn-fg);border-radius:8px;padding:10px 16px;margin:12px 0;font-weight:600}
details{border:1px solid var(--line);border-radius:8px;margin:12px 0;padding:0 16px 12px}
summary{cursor:pointer;font-weight:600;padding:10px 0}
.scroll{overflow-x:auto}
table{border-collapse:collapse;width:100%;margin-top:8px}
th,td{padding:4px 8px;border-bottom:1px solid var(--line);text-align:right;white-space:nowrap}
th:first-child,td:first-child{text-align:left;white-space:normal;word-break:break-all}
th{cursor:pointer;user-select:none;position:sticky;top:0;background:var(--card)}
.delta.slower{color:var(--red);font-weight:600}.delta.faster{color:var(--green);font-weight:600}
.badge{display:inline-block;padding:1px 8px;border-radius:10px;font-size:12px;border:1px solid var(--line)}
.badge.passed{color:var(--green)}.badge.failed{color:var(--red)}.badge.skipped,.badge.unmeasured{color:var(--muted)}
input.filter{margin-top:8px;padding:4px 8px;width:260px;background:var(--bg);color:var(--fg);border:1px solid var(--line);border-radius:4px}
ul{margin:4px 0;padding-left:20px}li{word-break:break-all}
svg.chart{width:100%;height:auto}
svg .label,svg .value{fill:var(--fg);font-size:12px}
svg .bar .setup{fill:var(--setup)}svg .bar .tests{fill:var(--tests)}svg .bar.previous rect{fill:var(--previous)}
.legend{margin:2px 0}.swatch{display:inline-block;width:10px;height:10px;margin:0 4px 0 12px}
.swatch.setup{background:var(--setup)}.swatch.tests{background:var(--tests)}.swatch.previous{background:var(--previous)}
`;

const SCRIPT = `
${compareRows.toString()}
${filterRows.toString()}
document.querySelectorAll("table.sortable").forEach(function (tableEl) {
  var body = tableEl.tBodies[0];
  var rows = Array.prototype.map.call(body.rows, function (el) {
    return { el: el, name: el.dataset.name, values: Array.prototype.map.call(el.cells, function (c) { return c.dataset.v !== undefined ? Number(c.dataset.v) : c.textContent.trim(); }) };
  });
  var sorted = { col: -1, dir: 1 };
  Array.prototype.forEach.call(tableEl.tHead.rows[0].cells, function (th, i) {
    th.addEventListener("click", function () {
      var dir = sorted.col === i ? -sorted.dir : 1;
      sorted = { col: i, dir: dir };
      rows.sort(compareRows(i, dir));
      rows.forEach(function (r) { body.appendChild(r.el); });
    });
  });
  var box = tableEl.parentElement.previousElementSibling;
  if (box && box.classList.contains("filter")) {
    box.addEventListener("input", function () {
      var keep = filterRows(rows, box.value);
      rows.forEach(function (r) { r.el.hidden = keep.indexOf(r) === -1; });
    });
  }
});
`;

function sum(list, f) {
  return list.reduce((acc, x) => acc + f(x), 0);
}

export function renderReport(data) {
  const run = data.run ?? null;
  const keys = Object.keys(data.suites);
  const suites = keys.map((key) => ({ key, entry: data.suites[key], deltas: computeDeltas(data.suites[key]), state: run?.suites?.[key] ?? "measured" }));
  const compared = suites.filter((s) => s.deltas);

  const setupMs = sum(suites, (s) => s.entry.setupMs);
  const testMs = sum(suites, (s) => s.entry.testMs);
  const count = sum(suites, (s) => total(s.entry.counts));

  const headerLines = [
    `<p><strong>${e(run ? localDateTime(run.startedAt) : "brak daty")}</strong> · commit <code>${e((run?.commit ?? "?").slice(0, 8))}</code>${run?.dirty ? " <strong>dirty</strong>" : ""} · gałąź <code>${e(run?.branch ?? "?")}</code></p>`,
    `<p>Razem ${e(formatMs(setupMs + testMs))}: setup ${e(formatMs(setupMs))}, testy ${e(formatMs(testMs))} · testów ${count}</p>`,
  ];
  if (compared.length) {
    const since = compared.map((s) => s.entry.previous.measuredAt).sort()[0];
    const dSetup = sum(compared, (s) => s.deltas.setupMs.deltaMs);
    const dTests = sum(compared, (s) => s.deltas.testMs.deltaMs);
    const dCount = sum(compared, (s) => s.deltas.testCount.delta);
    headerLines.push(`<p>Względem przebiegu z ${e(localDate(since))}: setup ${e(signed(dSetup))}, testy ${e(signed(dTests))}, liczba testów ${e(signedCount(dCount))}</p>`);
  } else headerLines.push(`<p class="muted">brak poprzedniego przebiegu</p>`);

  const uncertain = compared.filter((s) => s.deltas.uncertain).map((s) => suiteLabel(s.key));
  const warning = uncertain.length ? `<div class="warning">Porównanie niepewne: czerwone testy w ${e(uncertain.join(", "))}. Czasy nie są wiarygodne.</div>` : "";

  const suiteRows = suites.map(({ key, entry, deltas, state }) => {
    const untouched = state !== "measured";
    const badgeClass = untouched ? "unmeasured" : entry.status;
    const badgeText = untouched ? "nie zmierzony" : entry.status === "failed" ? "failed" : "passed";
    const reason = run?.reasons?.[key];
    return {
      name: key,
      cells: [
        cell(suiteLabel(key), { value: undefined }),
        cell(formatMs(entry.setupMs), { value: entry.setupMs }),
        cell(formatMs(entry.testMs), { value: entry.testMs }),
        cell(String(entry.counts.passed), { value: entry.counts.passed }),
        cell(String(entry.counts.failed), { value: entry.counts.failed }),
        cell(String(entry.counts.skipped), { value: entry.counts.skipped }),
        deltaCell(deltas?.testMs.deltaMs ?? null, entry.previous?.testMs),
        countDeltaCell(deltas?.testCount.delta ?? null),
        `<td><span class="badge ${badgeClass}"${reason && untouched ? ` title="${e(reason)}"` : ""}>${badgeText}</span></td>`,
        cell(localDate(entry.measuredAt), { value: undefined }),
      ],
    };
  });
  const suiteTable = table("suites", ["Zestaw", "Setup", "Testy", "Zaliczone", "Nieudane", "Pominięte", "Δ czasu testów", "Δ liczby", "Status", "Zmierzono"], suiteRows);

  const classRows = suites.flatMap(({ key, entry, deltas }) =>
    Object.entries(entry.classes).map(([name, c]) => {
      const d = deltas?.classes[name];
      return {
        name,
        cells: [
          cell(name),
          cell(suiteLabel(key)),
          cell(formatMs(c.ms), { value: c.ms }),
          cell(String(c.count), { value: c.count }),
          deltaCell(d?.deltaMs ?? null, d?.previousMs),
          countDeltaCell(d ? d.count - d.previousCount : null),
        ],
      };
    }),
  );
  const classTable = table("classes", ["Klasa / plik", "Zestaw", "Czas", "Testów", "Δ czasu", "Δ liczby"], classRows);

  const allTests = suites.flatMap(({ key, entry, deltas }) => Object.entries(entry.tests).map(([id, t]) => ({ id, suite: key, ms: t.ms, delta: deltas?.tests[id] ?? null })));
  const slowest = [...allTests].sort((a, b) => b.ms - a.ms).slice(0, LIST_CAP);
  const slowRows = slowest.map((t) => ({
    name: t.id,
    cells: [cell(t.id), cell(suiteLabel(t.suite)), cell(formatMs(t.ms), { value: t.ms }), deltaCell(t.delta?.deltaMs ?? null, t.delta?.previousMs)],
  }));
  const slowTable = table("slowest", ["Test", "Zestaw", "Czas", "Δ czasu"], slowRows);

  const added = compared.flatMap((s) => s.deltas.newTests.map((id) => ({ id, suite: s.key })));
  const removed = compared.flatMap((s) => s.deltas.removedTests.map((id) => ({ id, suite: s.key })));
  const slowdowns = compared
    .flatMap((s) => Object.entries(s.deltas.tests).filter(([, d]) => d.deltaMs > 0).map(([id, d]) => ({ id, suite: s.key, ...d })))
    .sort((a, b) => b.deltaMs - a.deltaMs);
  const changes = [
    cappedList("Nowe testy", added, (t) => `<li>${e(t.id)} <span class="muted">${e(suiteLabel(t.suite))}</span></li>`),
    cappedList("Usunięte testy", removed, (t) => `<li>${e(t.id)} <span class="muted">${e(suiteLabel(t.suite))}</span></li>`),
    cappedList("Największe spowolnienia", slowdowns, (t) => `<li>${e(t.id)} <span class="delta ${classifyDelta(t.deltaMs, t.previousMs)}">${e(signed(t.deltaMs))}</span> <span class="muted">${e(formatMs(t.previousMs))} → ${e(formatMs(t.ms))}</span></li>`),
  ].join("");

  const suiteChart = chart(
    "Zestawy: setup i testy",
    suites.map(({ key, entry }) => ({
      name: suiteLabel(key),
      current: [{ cls: "setup", ms: entry.setupMs, label: "setup" }, { cls: "tests", ms: entry.testMs, label: "testy" }],
      previous: entry.previous ? [{ cls: "setup", ms: entry.previous.setupMs, label: "setup" }, { cls: "tests", ms: entry.previous.testMs, label: "testy" }] : null,
    })),
  );
  const classChart = chart(
    `Klasy i pliki: ${CHART_CLASSES} najdłuższych`,
    suites
      .flatMap(({ entry }) =>
        Object.entries(entry.classes).map(([name, c]) => ({
          name,
          current: [{ cls: "tests", ms: c.ms, label: "czas" }],
          previous: entry.previous?.classes[name] ? [{ cls: "tests", ms: entry.previous.classes[name].ms, label: "czas" }] : null,
        })),
      )
      .sort((a, b) => b.current[0].ms - a.current[0].ms)
      .slice(0, CHART_CLASSES),
  );

  return `<!doctype html>
<html lang="pl">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Czasy testów — Skarbiec</title>
<style>${CSS}</style>
</head>
<body>
<h1>Czasy testów</h1>
<div class="header">${headerLines.join("")}</div>
${warning}
${section("Zestawy", suiteTable + suiteChart)}
${section("Klasy i pliki", classTable + classChart)}
${section("20 najwolniejszych testów", slowTable)}
${section("Zmiany od poprzedniego przebiegu", changes)}
<script>${SCRIPT}</script>
</body>
</html>
`;
}
