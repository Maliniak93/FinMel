import { existsSync, readdirSync, readFileSync } from "node:fs";
import path from "node:path";

const SKIP_DIRS = new Set(["bin", "obj", "node_modules"]);
const VERBS = { Get: "GET", Post: "POST", Put: "PUT", Delete: "DELETE", Patch: "PATCH" };

const MAP_RE =
  /(?:\bvar\s+(\w+)\s*=\s*)?(\w+)\s*\.\s*(MapGroup|MapInternalGroup)\s*\(\s*"([^"]*)"\s*\)|(\w+)\s*\.\s*Map(Get|Post|Put|Delete|Patch)\s*\(\s*"([^"]*)"/g;
const CHAINED_MAP_RE = /^\s*\.\s*Map(Get|Post|Put|Delete|Patch)\s*\(\s*"([^"]*)"/;

export function walkFiles(dir, accept) {
  const found = [];
  if (!existsSync(dir)) return found;
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (!SKIP_DIRS.has(entry.name)) found.push(...walkFiles(full, accept));
    } else if (accept(entry.name)) {
      found.push(full);
    }
  }
  return found;
}

export function readCode(file) {
  return readFileSync(file, "utf8")
    .replace(/"(?:[^"\\\n]|\\.)*"|'(?:[^'\\\n]|\\.)'|\/\/[^\n]*|\/\*[\s\S]*?\*\//g, (m) => (m[0] === "/" ? " " : m));
}

function joinPath(base, sub) {
  const tail = sub.trim();
  if (tail === "" || tail === "/") return base;
  return base + (tail.startsWith("/") ? "" : "/") + tail;
}

function parseEndpoints(text) {
  const endpoints = [];
  const groups = new Map();
  MAP_RE.lastIndex = 0;
  let match;
  while ((match = MAP_RE.exec(text))) {
    const [, variable, receiver, groupKind, groupPath, mapReceiver, verb, route] = match;
    if (groupKind) {
      const base =
        groupKind === "MapInternalGroup"
          ? `/internal/${groupPath.replace(/^\/+|\/+$/g, "")}`
          : joinPath(groups.get(receiver) ?? "", groupPath);
      const chained = CHAINED_MAP_RE.exec(text.slice(MAP_RE.lastIndex));
      if (chained) {
        endpoints.push({ verb: VERBS[chained[1]], path: joinPath(base, chained[2]) });
        MAP_RE.lastIndex += chained[0].length;
      } else if (variable) {
        groups.set(variable, base);
      }
    } else {
      endpoints.push({ verb: VERBS[verb], path: joinPath(groups.get(mapReceiver) ?? "", route) });
    }
  }
  return endpoints;
}

function sortEndpoints(endpoints) {
  return [...endpoints].sort((a, b) => a.path.localeCompare(b.path) || a.verb.localeCompare(b.verb));
}

export function discoverServices(root) {
  const servicesDir = path.join(root, "services");
  if (!existsSync(servicesDir)) throw new Error(`services folder not found under ${root}`);
  return readdirSync(servicesDir, { withFileTypes: true })
    .filter((d) => d.isDirectory())
    .map((d) => ({ name: d.name, dir: path.join(servicesDir, d.name, `Skarbiec.${d.name}`) }))
    .filter((s) => existsSync(path.join(s.dir, "Program.cs")))
    .sort((a, b) => a.name.localeCompare(b.name));
}

function extractSlices(service) {
  const featuresDir = path.join(service.dir, "Features");
  const byFolder = new Map();
  for (const file of walkFiles(featuresDir, (name) => name.endsWith("Endpoint.cs"))) {
    const dir = path.dirname(file);
    byFolder.set(dir, [...(byFolder.get(dir) ?? []), file]);
  }
  return [...byFolder.entries()]
    .map(([dir, files]) => {
      const folder = path.relative(featuresDir, dir).split(path.sep).join("/");
      return {
        name: path.basename(dir),
        folder,
        endpoints: sortEndpoints(files.flatMap((f) => parseEndpoints(readCode(f)))),
      };
    })
    .sort((a, b) => a.folder.localeCompare(b.folder));
}

