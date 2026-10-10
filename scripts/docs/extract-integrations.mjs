import { readCode, walkFiles } from "./extract-architecture.mjs";
import { consumersOf, matchClose, splitTop } from "./csharp.mjs";
import { configValue, readAppSettings } from "./extract-jobs.mjs";

const MAX_HOPS = 3;
const GENERIC_BASES = new Set(["IJob", "IConsumer", "BackgroundService", "IHostedService", "IDisposable", "IAsyncDisposable"]);
const EXTERNAL_URL_RE = /^https?:\/\//;

const literalOf = (expr) => /^new\s*(?:Uri)?\s*\(\s*"([^"]+)"/.exec(expr)?.[1] ?? null;

function declaredUri(text, identifier) {
  const found = new RegExp(`\\b${identifier}\\s*=\\s*(new\\s*(?:Uri)?\\s*\\(\\s*"[^"]+")`).exec(text)?.[1];
  return found ? literalOf(found) : null;
}

function optionsBase(classes, optionsType, property) {
  const cls = classes.find((c) => c.name === optionsType);
  if (!cls) return null;
  const section = /SectionName\s*=\s*"([^"]+)"/.exec(cls.text)?.[1] ?? null;
  const prop = property ?? /\bUri\s+(\w+)\s*\{/.exec(cls.text)?.[1] ?? null;
  if (!prop) return null;
  const def = new RegExp(`\\bUri\\s+${prop}\\s*\\{[^}]*\\}\\s*=\\s*(new\\s*(?:Uri)?\\s*\\(\\s*"[^"]+")`).exec(cls.text)?.[1];
  return { configKey: section ? `${section}:${prop}` : null, defaultUrl: def ? literalOf(def) : null };
}

function resolveBase(args, text, classes, settings) {
  const assigned = /\bBaseAddress\s*=\s*([^;]+)/.exec(args)?.[1].trim();
  let literal = null;
  let options = null;
  if (assigned) {
    literal = literalOf(assigned) ?? (/^\w+$/.test(assigned) ? declaredUri(text, assigned) : null);
    if (!literal && /\.Value\./.test(assigned)) {
      const type = /IOptions\s*<\s*(\w+)\s*>/.exec(assigned)?.[1];
      const prop = /\.Value\.(\w+)/.exec(assigned)?.[1];
      options = type ? optionsBase(classes, type, prop) : null;
    }
  } else {
    const type = /AddOptions\s*<\s*(\w+)\s*>/.exec(text)?.[1];
    options = type ? optionsBase(classes, type, null) : null;
  }
  if (literal) return { baseUrl: literal, origin: "stała w kodzie", configKey: null };
  if (!options) return null;
  const override = options.configKey ? configValue(settings.base, options.configKey) : null;
  if (override) return { baseUrl: override, origin: "appsettings.json", configKey: options.configKey };
  return options.defaultUrl ? { baseUrl: options.defaultUrl, origin: "domyślna wartość Options", configKey: options.configKey } : null;
}

function callerChains(classes, client, iface) {
  const chains = [];
  const extend = (cls, chain, hop) => {
    const next = hop < MAX_HOPS
      ? consumersOf(classes, cls.bases.filter((b) => !GENERIC_BASES.has(b)), cls.name).filter((c) => !chain.includes(c.name))
      : [];
    if (next.length === 0) chains.push(chain);
    for (const caller of next) extend(caller, [...chain, caller.name], hop + 1);
  };
  const start = classes.find((c) => c.name === client);
  for (const first of consumersOf(classes, [iface, client], client)) extend(first, [client, first.name], 1);
  return start ? chains : [];
}

export function extractIntegrations(services, classes, jobs) {
  const scheduleOf = new Map(jobs.map((j) => [j.class, `${j.key}: ${j.schedule}`]));
  const found = [];
  for (const service of services) {
    const settings = readAppSettings(service.dir);
    for (const file of walkFiles(service.dir, (n) => n.endsWith(".cs"))) {
      const text = readCode(file);
      for (const m of text.matchAll(/\bAddHttpClient\s*</g)) {
        const typeOpen = m.index + m[0].length - 1;
        const typeClose = matchClose(text, typeOpen);
        const types = splitTop(text.slice(typeOpen + 1, typeClose));
        const argsOpen = text.indexOf("(", typeClose);
        const args = text.slice(argsOpen + 1, matchClose(text, argsOpen));
        const base = resolveBase(args, text, classes, settings);
        if (!base || !EXTERNAL_URL_RE.test(base.baseUrl)) continue;
        const client = types.at(-1);
        const callers = callerChains(classes, client, types[0]).map((chain) => ({
          chain,
          schedule: chain.map((name) => scheduleOf.get(name)).find(Boolean) ?? null,
        }));
        found.push({ client, service: service.name, ...base, callers });
      }
    }
  }
  return found.sort((a, b) => a.client.localeCompare(b.client));
}
