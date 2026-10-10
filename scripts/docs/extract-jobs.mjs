import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { readCode, walkFiles } from "./extract-architecture.mjs";

export const ON_DEMAND = "na żądanie";

export function readJson(file) {
  return existsSync(file) ? JSON.parse(readFileSync(file, "utf8").replace(/^﻿/, "")) : null;
}

export const configValue = (json, key) => key.split(":").reduce((node, part) => node?.[part], json) ?? null;

export function readAppSettings(serviceDir) {
  return {
    base: readJson(path.join(serviceDir, "appsettings.json")),
    development: readJson(path.join(serviceDir, "appsettings.Development.json")),
  };
}

function resolveToken(token, text) {
  if (!token) return null;
  if (token.startsWith('"')) return token.slice(1, -1);
  return new RegExp(`\\b${token}\\s*=\\s*"([^"]*)"`).exec(text)?.[1] ?? null;
}

function parseJobs(service, classes) {
  const settings = readAppSettings(service.dir);
  const jobs = [];
  for (const file of walkFiles(service.dir, (n) => n.endsWith(".cs"))) {
    const text = readCode(file);
    for (const m of text.matchAll(/\bAddJob\s*<\s*(\w+)\s*>/g)) {
      const jobClass = classes.find((c) => c.name === m[1] && c.service === service.name);
      const key = /JobKey\s+Key\s*=\s*new(?:\s+JobKey)?\s*\(\s*"([^"]+)"/.exec(jobClass?.text ?? "")?.[1] ?? m[1];
      const scheduled = /\bWithCronSchedule\s*\(/.test(text);
      const lookup = scheduled ? /Configuration\s*\[\s*(\w+|"[^"]+")\s*\]\s*(?:\?\?\s*(\w+|"[^"]*"))?/.exec(text) : null;
      const configKey = resolveToken(lookup?.[1], text);
      const defaultCron = resolveToken(lookup?.[2], text);
      const appsettingsCron = configKey ? configValue(settings.base, configKey) : null;
      const developmentCron = configKey ? configValue(settings.development, configKey) : null;
      const effectiveCron = scheduled ? (appsettingsCron ?? defaultCron) : null;
      jobs.push({
        key,
        class: m[1],
        file: jobClass?.file ?? null,
        schedule: effectiveCron ?? ON_DEMAND,
        configKey,
        defaultCron,
        appsettingsCron,
        developmentCron,
      });
    }
  }
  for (const c of classes) {
    if (c.service !== service.name || !c.bases.includes("IJob") || jobs.some((j) => j.class === c.name)) continue;
    const key = /JobKey\s+Key\s*=\s*new(?:\s+JobKey)?\s*\(\s*"([^"]+)"/.exec(c.text)?.[1] ?? c.name;
    jobs.push({
      key,
      class: c.name,
      file: c.file,
      schedule: ON_DEMAND,
      configKey: null,
      defaultCron: null,
      appsettingsCron: null,
      developmentCron: null,
    });
  }
  return jobs.sort((a, b) => a.key.localeCompare(b.key));
}

function parseTriggers(service, classes) {
  return classes
    .filter((c) => c.service === service.name && /Trigger$/.test(c.name) && !c.name.startsWith("NoOp"))
    .map((c) => ({
      class: c.name,
      file: c.file,
      kind: /Startup/.test(c.name) ? "start aplikacji" : ON_DEMAND,
      implements: c.bases,
    }))
    .sort((a, b) => a.class.localeCompare(b.class));
}

export function extractJobs(services, classes) {
  const marketData = services.find((s) => s.name === "MarketData");
  if (!marketData) return { jobs: [], triggers: [] };
  return { jobs: parseJobs(marketData, classes), triggers: parseTriggers(marketData, classes) };
}
