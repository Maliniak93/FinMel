import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { LABELS } from "./extract.mjs";

const MERMAID = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "docs", "vendor", "mermaid.min.js");

const KIND_TITLE = { command: "Komendy", workflow: "Workflow", agent: "Agenci", script: "Skrypty" };
const KIND_LABEL = { command: "komenda", workflow: "workflow", agent: "agent", script: "skrypt" };
const LEGEND = {
  "zmienia git": "wykonuje git add/commit/push/merge/switch/checkout/reset/restore/stash/branch/pull/rebase albo gh issue develop --checkout",
  merguje: "wykonuje gh pr merge",
  "pisze do GitHuba": "gh issue create/edit/close/comment/develop, gh pr create/edit/comment/merge, gh project item-*",
  "dotyka lokalnego stacku": "jest lub wywołuje compose.mjs albo stop-stack.mjs",
  "dotyka izolowanego stacku e2e": "jest lub wywołuje e2e.mjs",
  "pełny verify": "wywołuje verify.mjs z --all",
};
const FACT_LABEL = {
  argumentHint: "argument-hint",
  allowedTools: "allowed-tools",
  disableModelInvocation: "disable-model-invocation",
  whenToUse: "when to use",
  disallowedTools: "disallowedTools",
};

const STYLE = `
body { font-family: system-ui, sans-serif; margin: 0; color: #1d2433; background: #f7f8fa; }
header { background: #1d2433; color: #fff; padding: 0.75rem 2rem; }
main { max-width: 1200px; margin: 1.5rem auto; padding: 0 2rem; }
table { border-collapse: collapse; width: 100%; margin: 0.5rem 0 1rem; background: #fff; }
th, td { border: 1px solid #d9dee8; padding: 0.35rem 0.7rem; text-align: left; vertical-align: top; }
th { background: #eef1f6; }
code { background: #eef1f6; padding: 0 0.25rem; border-radius: 3px; }
pre.mermaid { background: #fff; border: 1px solid #d9dee8; padding: 1rem; overflow-x: auto; }
.card { display: none; background: #fff; border: 1px solid #d9dee8; border-left: 4px solid #3b6fd4; padding: 0.5rem 1.5rem 1rem; margin: 1.5rem 0; }
.card:target { display: block; }
.kind { font-size: 0.8rem; color: #6b7487; font-weight: 400; margin-left: 0.5rem; }
.badge-stale { background: #c77d0a; color: #fff; font-size: 0.75rem; padding: 0.1rem 0.5rem; border-radius: 3px; margin-left: 0.5rem; }
.marker { display: inline-block; background: #fdecea; color: #8a1c10; padding: 0.1rem 0.5rem; border-radius: 3px; margin: 0 0.4rem 0.3rem 0; font-size: 0.85rem; }
.marker.inherited { background: #fff4e0; color: #7a4c00; }
footer { max-width: 1200px; margin: 2rem auto; padding: 0 2rem; color: #6b7487; font-size: 0.85rem; }
`;

const esc = (value) =>
  String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");

const slug = (id) => `card-${id.replace(/[^A-Za-z0-9_-]/g, "-")}`;
const link = (id) => `<a href="#${slug(id)}">${esc(id)}</a>`;

function mermaidSource(facts) {
  const ids = Object.keys(facts.nodes);
  const alias = new Map(ids.map((id, i) => [id, `n${i}`]));
  const lines = ["flowchart LR"];
  for (const kind of Object.keys(KIND_TITLE)) {
    const members = ids.filter((id) => facts.nodes[id].kind === kind);
    if (!members.length) continue;
    lines.push(`  subgraph sg_${kind}["${KIND_TITLE[kind]}"]`);
    for (const id of members) lines.push(`    ${alias.get(id)}["${facts.nodes[id].name.replace(/"/g, "'")}"]`);
    lines.push("  end");
  }
  for (const e of facts.edges) lines.push(`  ${alias.get(e.from)} --> ${alias.get(e.to)}`);
  for (const id of ids) lines.push(`  click ${alias.get(id)} "#${slug(id)}"`);
  return lines.join("\n");
}

function factValue(value) {
  if (Array.isArray(value)) return value.map((v) => (typeof v === "object" ? JSON.stringify(v) : v)).join(", ");
  return String(value);
}

