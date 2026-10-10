import { existsSync, readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import { endpointAnchor } from "./anchors.mjs";
import { matchClose } from "./csharp.mjs";
import { normalizeRoute, walkFiles } from "./extract-architecture.mjs";

const SOURCE_FILE = (name) => name.endsWith(".ts") && !name.endsWith(".spec.ts");
const toPosix = (p) => p.split(path.sep).join("/");

const stripComments = (text) => text.replace(/\/\*[\s\S]*?\*\/|(^|[^:])\/\/[^\n]*/g, "$1");

function sdkFunctions(apiDir) {
  const functions = new Map();
  if (!existsSync(apiDir)) return functions;
  for (const entry of readdirSync(apiDir, { withFileTypes: true }).filter((d) => d.isDirectory())) {
    const file = path.join(apiDir, entry.name, "sdk.gen.ts");
    if (!existsSync(file)) continue;
    for (const chunk of readFileSync(file, "utf8").split(/\nexport const /).slice(1)) {
      const name = /^(\w+)/.exec(chunk)?.[1];
      const call = /\.(get|post|put|delete|patch)\b[^(]*\(\s*\{\s*url:\s*'([^']+)'/.exec(chunk);
      if (name && call) functions.set(name, { service: entry.name, method: call[1].toUpperCase(), url: call[2] });
    }
  }
  return functions;
}

function calledFunctions(text, sdk) {
  const code = stripComments(text);
  const local = new Map();
  for (const m of code.matchAll(/import\s*(type\s*)?\{([\s\S]*?)\}\s*from\s*'([^']*)'/g)) {
    if (m[1] || !/\/api(\/|$)/.test(m[3])) continue;
    for (const member of m[2].split(",").map((s) => s.trim()).filter(Boolean)) {
      if (member.startsWith("type ")) continue;
      const [original, alias] = member.split(/\s+as\s+/);
      if (sdk.has(original)) local.set(alias ?? original, original);
    }
  }
  const body = code.replace(/^import\s[\s\S]*?from\s*'[^']*';?/gm, "");
  return [...local].filter(([alias]) => new RegExp(`\\b${alias}\\s*\\(`).test(body)).map(([, original]) => original);
}

function routeObjects(arrayText) {
  const objects = [];
  for (let i = 0; i < arrayText.length; i++) {
    if (arrayText[i] !== "{") continue;
    const close = matchClose(arrayText, i);
    objects.push(arrayText.slice(i, close + 1));
    i = close;
  }
  return objects;
}

const joinRoute = (parent, segment) => {
  const parts = [...parent.split("/"), ...segment.split("/")].filter(Boolean);
  return `/${parts.join("/")}`;
};

function parseRoutes(arrayText, parent, found) {
  for (const object of routeObjects(arrayText)) {
    let own = object;
    let children = null;
    const childrenAt = /\bchildren\s*:\s*\[/.exec(object);
    if (childrenAt) {
      const open = childrenAt.index + childrenAt[0].length - 1;
      const close = matchClose(object, open);
      children = object.slice(open + 1, close);
      own = object.slice(0, childrenAt.index) + object.slice(close + 1);
    }
    const segment = /\bpath\s*:\s*'([^']*)'/.exec(own)?.[1] ?? "";
    const full = joinRoute(parent, segment);
    const redirect = /\bredirectTo\s*:\s*'([^']*)'/.exec(own)?.[1];
    const lazy = /\bloadComponent\s*:[\s\S]*?import\(\s*'([^']+)'\s*\)/.exec(own)?.[1];
    if (redirect !== undefined) found.push({ path: full, redirectTo: joinRoute(parent, redirect) });
    else if (lazy) found.push({ path: full, importPath: lazy });
    if (children) parseRoutes(children, full, found);
  }
}

export function extractFrontend(root, architecture) {
  const appDir = path.join(root, "web", "src", "app");
  const routesFile = path.join(appDir, "app.routes.ts");
  if (!existsSync(routesFile)) return { routes: [], uiCalls: [] };

  const sdk = sdkFunctions(path.join(appDir, "api"));
  const endpoints = architecture.services.flatMap((s) =>
    [...s.slices.flatMap((slice) => slice.endpoints), ...s.inlineEndpoints].map((e) => ({ service: s.name, ...e })),
  );
  const endpointsFor = (fn) =>
    endpoints
      .filter((e) => e.verb === fn.method && normalizeRoute(e.path) === normalizeRoute(fn.url))
      .map((e) => ({ service: e.service, verb: e.verb, path: e.path, link: `architektura.html#${endpointAnchor(e.verb, e.path)}` }));
  const describeCall = (name, files) => {
    const fn = sdk.get(name);
    return { function: name, method: fn.method, url: fn.url, files, endpoints: endpointsFor(fn) };
  };
  const callsIn = (files) => {
    const byName = new Map();
    for (const file of files) {
      for (const name of calledFunctions(readFileSync(file, "utf8"), sdk)) {
        byName.set(name, [...(byName.get(name) ?? []), toPosix(path.relative(appDir, file))]);
      }
    }
    return [...byName].map(([name, list]) => describeCall(name, list)).sort((a, b) => a.function.localeCompare(b.function));
  };

  const parsed = [];
  const routesText = readFileSync(routesFile, "utf8");
  const arrayOpen = routesText.indexOf("[", routesText.indexOf("routes"));
  parseRoutes(routesText.slice(arrayOpen + 1, matchClose(routesText, arrayOpen)), "", parsed);

  const routes = parsed.map((route) => {
    if (route.redirectTo !== undefined) return { path: route.path, redirectTo: route.redirectTo };
    const componentFile = path.resolve(appDir, `${route.importPath}.ts`);
    const folder = path.dirname(componentFile);
    const files = existsSync(folder) ? walkFiles(folder, SOURCE_FILE) : [];
    return {
      path: route.path,
      component: toPosix(path.relative(appDir, componentFile)),
      files: files.map((f) => toPosix(path.relative(appDir, f))).sort(),
      calls: callsIn(files),
    };
  });

  const uiFiles = walkFiles(appDir, SOURCE_FILE).filter((f) => !toPosix(path.relative(appDir, f)).startsWith("api/"));
  return { routes: routes.sort((a, b) => a.path.localeCompare(b.path)), uiCalls: callsIn(uiFiles) };
}
