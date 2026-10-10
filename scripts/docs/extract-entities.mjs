import { existsSync, readdirSync } from "node:fs";
import path from "node:path";
import { discoverServices, readCode, walkFiles } from "./extract-architecture.mjs";

const TENANCY_FILTER = "filtr tenancy";
const TENANCY_MISSING = "UserId bez filtra";
const MASSTRANSIT_LABEL = "outbox/inbox";

const shortName = (fullName) => fullName.replace(/<.*$/, "").split(".").at(-1);

function parseSnapshot(text, serviceName) {
  const chunks = text.split(/modelBuilder\s*\.\s*Entity\s*\(\s*"/).slice(1);
  if (chunks.length === 0) throw new Error(`model snapshot of ${serviceName} contains no entity`);
  const merged = new Map();
  for (const chunk of chunks) {
    const type = /^([^"]+)"/.exec(chunk)[1];
    merged.set(type, `${merged.get(type) ?? ""}\n${chunk}`);
  }
  return [...merged.entries()].map(([type, chunk]) => {
    const columns = [];
    for (const m of chunk.matchAll(/\bb\s*\.\s*Property<([^>]+)>\s*\(\s*"(\w+)"\s*\)([^;]*);/g)) {
      const [, clrType, propertyName, chain] = m;
      if (propertyName === "xmin") continue;
      const nullable = clrType.endsWith("?") || (clrType === "string" && !/\.IsRequired\(\)/.test(chain));
      columns.push({
        name: /\.HasColumnName\(\s*"(\w+)"/.exec(chain)?.[1] ?? propertyName,
        type: clrType.replace(/\?$/, ""),
        nullable,
      });
    }
    const keyArgs = /\bb\s*\.\s*HasKey\s*\(([^)]*)\)/.exec(chunk)?.[1] ?? "";
    const table = /\bb\s*\.\s*ToTable\s*\(\s*"(\w+)"/.exec(chunk)?.[1] ?? shortName(type);
    const foreignKeys = [];
    for (const m of chunk.matchAll(/\bb\s*\.\s*HasOne\s*\(\s*"([^"]+)"[^;]*?\.\s*HasForeignKey\s*\(([^)]*)\)/g)) {
      for (const column of [...m[2].matchAll(/"([^"]*)"/g)].map((c) => c[1]).filter((c) => c && !c.includes("."))) {
        foreignKeys.push({ column, principalType: m[1] });
      }
    }
    return { type, table, key: [...keyArgs.matchAll(/"(\w+)"/g)].map((k) => k[1]), columns, foreignKeys };
  });
}

function classDeclarations(files) {
  const found = new Map();
  for (const file of files) {
    for (const m of file.matchAll(/\bclass\s+(\w+)\s*(?:<[^>]*>)?\s*(?:\([^)]*\))?\s*(:[^{;]*)?[{;]/g)) {
      if (!/\bIUserOwned\b/.test(found.get(m[1]) ?? "")) found.set(m[1], m[2] ?? "");
    }
  }
  return found;
}

function extractService(service) {
  const snapshotDir = path.join(service.dir, "Migrations");
  const snapshotFile = existsSync(snapshotDir) ? readdirSync(snapshotDir).find((n) => n.endsWith("ModelSnapshot.cs")) : null;
  if (!snapshotFile) throw new Error(`no model snapshot found for service ${service.name} (Migrations/*ModelSnapshot.cs)`);
  const parsed = parseSnapshot(readCode(path.join(snapshotDir, snapshotFile)), service.name);

  const sources = walkFiles(service.dir, (n) => n.endsWith(".cs") && !n.endsWith("ModelSnapshot.cs")).map(readCode);
  const classes = classDeclarations(sources);
  const filtersApplied = sources.some((text) => /\bApplyUserOwnedQueryFilters\s*\(/.test(text));

  const mass = parsed.filter((e) => e.type.startsWith("MassTransit."));
  const own = parsed.filter((e) => !e.type.startsWith("MassTransit."));
  if (own.length === 0) throw new Error(`model snapshot of ${service.name} contains no entity besides MassTransit tables`);
  const tableOfType = new Map(own.map((e) => [e.type, e.table]));

  const entities = own
    .map((e) => {
      const hasUserId = e.columns.some((c) => c.name === "UserId");
      const userOwned = /\bIUserOwned\b/.test(classes.get(shortName(e.type)) ?? "");
      let tenancy = null;
      if (hasUserId) tenancy = userOwned && filtersApplied ? TENANCY_FILTER : TENANCY_MISSING;
      return {
        table: e.table,
        key: e.key,
        columns: e.columns,
        foreignKeys: e.foreignKeys
          .map((fk) => ({ column: fk.column, references: tableOfType.get(fk.principalType) ?? shortName(fk.principalType) }))
          .sort((a, b) => a.column.localeCompare(b.column)),
        tenancy,
      };
    })
    .sort((a, b) => a.table.localeCompare(b.table));

  return {
    name: service.name,
    entities,
    masstransit: mass.length ? { label: MASSTRANSIT_LABEL, tables: mass.map((e) => e.table).sort() } : null,
  };
}

export function extractEntities(root) {
  return { services: discoverServices(root).map(extractService) };
}