export function normalizeRoute(route) {
  return route.split("?")[0].replace(/\{[^}]*\}?/g, "{}").replace(/\/+$/, "");
}

function internalLiterals(text) {
  const found = [];
  for (const match of text.matchAll(/"(\/internal\/[^"]*)"/g)) found.push(match[1]);
  return found;
}

function dedupe(items) {
  const seen = new Set();
  return items.filter((item) => {
    const key = JSON.stringify(item);
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

function extractRestEdges(services, endpointOwners) {
  const edges = [];
  for (const service of services) {
    for (const file of service.files) {
      for (const literal of internalLiterals(readCode(file))) {
        const normalized = normalizeRoute(literal);
        const owner = endpointOwners.find((e) => normalizeRoute(e.path) === normalized);
        edges.push({
          from: service.name,
          to: owner?.service ?? null,
          path: owner?.path ?? literal.split("?")[0],
          unmatched: !owner,
        });
      }
    }
  }
  return dedupe(edges).sort((a, b) => a.from.localeCompare(b.from) || a.path.localeCompare(b.path));
}

function extractEventEdges(services) {
  const published = new Map();
  const consumed = new Map();
  const add = (map, event, service) => map.set(event, new Set([...(map.get(event) ?? []), service]));
  for (const service of services) {
    for (const file of service.files) {
      const text = readCode(file);
      for (const m of text.matchAll(/\.\s*Publish\s*\(\s*new\s+(\w+)/g)) add(published, m[1], service.name);
      for (const m of text.matchAll(/\bIConsumer\s*<\s*(\w+)\s*>/g)) add(consumed, m[1], service.name);
    }
  }
  const edges = [];
  for (const [event, publishers] of published) {
    for (const from of publishers) for (const to of consumed.get(event) ?? []) edges.push({ event, from, to });
  }
  return edges.sort((a, b) => a.event.localeCompare(b.event) || a.from.localeCompare(b.from) || a.to.localeCompare(b.to));
}

function extractGatewayRoutes(root) {
  const file = path.join(root, "gateway", "Skarbiec.Gateway", "appsettings.json");
  if (!existsSync(file)) throw new Error(`gateway config not found: ${file}`);
  const routes = JSON.parse(readFileSync(file, "utf8").replace(/^﻿/, ""))?.ReverseProxy?.Routes;
  if (!routes || Object.keys(routes).length === 0) {
    throw new Error(`no ReverseProxy routes found in ${path.relative(root, file)}`);
  }
  return Object.entries(routes)
    .map(([id, route]) => ({
      id,
      path: route.Match?.Path ?? null,
      cluster: route.ClusterId ?? null,
      authorizationPolicy: route.AuthorizationPolicy ?? null,
      rateLimiterPolicy: route.RateLimiterPolicy ?? null,
    }))
    .sort((a, b) => a.id.localeCompare(b.id));
}

export function extractArchitecture(root) {
  const discovered = discoverServices(root);
  if (discovered.length === 0) throw new Error(`no service with a Program.cs found under ${path.join(root, "services")}`);

  const services = discovered.map((service) => {
    const slices = extractSlices(service);
    const inlineEndpoints = sortEndpoints(parseEndpoints(readCode(path.join(service.dir, "Program.cs"))));
    const endpointCount = slices.reduce((sum, s) => sum + s.endpoints.length, 0) + inlineEndpoints.length;
    if (slices.length === 0 || endpointCount === 0) {
      throw new Error(`no endpoint found for service ${service.name}: no *Endpoint.cs under ${service.name} Features/ maps a route`);
    }
    return { name: service.name, dir: service.dir, slices, inlineEndpoints, files: walkFiles(service.dir, (n) => n.endsWith(".cs")) };
  });

  const endpointOwners = services.flatMap((s) =>
    [...s.slices.flatMap((slice) => slice.endpoints), ...s.inlineEndpoints].map((e) => ({ service: s.name, path: e.path })),
  );

  return {
    services: services.map(({ name, slices, inlineEndpoints }) => ({ name, slices, inlineEndpoints })),
    restEdges: extractRestEdges(services, endpointOwners),
    eventEdges: extractEventEdges(services),
    gatewayRoutes: extractGatewayRoutes(root),
  };
}
