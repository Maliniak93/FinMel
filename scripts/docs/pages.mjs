import { createHash } from "node:crypto";
import { endpointAnchor, integrationAnchor, jobAnchor, messageAnchor, tableAnchor } from "./anchors.mjs";
import { extractArchitecture } from "./extract-architecture.mjs";
import { extractEntities } from "./extract-entities.mjs";
import { extractEvents } from "./extract-events.mjs";
import { extractJoby } from "./extract-joby.mjs";
import { extractWeakPoints } from "./extract-weak-points.mjs";

const STAMP_RE = /<!--\s*facts:(\S+?)\s*-->/;

const escapeHtml = (value) =>
  String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");

export function hashFacts(facts) {
  return createHash("sha256").update(JSON.stringify(facts)).digest("hex");
}

export function proseStamp(prose) {
  return STAMP_RE.exec(prose ?? "")?.[1] ?? null;
}

function table(headers, rows) {
  const head = headers.map((h) => `<th>${escapeHtml(h)}</th>`).join("");
  const body = rows.map((cells) => `<tr>${cells.map((c) => `<td>${c}</td>`).join("")}</tr>`).join("\n");
  return `<table>\n<thead><tr>${head}</tr></thead>\n<tbody>\n${body}\n</tbody>\n</table>`;
}

const code = (value) => `<code>${escapeHtml(value)}</code>`;

const anchorSpan = (id) => `<span id="${escapeHtml(id)}"></span>`;

const endpointCell = (e) => `${anchorSpan(endpointAnchor(e.verb, e.path))}${escapeHtml(e.verb)} ${code(e.path)}`;

function clusterService(cluster, services) {
  const key = String(cluster ?? "").replace(/-cluster$/, "").toLowerCase();
  return services.find((s) => s.name.toLowerCase() === key)?.name ?? null;
}

function architectureDiagram(facts) {
  const lines = ["flowchart LR", "  Gateway((Gateway))"];
  for (const service of facts.services) lines.push(`  ${service.name}[${service.name}]`);
  const gatewayTargets = new Set(facts.gatewayRoutes.map((r) => clusterService(r.cluster, facts.services)).filter(Boolean));
  for (const name of [...gatewayTargets].sort()) lines.push(`  Gateway ==> ${name}`);
  for (const edge of facts.restEdges.filter((e) => e.to)) lines.push(`  ${edge.from} -->|"${edge.path}"| ${edge.to}`);
  for (const edge of facts.eventEdges) lines.push(`  ${edge.from} -.->|"${edge.event}"| ${edge.to}`);
  return lines.join("\n");
}

function renderArchitektura(facts) {
  const services = facts.services
    .map((service) => {
      const rows = service.slices.map((slice) => [
        code(slice.folder),
        slice.endpoints.map(endpointCell).join("<br>"),
      ]);
      for (const e of service.inlineEndpoints) rows.push([`<em>Program.cs</em>`, endpointCell(e)]);
      return `<h3>${escapeHtml(service.name)}</h3>\n${table(["Slice", "Endpointy"], rows)}`;
    })
    .join("\n");

  const rest = facts.restEdges.length
    ? table(
        ["Wywołuje", "Endpoint", "Obsługuje"],
        facts.restEdges.map((e) => [
          escapeHtml(e.from),
          code(e.path),
          e.unmatched ? `<strong>brak dopasowanego endpointu</strong>` : escapeHtml(e.to),
        ]),
      )
    : "<p>Brak wywołań REST między serwisami.</p>";

  const events = facts.eventEdges.length
    ? table(
        ["Zdarzenie", "Publikuje", "Konsumuje"],
        facts.eventEdges.map((e) => [code(e.event), escapeHtml(e.from), escapeHtml(e.to)]),
      )
    : "<p>Brak zdarzeń między serwisami.</p>";

  const gateway = table(
    ["Trasa", "Ścieżka", "Klaster", "Autoryzacja", "Limit zapytań"],
    facts.gatewayRoutes.map((r) => [
      escapeHtml(r.id),
      code(r.path),
      escapeHtml(r.cluster),
      escapeHtml(r.authorizationPolicy),
      escapeHtml(r.rateLimiterPolicy),
    ]),
  );

  return [
    "<h2>Połączenia</h2>",
    "<p>Linia ciągła gruba: Gateway kieruje do serwisu. Linia ciągła cienka: REST <code>/internal</code>. Linia przerywana: zdarzenie RabbitMQ.</p>",
    `<pre class="mermaid">\n${architectureDiagram(facts).replace(/&/g, "&amp;").replace(/</g, "&lt;")}\n</pre>`,
    "<h2>Serwisy, slice'y i endpointy</h2>",
    services,
    '<h2 id="rest">REST między serwisami</h2>',
    rest,
    "<h2>Zdarzenia</h2>",
    events,
    "<h2>Trasy Gateway</h2>",
    gateway,
  ].join("\n");
}