function renderFacts(node) {
  const f = node.facts;
  const parts = [];
  const scalars = Object.entries(f).filter(
    ([key, value]) =>
      value !== undefined &&
      value !== "" &&
      key !== "description" &&
      !["flags", "limits", "phases", "levels", "usage"].includes(key) &&
      !(Array.isArray(value) && value.length === 0),
  );
  if (f.usage) parts.push(`<p><code>${esc(f.usage)}</code></p>`);
  if (scalars.length) {
    parts.push(
      `<table>${scalars.map(([k, v]) => `<tr><th>${esc(FACT_LABEL[k] ?? k)}</th><td>${esc(factValue(v))}</td></tr>`).join("")}</table>`,
    );
  }
  if (f.phases?.length) {
    parts.push(
      `<h3>Fazy</h3><ol>${f.phases.map((p) => `<li><strong>${esc(p.title)}</strong> — ${esc(p.detail)}${p.model ? ` (${esc(p.model)})` : ""}</li>`).join("")}</ol>`,
    );
  }
  for (const level of f.levels ?? []) {
    parts.push(`<h3>Poziomy: ${esc(level.name)}</h3><ul>${level.rows.map((r) => `<li>${esc(r)}</li>`).join("")}</ul>`);
  }
  if (f.flags?.length) {
    parts.push(
      `<h3>Flagi</h3><table><tr><th>Flaga</th><th>Opis</th></tr>${f.flags.map((x) => `<tr><td><code>${esc(x.flag)}</code></td><td>${esc(x.text)}</td></tr>`).join("")}</table>`,
    );
  }
  if (f.limits?.length) {
    parts.push(
      `<h3>Limity</h3><table>${f.limits.map((l) => `<tr><td><code>${esc(l.name)}</code></td><td>${esc(l.value)}${l.unit ? ` ${esc(l.unit)}` : ""}</td></tr>`).join("")}</table>`,
    );
  }
  return parts.join("\n");
}

function renderMarkers(node) {
  if (!node.markers.length) return "";
  const items = node.markers.map((m) =>
    m.direct
      ? `<span class="marker">${esc(m.label)} (bezpośrednio)</span>`
      : `<span class="marker inherited">${esc(m.label)} (przez ${esc(m.via)})</span>`,
  );
  return `<h3>Uprawnienia</h3><p>${items.join("")}</p>`;
}

function renderSteps(description) {
  const steps = description.steps
    .map((s) => {
      const extra = [
        s.condition ? `<em>warunek: ${esc(s.condition)}</em>` : "",
        s.agent ? `agent: ${link(`agent:${s.agent}`)}` : "",
        s.retries !== undefined ? `ponowienia: ${esc(s.retries)}` : "",
        s.limit ? `limit: ${esc(s.limit)}` : "",
      ].filter(Boolean);
      return `<li><strong>${esc(s.title)}</strong> — ${esc(s.detail)}${extra.length ? `<br>${extra.join(" · ")}` : ""}</li>`;
    })
    .join("");
  return `<p>${esc(description.summary)}</p><ol>${steps}</ol>`;
}

function renderCard(id, facts, descriptions, stale) {
  const node = facts.nodes[id];
  const callers = facts.edges.filter((e) => e.to === id).map((e) => e.from);
  const callees = facts.edges.filter((e) => e.from === id).map((e) => e.to);
  const description = descriptions[id];
  const links = (ids) => (ids.length ? ids.map(link).join(", ") : "—");
  return `<section class="card" id="${slug(id)}">
<h2>${esc(node.name)}<span class="kind">${KIND_LABEL[node.kind]} · <code>${esc(node.file)}</code></span>${stale.has(id) ? '<span class="badge-stale">nieaktualny opis</span>' : ""}</h2>
${node.facts.description ? `<p>${esc(node.facts.description)}</p>` : ""}
${renderFacts(node)}
${renderMarkers(node)}
<p><strong>Wywołują:</strong> ${links(callers)}<br><strong>Wywołuje:</strong> ${links(callees)}</p>
${description ? renderSteps(description) : "<p><em>Brak opisu kroków.</em></p>"}
</section>`;
}

export function renderPage(facts, descriptions, stale, meta) {
  const ids = Object.keys(facts.nodes);
  const index = Object.keys(KIND_TITLE)
    .map((kind) => {
      const members = ids.filter((id) => facts.nodes[id].kind === kind);
      return members.length ? `<h3>${KIND_TITLE[kind]}</h3><p>${members.map(link).join(" · ")}</p>` : "";
    })
    .join("");
  const legend = `<table><tr><th>Znacznik</th><th>Znaczenie</th></tr>${LABELS.map((l) => `<tr><td><span class="marker">${esc(l)}</span></td><td>${esc(LEGEND[l])}</td></tr>`).join("")}</table>`;
  const footer = `Wygenerowano ${esc(meta.timestamp)} · commit ${esc(meta.commit)}${meta.dirty ? "+zmiany" : ""}`;
  const mermaid = readFileSync(MERMAID, "utf8").replace(/<\/script/gi, "<\\/script");
  return `<!doctype html>
<html lang="pl">
<head>
<meta charset="utf-8">
<title>Mapa workflow · Skarbiec</title>
<style>${STYLE}</style>
</head>
<body>
<header><strong>Skarbiec</strong> · mapa workflow</header>
<main>
<h1>Mapa workflow</h1>
<p>Kliknij węzeł, aby otworzyć jego kartę.</p>
<pre class="mermaid">
${esc(mermaidSource(facts))}
</pre>
<h2>Legenda</h2>
${legend}
<h2>Węzły</h2>
${index}
${ids.map((id) => renderCard(id, facts, descriptions, stale)).join("\n")}
</main>
<footer>${footer}</footer>
<script>${mermaid}</script>
<script>mermaid.initialize({ startOnLoad: true, securityLevel: "loose" });</script>
</body>
</html>
`;
}
