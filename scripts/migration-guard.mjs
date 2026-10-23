#!/usr/bin/env node
// Usage: node scripts/migration-guard.mjs [--body-file <f>] [--base <ref>]  fail on a migration that destroys protected data or rewrites history without the ticket's consent
//   --body-file f  the ticket body holding ## Utrata danych / ## Przeniesienie danych (default: an empty body)
//   --base ref     the ref the branch is compared with (default master, then origin/master)

import { spawnSync } from "node:child_process";
import { existsSync, readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const PROTECTED_FILE = path.join(path.dirname(fileURLToPath(import.meta.url)), "protected-data.json");
const MIGRATION_RE = /^services\/([^/]+)\/Skarbiec\.[^/]+\/Migrations\/[^/]+\.cs$/;
const SERVICES = ["Identity", "Portfolio", "MarketData", "Reporting"];
const KEY_RE = new RegExp(`\\b(?:${SERVICES.join("|")})\\.[A-Za-z_]\\w*(?:\\.[A-Za-z_]\\w*)?`, "g");
const PROOF_RE = /proof:\s*`?([A-Za-z_][\w.]*)`?/i;
const LOSS = "Utrata danych";
const MOVE = "Przeniesienie danych";

const slashes = (p) => p.replace(/\\/g, "/");

export function isMigrationFile(file) {
  const rel = slashes(file);
  return MIGRATION_RE.test(rel) && !rel.endsWith("ModelSnapshot.cs");
}

const isMigrationSource = (file) => isMigrationFile(file) && !slashes(file).endsWith(".Designer.cs");

export function serviceOf(file) {
  return MIGRATION_RE.exec(slashes(file))?.[1] ?? null;
}

function loadConfig() {
  return JSON.parse(readFileSync(PROTECTED_FILE, "utf8"));
}

export function isProtected(service, table, config = loadConfig()) {
  const entry = config[service];
  if (!entry || (entry.derived ?? []).includes(table)) return false;
  return entry.protected === "*" || (entry.protected ?? []).includes(table);
}

function skipLiteral(src, i) {
  const c = src[i];
  if (c === "/" && src[i + 1] === "/") {
    const end = src.indexOf("\n", i);
    return end === -1 ? src.length : end;
  }
  if (c === "/" && src[i + 1] === "*") {
    const end = src.indexOf("*/", i + 2);
    return end === -1 ? src.length : end + 2;
  }
  if (c === "'") {
    let j = i + 1;
    while (j < src.length && src[j] !== "'") j += src[j] === "\\" ? 2 : 1;
    return j + 1;
  }
  if (c === '"' || ((c === "@" || c === "$") && (src[i + 1] === '"' || ((src[i + 1] === "@" || src[i + 1] === "$") && src[i + 2] === '"')))) {
    let j = i;
    let verbatim = false;
    while (src[j] !== '"') {
      if (src[j] === "@") verbatim = true;
      j++;
    }
    if (src.startsWith('"""', j)) {
      const end = src.indexOf('"""', j + 3);
      return end === -1 ? src.length : end + 3;
    }
    j++;
    while (j < src.length) {
      if (src[j] === '"') {
        if (verbatim && src[j + 1] === '"') j += 2;
        else return j + 1;
      } else j += !verbatim && src[j] === "\\" ? 2 : 1;
    }
    return src.length;
  }
  return -1;
}

function matchClose(src, openIdx, open, close) {
  let depth = 0;
  for (let i = openIdx; i < src.length; ) {
    const skipped = skipLiteral(src, i);
    if (skipped !== -1) {
      i = skipped;
      continue;
    }
    if (src[i] === open) depth++;
    else if (src[i] === close && --depth === 0) return i;
    i++;
  }
  return src.length;
}

function upBody(source) {
  const sig = /\bvoid\s+Up\s*\(\s*MigrationBuilder\s+(\w+)\s*\)/.exec(source);
  if (!sig) return null;
  const open = source.indexOf("{", sig.index + sig[0].length);
  if (open === -1) return null;
  return { builder: sig[1], text: source.slice(open + 1, matchClose(source, open, "{", "}")) };
}

function calls(body, builder) {
  const out = [];
  const re = new RegExp(`\\b${builder}\\s*\\.\\s*(\\w+)\\s*(?:<([^>(]*)>)?\\s*\\(`, "g");
  let m;
  while ((m = re.exec(body))) {
    const open = m.index + m[0].length - 1;
    const close = matchClose(body, open, "(", ")");
    out.push({ op: m[1], generic: m[2]?.trim() ?? null, args: body.slice(open + 1, close) });
    re.lastIndex = close;
  }
  return out;
}

const textArg = (args, name) => new RegExp(`(?<!\\w)${name}\\s*:\\s*"([^"]*)"`).exec(args)?.[1] ?? null;
const numArg = (args, name) => {
  const m = new RegExp(`(?<!\\w)${name}\\s*:\\s*(\\d+)`).exec(args);
  return m ? Number(m[1]) : null;
};
const clr = (t) => t?.replace(/\s+/g, "").replace(/^System\./, "").replace(/\?$/, "") ?? null;

function alterNarrows(call) {
  const oldClr = /(?<!\w)oldClrType\s*:\s*typeof\(\s*([^)]+?)\s*\)/.exec(call.args)?.[1] ?? null;
  if (oldClr && call.generic && clr(oldClr) !== clr(call.generic)) return true;
  const maxLength = numArg(call.args, "maxLength");
  const oldMaxLength = numArg(call.args, "oldMaxLength");
  if (maxLength !== null && (oldMaxLength === null || maxLength < oldMaxLength)) return true;
  const precision = numArg(call.args, "precision");
  const oldPrecision = numArg(call.args, "oldPrecision");
  return precision !== null && oldPrecision !== null && precision < oldPrecision;
}