function renderIndex(facts) {
  const items = facts.pages.map((p) => `<li><a href="${escapeHtml(p.slug)}.html">${escapeHtml(p.title)}</a></li>`).join("\n");
  return `<h2>Strony przewodnika</h2>\n<ul>\n${items}\n</ul>`;
}

const mermaidBlock = (diagram) =>
  `<pre class="mermaid">\n${diagram.replace(/&/g, "&amp;").replace(/</g, "&lt;")}\n</pre>`;

const nodeId = (value) => String(value).replace(/\W/g, "_");

function eventsDiagram(facts) {
  const lines = ["flowchart LR"];
  for (const message of facts.messages) {
    const event = `${nodeId(message.name)}(["${message.name}"])`;
    lines.push(`  ${event}`);
    for (const p of message.publishers) lines.push(`  ${nodeId(p.service)}[${p.service}] --> ${nodeId(message.name)}`);
    for (const c of message.consumers) lines.push(`  ${nodeId(message.name)} --> ${nodeId(c.service)}[${c.service}]`);
  }
  return [...new Set(lines)].join("\n");
}

function renderEventy(facts) {
  const sections = facts.messages
    .map((message) => {
      const properties = message.properties.map((p) => {
        const nested = p.nested ? ` &rarr; ${p.nested.map((n) => `${code(n.name)}: ${escapeHtml(n.type)}`).join(", ")}` : "";
        return [code(p.name), escapeHtml(p.type) + nested];
      });
      const publishers = message.publishers.length
        ? message.publishers.map((p) => `${escapeHtml(p.service)} ${code(p.file)}`).join("<br>")
        : "<em>brak</em>";
      const consumers = message.consumers.length
        ? message.consumers
            .map((c) => `${escapeHtml(c.service)} ${code(c.consumer)}${c.definition ? ` (${code(c.definition)})` : ""}`)
            .join("<br>")
        : "<em>brak</em>";
      const kind = message.kind === "internal" ? "wiadomość wewnętrzna serwisu" : "zdarzenie z kontraktów";
      return [
        `<h3 id="${escapeHtml(messageAnchor(message.name))}">${escapeHtml(message.name)}</h3>`,
        `<p>${kind}. Publikuje: ${publishers}. Konsumuje: ${consumers}.</p>`,
        table(["Pole", "Typ"], properties),
      ].join("\n");
    })
    .join("\n");
  return ['<h2 id="przeplyw">Przepływ wiadomości</h2>', mermaidBlock(eventsDiagram(facts)), "<h2>Wiadomości</h2>", sections].join("\n");
}

const erType = (type) => type.replace(/\[\]/g, "Array").replace(/\W/g, "");

function erDiagram(service) {
  const lines = ["erDiagram"];
  for (const entity of service.entities) {
    lines.push(`  ${entity.table} {`);
    for (const column of entity.columns) {
      const marks = [entity.key.includes(column.name) ? "PK" : null, entity.foreignKeys.some((f) => f.column === column.name) ? "FK" : null]
        .filter(Boolean)
        .join(",");
      lines.push(`    ${erType(column.type)} ${column.name}${marks ? ` ${marks}` : ""}`);
    }
    lines.push("  }");
  }
  const tables = new Set(service.entities.map((e) => e.table));
  for (const entity of service.entities) {
    for (const fk of entity.foreignKeys.filter((f) => tables.has(f.references))) {
      lines.push(`  ${fk.references} ||--o{ ${entity.table} : "${fk.column}"`);
    }
  }
  return lines.join("\n");
}

