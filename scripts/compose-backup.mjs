// Usage: imported by compose.mjs (up, reset, backup, backups, restore) - dump, rotation, listing and restore of the four skarbiec-local databases through an injected command runner

import { existsSync, mkdirSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from "node:fs";
import path from "node:path";

export const POSTGRES_VOLUME = "skarbiec-local_postgres-data";
export const KEEP = 5;
export const SERVICES = [
  { name: "identity", dir: "Identity", database: "identity_db", user: "identity_user" },
  { name: "portfolio", dir: "Portfolio", database: "portfolio_db", user: "portfolio_user" },
  { name: "marketdata", dir: "MarketData", database: "marketdata_db", user: "marketdata_user" },
  { name: "reporting", dir: "Reporting", database: "reporting_db", user: "reporting_user" },
];

const NAME_PATTERN = /^\d{8}-\d{6}(-\d+)?$/;
const MIGRATION_FILE = /^(\d{14}_\w+)\.cs$/;
const LATEST_MIGRATION_SQL = 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1';

const fail = (message) => {
  throw new Error(message);
};

const stamp = (date) => date.toISOString().replace(/\.\d+Z$/, "").replace(/[-:]/g, "").replace("T", "-");

function postgresRunning(ctx) {
  const ps = ctx.compose(["ps", "--status", "running", "-q", "postgres"]);
  return ps.ok && ps.out !== "";
}

function startPostgres(ctx) {
  if (postgresRunning(ctx)) return;
  ctx.log("starting postgres ...");
  if (!ctx.compose(["up", "-d", "--wait", "postgres"], { inherit: true }).ok) fail("could not start postgres");
}

function newBackupDir(ctx) {
  mkdirSync(ctx.root, { recursive: true });
  const base = stamp(ctx.now());
  let name = base;
  for (let n = 2; existsSync(path.join(ctx.root, name)); n++) name = `${base}-${n}`;
  const dir = path.join(ctx.root, name);
  mkdirSync(dir);
  return { name, dir };
}

function dumpDatabase(ctx, service, dir) {
  const file = path.join(dir, `${service.database}.dump`);
  const res = ctx.compose(["exec", "-T", "postgres", "pg_dump", "-Fc", "-U", "postgres", "-d", service.database], { stdoutFile: file });
  if (!res.ok) fail(`backup failed: pg_dump of ${service.database} failed: ${res.tail || res.error}`);
  if (!existsSync(file) || statSync(file).size === 0) fail(`backup failed: pg_dump of ${service.database} wrote nothing`);
  return path.basename(file);
}

function latestMigration(ctx, service) {
  const res = ctx.compose(["exec", "-T", "postgres", "psql", "-U", "postgres", "-d", service.database, "-tAc", LATEST_MIGRATION_SQL]);
  return res.ok && res.out ? res.out.split(/\r?\n/)[0].trim() : null;
}

export function listBackups(ctx) {
  if (!existsSync(ctx.root)) return [];
  return readdirSync(ctx.root)
    .filter((name) => NAME_PATTERN.test(name) && existsSync(path.join(ctx.root, name, "manifest.json")))
    .sort()
    .reverse()
    .map((name) => {
      const dir = path.join(ctx.root, name);
      return { name, dir, manifest: JSON.parse(readFileSync(path.join(dir, "manifest.json"), "utf8")) };
    });
}

export function rotate(ctx, keep = KEEP) {
  if (!existsSync(ctx.root)) return [];
  const names = readdirSync(ctx.root).filter((name) => NAME_PATTERN.test(name)).sort().reverse();
  const stale = names.slice(keep);
  for (const name of stale) rmSync(path.join(ctx.root, name), { recursive: true, force: true });
  return stale;
}

export function backupAll(ctx, { reason = null, start = false } = {}) {
  if (start) startPostgres(ctx);
  else if (!postgresRunning(ctx)) fail("postgres is not running; start the stack with: node scripts/compose.mjs up");

  const { name, dir } = newBackupDir(ctx);
  const databases = {};
  try {
    for (const service of SERVICES) {
      const file = dumpDatabase(ctx, service, dir);
      databases[service.name] = { database: service.database, file, latestMigration: latestMigration(ctx, service) };
    }
  } catch (err) {
    rmSync(dir, { recursive: true, force: true });
    throw err;
  }
  const manifest = { name, createdAt: ctx.now().toISOString(), reason, databases };
  writeFileSync(path.join(dir, "manifest.json"), `${JSON.stringify(manifest, null, 2)}\n`);
  rotate(ctx);
  ctx.log(`backup written to ${dir}`);
  return { name, dir, manifest };
}

export function backupIfData(ctx, reason) {
  if (!ctx.volumeExists()) {
    ctx.log("no postgres volume yet; nothing to back up");
    return null;
  }
  return backupAll(ctx, { reason, start: true });
}

export function upWithBackup(ctx) {
  backupIfData(ctx, "before up");
  return ctx.compose(["up", "-d", "--wait"], { inherit: true }).ok ? 0 : 2;
}

export async function resetWithBackup(ctx, { yes = false } = {}) {
  backupIfData(ctx, "before reset");
  if (!yes) {
    if (!ctx.interactive) fail("reset deletes the databases and queues; run it in a terminal or pass --yes");
    const answer = await ctx.ask('The databases and queues will be deleted. Type "reset" to continue: ');
    if (answer.trim() !== "reset") fail("reset cancelled");
  }
  return ctx.compose(["down", "--volumes"], { inherit: true }).ok ? 0 : 2;
}

export function formatBackups(backups) {
  if (!backups.length) return ["no backups"];
  return backups.map(({ name, manifest }) => {
    const migrations = Object.entries(manifest.databases)
      .map(([service, entry]) => `${service}=${entry.latestMigration ?? "none"}`)
      .join(" ");
    return `${name}  ${manifest.createdAt}  ${manifest.reason ?? "-"}  ${migrations}`;
  });
}

export function checkCompatibility(ctx, manifest) {
  const errors = [];
  const warnings = [];
  for (const service of SERVICES) {
    const latest = manifest.databases[service.name]?.latestMigration;
    if (!latest) continue;
    const image = `skarbiec-local/${service.name}:latest`;
    const revision = ctx.revisionOf(image);
    if (!revision) {
      errors.push(`${image} is missing or has no revision label, so ${service.name} migrations cannot be compared`);
      continue;
    }
    const tree = ctx.run("git", ["ls-tree", "-r", "--name-only", revision, "--", `services/${service.dir}/Skarbiec.${service.dir}/Migrations`]);
    if (!tree.ok) {
      errors.push(`revision ${revision.slice(0, 7)} of ${image} is unknown to git`);
      continue;
    }
    const known = tree.out
      .split(/\r?\n/)
      .map((file) => MIGRATION_FILE.exec(path.posix.basename(file))?.[1])
      .filter(Boolean)
      .sort();
    if (!known.includes(latest)) {
      errors.push(`the dump holds ${service.name} migration ${latest}, unknown to the images (revision ${revision.slice(0, 7)})`);
    } else if (latest < known[known.length - 1]) {
      warnings.push(`the ${service.name} dump is older (${latest}); the service applies ${known[known.length - 1]} at the next start`);
    }
  }
  return { errors, warnings };
}

function resolveBackup(ctx, name) {
  const backups = listBackups(ctx);
  if (!backups.length) fail(`no backups in ${ctx.root}`);
  if (!name || name === "latest") return backups[0];
  return backups.find((backup) => backup.name === name) ?? fail(`no backup named ${name} in ${ctx.root}`);
}

function psql(ctx, database, sql) {
  const res = ctx.compose(["exec", "-T", "postgres", "psql", "-U", "postgres", "-d", "postgres", "-v", "ON_ERROR_STOP=1", "-c", sql]);
  if (!res.ok) fail(`restore of ${database} failed: ${sql}: ${res.tail || res.error}`);
}

function restoreDatabase(ctx, service, backup) {
  const entry = backup.manifest.databases[service.name];
  const file = path.join(backup.dir, entry.file);
  if (!existsSync(file)) fail(`restore of ${service.database} failed: ${file} is missing`);
  const db = `"${service.database}"`;
  psql(ctx, service.database, `SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '${service.database}' AND pid <> pg_backend_pid()`);
  psql(ctx, service.database, `DROP DATABASE IF EXISTS ${db}`);
  psql(ctx, service.database, `CREATE DATABASE ${db} OWNER "${service.user}"`);
  psql(ctx, service.database, `REVOKE CONNECT ON DATABASE ${db} FROM PUBLIC`);
  psql(ctx, service.database, `GRANT CONNECT, TEMP ON DATABASE ${db} TO "${service.user}"`);
  const res = ctx.compose(["exec", "-T", "postgres", "pg_restore", "-U", "postgres", "-d", service.database, "--exit-on-error"], { stdinFile: file });
  if (!res.ok) fail(`restore of ${service.database} failed: pg_restore: ${res.tail || res.error}`);
  ctx.log(`restored ${service.database}`);
}

export function restoreBackup(ctx, { name, force = false } = {}) {
  const backup = resolveBackup(ctx, name);
  const { errors, warnings } = checkCompatibility(ctx, backup.manifest);
  for (const warning of warnings) ctx.log(`warning: ${warning}`);
  if (errors.length) {
    if (!force) fail(`restore of ${backup.name} refused:\n- ${errors.join("\n- ")}\nrun with --force to restore anyway`);
    for (const error of errors) ctx.log(`forced past: ${error}`);
  }

  const listed = ctx.compose(["config", "--services"]);
  if (!listed.ok) fail(`could not list the compose services: ${listed.tail || listed.error}`);
  const others = listed.out.split(/\s+/).filter((service) => service && service !== "postgres");
  if (others.length && !ctx.compose(["stop", ...others], { inherit: true }).ok) fail("could not stop the services");
  startPostgres(ctx);

  for (const service of SERVICES) {
    if (backup.manifest.databases[service.name]) restoreDatabase(ctx, service, backup);
  }
  ctx.log(`restored ${backup.name}; postgres is running, the rest is stopped. Start it with: node scripts/compose.mjs up`);
  return backup;
}