const IDENT = String.raw`(?:\w+\.)?(\w+)`;
function sqlTargets(args) {
  const sql = args.replace(/["\\@$]/g, "");
  const targets = [];
  const add = (re, withColumn = false) => {
    for (const m of sql.matchAll(re)) targets.push(withColumn ? { table: m[1], column: m[2] } : { table: m[1] });
  };
  add(new RegExp(String.raw`\bDELETE\s+FROM\s+(?:ONLY\s+)?${IDENT}`, "gi"));
  add(new RegExp(String.raw`\bTRUNCATE\s+(?:TABLE\s+)?(?:ONLY\s+)?${IDENT}`, "gi"));
  add(new RegExp(String.raw`\bDROP\s+TABLE\s+(?:IF\s+EXISTS\s+)?${IDENT}`, "gi"));
  add(new RegExp(String.raw`\bALTER\s+TABLE\s+(?:IF\s+EXISTS\s+)?(?:ONLY\s+)?${IDENT}[^;]*?\bDROP\s+COLUMN\s+(?:IF\s+EXISTS\s+)?(\w+)`, "gi"), true);
  return targets;
}

export function findDestructive(source, service, config = loadConfig()) {
  const up = upBody(source);
  if (!up) return [];
  const found = new Map();
  const flag = (table, column, kind) => {
    if (!table || !isProtected(service, table, config)) return;
    const key = column ? `${service}.${table}.${column}` : `${service}.${table}`;
    if (!found.has(key)) found.set(key, { key, kind, service, table, ...(column ? { column } : {}) });
  };
  for (const call of calls(up.text, up.builder)) {
    if (call.op === "DropTable") flag(textArg(call.args, "name"), null, "DropTable");
    else if (call.op === "DropColumn") flag(textArg(call.args, "table"), textArg(call.args, "name"), "DropColumn");
    else if (call.op === "AlterColumn" && alterNarrows(call)) flag(textArg(call.args, "table"), textArg(call.args, "name"), "AlterColumn");
    else if (call.op === "Sql") {
      for (const t of sqlTargets(call.args)) flag(t.table, t.column ?? null, "Sql");
    }
  }
  return [...found.values()];
}

export function parseNameStatus(text) {
  return text
    .split(/\r?\n/)
    .filter(Boolean)
    .map((line) => {
      const [status, first, second] = line.split("\t");
      return second ? { status, oldPath: slashes(first), path: slashes(second) } : { status, path: slashes(first) };
    });
}

export function historyViolations(entries) {
  return entries.filter((e) => "MDRT".includes(e.status[0]) && (isMigrationFile(e.path) || (e.oldPath && isMigrationFile(e.oldPath))));
}

function sectionLines(body, name) {
  const lines = (body ?? "").replace(/\r\n/g, "\n").split("\n");
  const start = lines.findIndex((l) => l.trim() === `## ${name}`);
  if (start === -1) return null;
  const end = lines.findIndex((l, i) => i > start && /^## /.test(l));
  return lines.slice(start + 1, end === -1 ? undefined : end);
}

function bullets(lines) {
  const entries = [];
  for (const line of lines) {
    const head = /^\s*[-*]\s+(.*)$/.exec(line);
    if (head) entries.push(head[1]);
    else if (entries.length && /^\s+\S/.test(line)) entries[entries.length - 1] += ` ${line.trim()}`;
  }
  return entries;
}

export function parseConsent(body) {
  const lossLines = sectionLines(body, LOSS);
  const moveLines = sectionLines(body, MOVE);
  const loss = { present: lossLines !== null, zgoda: null, keys: new Set() };
  const move = { present: moveLines !== null, entries: [] };
  if (lossLines) {
    loss.zgoda = lossLines.map((l) => /^\s*(?:[-*]\s+)?(?:\*\*)?Zgoda:(?:\*\*)?\s*(\S.*)$/i.exec(l)?.[1]).find(Boolean) ?? null;
    for (const entry of bullets(lossLines)) {
      if (/^(?:\*\*)?Zgoda:/i.test(entry)) continue;
      for (const key of entry.match(KEY_RE) ?? []) loss.keys.add(key);
    }
  }
  if (moveLines) {
    for (const entry of bullets(moveLines)) {
      const proof = PROOF_RE.exec(entry)?.[1] ?? null;
      move.entries.push({ text: entry, proof, keys: entry.replace(PROOF_RE, "").match(KEY_RE) ?? [] });
    }
  }
  return { loss, move };
}

export function consentProblems(body) {
  const { loss, move } = parseConsent(body);
  const problems = [];
  if (loss.present) {
    if (!loss.zgoda) problems.push(`\`## ${LOSS}\` has no non-empty \`Zgoda:\` line`);
    if (!loss.keys.size) problems.push(`\`## ${LOSS}\` names no \`Service.Table[.Column]\` entry`);
  }
  if (move.present) {
    if (!move.entries.length) problems.push(`\`## ${MOVE}\` has no \`- Service.Table[.Column] … proof: Class.Method\` entry`);
    for (const e of move.entries) {
      if (!e.proof) problems.push(`\`## ${MOVE}\` entry without \`proof: <Class.Method>\`: ${e.text}`);
      else if (!e.keys.length) problems.push(`\`## ${MOVE}\` entry names no \`Service.Table[.Column]\`: ${e.text}`);
    }
  }
  return problems;
}

function testFiles(root) {
  const out = [];
  const walk = (dir) => {
    for (const d of readdirSync(dir, { withFileTypes: true })) {
      if (d.isDirectory()) {
        if (!["bin", "obj", "node_modules"].includes(d.name)) walk(path.join(dir, d.name));
      } else if (d.name.endsWith(".cs") && slashes(path.relative(root, path.join(dir, d.name))).includes("Tests")) out.push(path.join(dir, d.name));
    }
  };
  if (existsSync(path.join(root, "services"))) walk(path.join(root, "services"));
  return out;
}

export function testExists(proof, root = REPO_ROOT) {
  const parts = proof.split(".").filter(Boolean);
  if (parts.length < 2) return false;
  const [cls, method] = parts.slice(-2);
  const classRe = new RegExp(String.raw`\bclass\s+${cls}\b`);
  const methodRe = new RegExp(String.raw`\b${method}\s*\(`);
  return testFiles(root).some((f) => {
    const text = readFileSync(f, "utf8");
    return classRe.test(text) && methodRe.test(text);
  });
}

export function checkChange({ files, body = "", readSource = (p) => readFileSync(path.join(REPO_ROOT, p), "utf8"), proofExists = testExists, config = loadConfig() }) {
  const history = historyViolations(files);
  const findings = [];
  for (const e of files) {
    if (e.status[0] !== "A" || !isMigrationSource(e.path)) continue;
    for (const f of findDestructive(readSource(e.path), serviceOf(e.path), config)) {
      if (!findings.some((x) => x.key === f.key)) findings.push({ ...f, file: e.path });
    }
  }
  const consent = parseConsent(body);
  const missing = [];
  for (const f of findings) {
    if (consent.loss.zgoda && consent.loss.keys.has(f.key)) continue;
    const move = consent.move.entries.find((e) => e.keys.includes(f.key));
    if (move?.proof && proofExists(move.proof)) continue;
    const reason = move
      ? move.proof
        ? `proof ${move.proof} names no test under services/**/*Tests*`
        : `its \`## ${MOVE}\` entry has no \`proof: <Class.Method>\``
      : consent.loss.keys.has(f.key)
        ? `its \`## ${LOSS}\` entry needs a non-empty \`Zgoda:\` line`
        : `add it to \`## ${LOSS}\` (with a \`Zgoda:\` line) or to \`## ${MOVE}\` (with \`proof: <Class.Method>\`)`;
    missing.push({ key: f.key, file: f.file, kind: f.kind, reason });
  }
  return { ok: history.length === 0 && missing.length === 0, findings, history, missing };
}

export function formatResult(result) {
  const lines = [];
  for (const h of result.history) {
    lines.push(`migration history is append-only: ${h.status} ${h.oldPath ? `${h.oldPath} -> ` : ""}${h.path} (add a new migration instead)`);
  }
  for (const m of result.missing) lines.push(`${m.key} (${m.kind} in ${path.basename(m.file)}): ${m.reason}`);
  return lines;
}

function git(args) {
  const res = spawnSync("git", args, { cwd: REPO_ROOT, encoding: "utf8", windowsHide: true, maxBuffer: 256 * 1024 * 1024 });
  return res.status === 0 ? res.stdout : null;
}

export function changedEntries(base = "master") {
  for (const ref of [base, `origin/${base}`]) {
    const mergeBase = git(["merge-base", ref, "HEAD"])?.trim();
    if (!mergeBase) continue;
    const diff = git(["diff", "--name-status", "-M", mergeBase]);
    const untracked = git(["ls-files", "--others", "--exclude-standard"]);
    if (diff === null || untracked === null) continue;
    return [...parseNameStatus(diff), ...untracked.split(/\r?\n/).filter(Boolean).map((p) => ({ status: "A", path: slashes(p) }))];
  }
  return null;
}

export function guardBranch({ body, base = "master" } = {}) {
  const files = changedEntries(base);
  if (!files) return { ok: false, findings: [], history: [], missing: [], error: `no ${base} or origin/${base} ref to compare the migrations with` };
  return checkChange({ files, body });
}

function main() {
  const argv = process.argv.slice(2);
  let bodyFile = null;
  let base = "master";
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === "--body-file") bodyFile = argv[++i];
    else if (argv[i] === "--base") base = argv[++i];
    else {
      console.error(`unknown argument '${argv[i]}'`);
      process.exit(1);
    }
  }
  const body = bodyFile ? readFileSync(path.resolve(bodyFile), "utf8") : "";
  const result = guardBranch({ body, base });
  const lines = result.error ? [result.error] : formatResult(result);
  for (const l of lines) console.log(l);
  console.log(result.ok ? `migration guard: ok (${result.findings.length} destructive finding(s), all consented)` : "migration guard: FAILED");
  process.exit(result.ok ? 0 : 2);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main();