function renderBazyDanych(facts) {
  return facts.services
    .map((service) => {
      const rows = service.entities.map((e) => [
        anchorSpan(tableAnchor(service.name, e.table)) + code(e.table),
        e.key.map(code).join(", "),
        e.foreignKeys.map((f) => `${code(f.column)} &rarr; ${code(f.references)}`).join("<br>"),
        e.tenancy ? (e.tenancy === "UserId bez filtra" ? `<strong>${escapeHtml(e.tenancy)}</strong>` : escapeHtml(e.tenancy)) : "",
      ]);
      const mass = service.masstransit
        ? `<p>MassTransit: ${escapeHtml(service.masstransit.label)} (${service.masstransit.tables.length} tabele).</p>`
        : "";
      return [
        `<h2>${escapeHtml(service.name)}</h2>`,
        mermaidBlock(erDiagram(service)),
        table(["Tabela", "Klucz", "Klucze obce", "Tenancy"], rows),
        mass,
      ].join("\n");
    })
    .join("\n");
}

const SEQUENCE_DIAGRAMS_REQUIRED = 4;

function validateEventyProse(prose) {
  const found = prose.match(/sequenceDiagram/g)?.length ?? 0;
  return found < SEQUENCE_DIAGRAMS_REQUIRED
    ? `prose fragment holds ${found} sequenceDiagram blocks, at least ${SEQUENCE_DIAGRAMS_REQUIRED} required`
    : null;
}

const chainCell = (callers) =>
  callers.length
    ? callers
        .map((c) => `${c.chain.map(code).join(" &rarr; ")}${c.schedule ? ` (${escapeHtml(c.schedule)})` : ""}`)
        .join("<br>")
    : "<em>brak</em>";

function routeCalls(route) {
  if (route.redirectTo !== undefined) return `przekierowanie na ${code(route.redirectTo)}`;
  if (route.calls.length === 0) return "<em>brak wywołań API</em>";
  return route.calls
    .map((call) => {
      const targets = call.endpoints.length
        ? call.endpoints
            .map((e) => `<a href="${escapeHtml(e.link)}">${escapeHtml(e.service)} ${code(e.path)}</a>`)
            .join(", ")
        : "<strong>brak endpointu w backendzie</strong>";
      return `${escapeHtml(call.method)} ${code(call.url)} &rarr; ${targets}`;
    })
    .join("<br>");
}

function renderJoby(facts) {
  const jobs = facts.jobs.length
    ? table(
        ["Klucz", "Klasa", "Harmonogram (efektywny)", "Development", "Domyślny w kodzie", "Klucz konfiguracji"],
        facts.jobs.map((j) => [
          anchorSpan(jobAnchor(j.key)) + code(j.key),
          code(j.class),
          code(j.schedule),
          j.developmentCron ? code(j.developmentCron) : "",
          j.defaultCron ? code(j.defaultCron) : "",
          j.configKey ? code(j.configKey) : "",
        ]),
      )
    : "<p>Brak jobów Quartz.</p>";
  const triggers = facts.triggers.length
    ? table(
        ["Wyzwalacz", "Kiedy", "Implementuje"],
        facts.triggers.map((t) => [code(t.class), escapeHtml(t.kind), t.implements.map(code).join(", ")]),
      )
    : "<p>Brak wyzwalaczy innych niż cron.</p>";
  const integrations = facts.integrations.length
    ? table(
        ["Klient", "Serwis", "Bazowy URL", "Skąd adres", "Wywołujący (klient → źródło → job)"],
        facts.integrations.map((i) => [
          anchorSpan(integrationAnchor(i.client)) + code(i.client),
          escapeHtml(i.service),
          code(i.baseUrl),
          escapeHtml(i.origin) + (i.configKey ? ` ${code(i.configKey)}` : ""),
          chainCell(i.callers),
        ]),
      )
    : "<p>Brak zewnętrznych klientów HTTP.</p>";
  const routes = facts.routes.length
    ? table(
        ["Trasa", "Komponent", "Wywołania API → endpoint"],
        facts.routes.map((r) => [code(r.path), r.component ? code(r.component) : "", routeCalls(r)]),
      )
    : "<p>Brak tras Angulara.</p>";
  return [
    '<h2 id="jobs">Joby Quartz</h2>',
    jobs,
    "<h2>Wyzwalacze inne niż cron</h2>",
    triggers,
    '<h2 id="integrations">Integracje zewnętrzne</h2>',
    integrations,
    '<h2 id="frontend">Frontend: trasy i wywołania API</h2>',
    routes,
  ].join("\n");
}

