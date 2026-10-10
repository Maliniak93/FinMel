import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { adrAnchor, endpointAnchor, integrationAnchor, messageAnchor, tableAnchor } from "./anchors.mjs";
import { normalizeRoute } from "./extract-architecture.mjs";
import { extractEntities } from "./extract-entities.mjs";
import { extractEvents } from "./extract-events.mjs";
import { collectRuntime } from "./extract-joby.mjs";

const PENDING = "🕐";
const NO_FILTER = "UserId bez filtra";
const ADR_HEADING_RE = /^##\s+(ADR-\d+)\s+(\S+)\s+(.+)$/gm;

const ADR_PAGES = [
  [/\b(events?|outbox|inbox|consumers?|publish\w*)\b/i, "eventy.html#przeplyw"],
  [/\b(REST|internal|gateway)\b/i, "architektura.html#rest"],
  [/\b(jobs?|cron|schedul\w*|providers?|price|prices|ticker|external|quotes?|source)\b/i, "joby-integracje-frontend.html#integrations"],
];

function adrItems(root) {
  const file = path.join(root, "skarbiec-plan", "decisions.md");
  if (!existsSync(file)) return [];
  const items = [];
  for (const [, id, marker, title] of readFileSync(file, "utf8").matchAll(ADR_HEADING_RE)) {
    const reasons = [];
    if (marker === PENDING) reasons.push("oczekująca (🕐)");
    if (/\bexception\b/i.test(title)) reasons.push("wyjątek od reguły");
    if (/fail[ -]soft/i.test(title)) reasons.push("fail soft");
    if (reasons.length === 0) continue;
    const related = ADR_PAGES.find(([re]) => re.test(title))?.[1];
    items.push({
      kind: "adr",
      severity: "decyzja",
      title: `${id} ${title}`,
      reasons,
      links: [...(related ? [related] : []), `slabe-punkty.html#${adrAnchor(id)}`],
      anchor: adrAnchor(id),
    });
  }
  return items;
}

function ruleItems(runtime, events, entities) {
  const items = [];
  const seenEdges = new Set();
  for (const edge of runtime.architecture.restEdges.filter((e) => e.unmatched)) {
    const key = `${edge.from} ${edge.path}`;
    if (seenEdges.has(key)) continue;
    seenEdges.add(key);
    items.push({
      kind: "internal-bez-endpointu",
      severity: "reguła",
      title: `${edge.from} wywołuje ${edge.path}, ale żaden serwis nie mapuje takiego endpointu`,
      links: ["architektura.html#rest"],
    });
  }
  for (const message of events.messages) {
    if (message.consumers.length === 0) {
      items.push({
        kind: "wiadomosc-bez-konsumenta",
        severity: "reguła",
        title: `${message.name} nie ma konsumenta w kodzie serwisów`,
        links: [`eventy.html#${messageAnchor(message.name)}`],
      });
    } else if (message.publishers.length === 0) {
      items.push({
        kind: "wiadomosc-bez-publishera",
        severity: "reguła",
        title: `${message.name} jest konsumowane, ale nikt jej nie publikuje`,
        links: [`eventy.html#${messageAnchor(message.name)}`],
      });
    }
  }
  for (const integration of runtime.integrations.filter((i) => i.service !== "MarketData")) {
    items.push({
      kind: "zewnetrzny-url-poza-marketdata",
      severity: "reguła",
      title: `${integration.service}: klient ${integration.client} woła zewnętrzny adres ${integration.baseUrl} (zewnętrzne API tylko z MarketData)`,
      links: [`joby-integracje-frontend.html#${integrationAnchor(integration.client)}`],
    });
  }
  for (const service of entities.services) {
    for (const entity of service.entities.filter((e) => e.tenancy === NO_FILTER)) {
      items.push({
        kind: "tenancy-bez-filtra",
        severity: "reguła",
        title: `${service.name}: tabela ${entity.table} ma UserId, ale nie ma filtra tenancy`,
        links: [`bazy-danych.html#${tableAnchor(service.name, entity.table)}`],
      });
    }
  }
  for (const call of runtime.uiCalls.filter((c) => c.endpoints.length === 0)) {
    items.push({
      kind: "wywolanie-ui-bez-endpointu",
      severity: "reguła",
      title: `UI wywołuje ${call.function} (${call.method} ${call.url}), ale backend nie ma takiego endpointu`,
      links: ["joby-integracje-frontend.html#frontend"],
    });
  }
  const called = new Set(
    runtime.uiCalls.flatMap((c) => c.endpoints.map((e) => `${e.verb} ${normalizeRoute(e.path)}`)),
  );
  for (const service of runtime.architecture.services) {
    const endpoints = [...service.slices.flatMap((s) => s.endpoints), ...service.inlineEndpoints];
    for (const endpoint of endpoints.filter((e) => e.path.startsWith("/api/"))) {
      if (called.has(`${endpoint.verb} ${normalizeRoute(endpoint.path)}`)) continue;
      items.push({
        kind: "endpoint-bez-wywolania-z-ui",
        severity: "informacyjnie",
        title: `${service.name}: ${endpoint.verb} ${endpoint.path} nie jest wywoływany przez żadną trasę UI`,
        links: [`architektura.html#${endpointAnchor(endpoint.verb, endpoint.path)}`],
      });
    }
  }
  return items;
}

export function extractWeakPoints(root) {
  const runtime = collectRuntime(root);
  const items = [...adrItems(root), ...ruleItems(runtime, extractEvents(root), extractEntities(root))];
  const seen = new Set();
  return {
    items: items.filter((item) => {
      const key = JSON.stringify(item);
      if (seen.has(key)) return false;
      seen.add(key);
      return true;
    }),
  };
}
