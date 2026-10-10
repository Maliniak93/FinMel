import { existsSync } from "node:fs";
import path from "node:path";
import { discoverServices, readCode, walkFiles } from "./extract-architecture.mjs";

const RECORD_SPLIT_RE = /(?=\bpublic\s+(?:sealed\s+)?record\s)/;
const RECORD_RE = /^public\s+(?:sealed\s+)?record\s+(?:class\s+)?(\w+)/;
const PROPERTY_RE = /public\s+(?:required\s+)?([\w.<>,?[\] ]+?)\s+(\w+)\s*\{\s*get;/g;

function parseRecords(text) {
  const records = [];
  for (const part of text.split(RECORD_SPLIT_RE)) {
    const name = RECORD_RE.exec(part)?.[1];
    if (!name) continue;
    const properties = [...part.matchAll(PROPERTY_RE)].map((m) => ({ name: m[2], type: m[1].trim() }));
    records.push({ name, properties });
  }
  return records;
}

function typeNames(type) {
  return type.match(/\w+/g) ?? [];
}

function withNested(properties, nestedRecords) {
  return properties.map((property) => {
    const target = typeNames(property.type).map((n) => nestedRecords.get(n)).find(Boolean);
    return target ? { ...property, nested: target.properties } : property;
  });
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

const byKey = (a, b) => JSON.stringify(a).localeCompare(JSON.stringify(b));

export function extractEvents(root) {
  const eventsDir = path.join(root, "contracts", "Skarbiec.Contracts", "Events");
  const contractRecords = existsSync(eventsDir)
    ? walkFiles(eventsDir, (n) => n.endsWith(".cs")).flatMap((f) => parseRecords(readCode(f)))
    : [];
  const contractByName = new Map(contractRecords.map((r) => [r.name, r]));
  const usedAsPropertyType = new Set(
    contractRecords.flatMap((r) => r.properties.flatMap((p) => typeNames(p.type))).filter((n) => contractByName.has(n)),
  );

  const services = discoverServices(root).map((service) => ({
    name: service.name,
    dir: service.dir,
    files: walkFiles(service.dir, (n) => n.endsWith(".cs")).map((file) => ({
      relative: path.relative(service.dir, file).split(path.sep).join("/"),
      text: readCode(file),
    })),
  }));

  const published = [];
  const consumed = [];
  const definitions = [];
  const localRecords = [];
  for (const service of services) {
    for (const file of service.files) {
      for (const m of file.text.matchAll(/\.\s*Publish\s*\(\s*new\s+(\w+)/g)) {
        published.push({ message: m[1], service: service.name, file: file.relative });
      }
      for (const m of file.text.matchAll(/\bclass\s+(\w+)[^{;]*?\bIConsumer\s*<\s*(\w+)\s*>/g)) {
        consumed.push({ message: m[2], service: service.name, consumer: m[1] });
      }
      for (const m of file.text.matchAll(/\bclass\s+(\w+)[^{;]*?:\s*[\w.]*ConsumerDefinition\s*<\s*(\w+)\s*,/g)) {
        definitions.push({ service: service.name, consumer: m[2], definition: m[1] });
      }
      for (const record of parseRecords(file.text)) localRecords.push({ ...record, service: service.name });
    }
  }

  const describe = (record, kind, serviceFilter) => ({
    name: record.name,
    kind,
    properties: withNested(record.properties, contractByName),
    publishers: dedupe(
      published
        .filter((p) => p.message === record.name && (!serviceFilter || p.service === serviceFilter))
        .map(({ service, file }) => ({ service, file })),
    ).sort(byKey),
    consumers: dedupe(
      consumed
        .filter((c) => c.message === record.name && (!serviceFilter || c.service === serviceFilter))
        .map((c) => ({
          service: c.service,
          consumer: c.consumer,
          definition: definitions.find((d) => d.service === c.service && d.consumer === c.consumer)?.definition ?? null,
        })),
    ).sort(byKey),
  });

  const events = contractRecords.filter((r) => !usedAsPropertyType.has(r.name)).map((r) => describe(r, "event"));
  const internal = localRecords
    .filter(
      (r) =>
        !contractByName.has(r.name) &&
        published.some((p) => p.message === r.name && p.service === r.service) &&
        consumed.some((c) => c.message === r.name && c.service === r.service),
    )
    .map((r) => describe(r, "internal", r.service));

  const messages = [...events, ...internal].sort((a, b) => a.name.localeCompare(b.name));
  return { messages };
}