function renderSlabePunkty(facts) {
  const rows = facts.items.map((item) => [
    (item.anchor ? anchorSpan(item.anchor) : "") + escapeHtml(item.severity),
    escapeHtml(item.title) + (item.reasons ? ` (${escapeHtml(item.reasons.join(", "))})` : ""),
    item.links.map((l) => `<a href="${escapeHtml(l)}">${escapeHtml(l.replace(/\.html#.*/, ""))}</a>`).join(", "),
  ]);
  return [
    '<h2 id="wykryte">Wykryte automatycznie</h2>',
    "<p>Informacja z kodu i ADR-ów, nie gotowa specyfikacja.</p>",
    rows.length ? table(["Rodzaj", "Opis", "Strony"], rows) : "<p>Nic nie wykryto.</p>",
  ].join("\n");
}

function validateSlabePunktyProse(prose) {
  return /<section[^>]*class="[^"]*\bocena-claude\b/.test(prose)
    ? null
    : 'prose fragment lacks <section class="ocena-claude"> with the assessment';
}

export const PAGES = [
  {
    slug: "index",
    title: "Przegląd",
    extract: () => ({ pages: PAGES.map(({ slug, title }) => ({ slug, title })) }),
    render: renderIndex,
  },
  { slug: "architektura", title: "Architektura", extract: extractArchitecture, render: renderArchitektura },
  { slug: "eventy", title: "Eventy i przepływy", extract: extractEvents, render: renderEventy, validateProse: validateEventyProse },
  { slug: "bazy-danych", title: "Bazy danych", extract: extractEntities, render: renderBazyDanych },
  { slug: "joby-integracje-frontend", title: "Joby, integracje, frontend", extract: extractJoby, render: renderJoby },
  {
    slug: "slabe-punkty",
    title: "Słabe punkty",
    extract: extractWeakPoints,
    render: renderSlabePunkty,
    validateProse: validateSlabePunktyProse,
  },
];

const STYLE = `
body { font-family: system-ui, sans-serif; margin: 0; color: #1d2433; background: #f7f8fa; }
nav { background: #1d2433; padding: 0.75rem 2rem; display: flex; gap: 1.5rem; }
nav a { color: #cfd6e4; text-decoration: none; }
nav a.current { color: #fff; font-weight: 600; }
main { max-width: 1100px; margin: 2rem auto; padding: 0 2rem; }
table { border-collapse: collapse; width: 100%; margin-bottom: 1.5rem; background: #fff; }
th, td { border: 1px solid #d9dee8; padding: 0.4rem 0.7rem; text-align: left; vertical-align: top; }
th { background: #eef1f6; }
code { background: #eef1f6; padding: 0 0.25rem; border-radius: 3px; }
pre.mermaid { background: #fff; border: 1px solid #d9dee8; padding: 1rem; overflow-x: auto; }
section.ocena-claude { background: #fff7e6; border-left: 4px solid #c77d0a; padding: 0.5rem 1rem; margin: 1rem 0; }
footer { max-width: 1100px; margin: 2rem auto; padding: 0 2rem; color: #6b7487; font-size: 0.85rem; }
`;

export function renderPage(page, facts, prose, meta) {
  const nav = PAGES.map(
    (p) => `<a href="${p.slug}.html"${p.slug === page.slug ? ' class="current"' : ""}>${escapeHtml(p.title)}</a>`,
  ).join("\n");
  const footer = `Wygenerowano ${escapeHtml(meta.timestamp)} · commit ${escapeHtml(meta.commit)}${meta.dirty ? "+zmiany" : ""}`;
  return `<!doctype html>
<html lang="pl">
<head>
<meta charset="utf-8">
<title>${escapeHtml(page.title)} · Skarbiec</title>
<style>${STYLE}</style>
</head>
<body>
<nav>
${nav}
</nav>
<main>
<h1>${escapeHtml(page.title)}</h1>
<section class="prose">
${prose}
</section>
${page.render(facts)}
</main>
<footer>${footer}</footer>
<script src="mermaid.min.js"></script>
<script>mermaid.initialize({ startOnLoad: true, securityLevel: "strict" });</script>
</body>
</html>
`;
}
