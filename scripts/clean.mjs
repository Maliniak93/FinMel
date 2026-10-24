#!/usr/bin/env node
// Usage: node scripts/clean.mjs [--apply]  delete generated local files older than their age threshold (preview otherwise)
//   --apply  delete them; without it the table shows what would go

import { lstatSync, readdirSync, rmSync } from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { sessionsHoldingResume } from "./build-replay.mjs";
import { REPO_ROOT, historyDirs } from "./claude-history.mjs";
import { OWNER, REPO, graphql } from "./gh.mjs";

export const RETENTION = { sessionDays: 30, playwrightDays: 7, ticketDays: 7, logDays: 30 };

const DAY_MS = 24 * 60 * 60 * 1000;
const STATES_PER_REQUEST = 50;
const TICKET_FILE = /^(\d+)\.(?:md|run\.json|resume\.js|manual\.md|e2e\.patch|diagnosis\.md|ci-fix\.md)$/;
const SESSION_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const USAGE ="usage: clean.mjs [--apply]";

export const CATEGORIES = {
  sessions: "sesje Claude",
  playwright: "Playwright",
  tickets: "pliki zgłoszeń",
  logs: "logi autobuild",
};

const names = (dir, withTypes = false) => {
  try {
    return readdirSync(dir, { withFileTypes: withTypes });
  } catch {
    return [];
  }
};

export function measure(target) {
  let stat;
  try {
    stat = lstatSync(target);
  } catch {
    return { files: 0, bytes: 0, newestMs: 0 };
  }
  if (!stat.isDirectory()) return { files: 1, bytes: stat.size, newestMs: stat.mtimeMs };
  const total = { files: 0, bytes: 0, newestMs: stat.mtimeMs };
  for (const name of names(target)) {
    const part = measure(path.join(target, name));
    total.files += part.files;
    total.bytes += part.bytes;
    total.newestMs = Math.max(total.newestMs, part.newestMs);
  }
  return total;
}

function entryOf(extra, paths) {
  const total = { files: 0, bytes: 0, newestMs: 0 };
  for (const p of paths) {
    const part = measure(p);
    total.files += part.files;
    total.bytes += part.bytes;
    total.newestMs = Math.max(total.newestMs, part.newestMs);
  }
  return { ...extra, paths, ...total };
}

// A session unit is `<id>.jsonl` plus the `<id>/` folder beside it; a folder named by a session id is a unit too.
// Any other folder (Claude Code's `memory/`) is not a session and is never listed.
export function listSessionUnits(dirs) {
  const units = [];
  for (const dir of dirs) {
    const entries = names(dir, true);
    const transcripts = new Set(entries.filter((e) => e.isFile() && e.name.endsWith(".jsonl")).map((e) => e.name.slice(0, -".jsonl".length)));
    const byId = new Map();
    for (const e of entries) {
      const id = e.isFile() && e.name.endsWith(".jsonl") ? e.name.slice(0, -".jsonl".length) : e.isDirectory() && (SESSION_ID.test(e.name) || transcripts.has(e.name)) ? e.name : null;
      if (id) byId.set(id, [...(byId.get(id) ?? []), path.join(dir, e.name)]);
    }
    for (const [id, paths] of byId) units.push(entryOf({ id }, paths));
  }
  return units;
}

export const listTopLevel = (root) => names(root).map((name) => entryOf({ id: name }, [path.join(root, name)]));

export function listTicketFiles(issuesDir) {
  const byNumber = new Map();
  for (const e of names(issuesDir, true)) {
    const match = e.isFile() ? TICKET_FILE.exec(e.name) : null;
    if (match) byNumber.set(Number(match[1]), [...(byNumber.get(Number(match[1])) ?? []), path.join(issuesDir, e.name)]);
  }
  return [...byNumber].map(([number, paths]) => entryOf({ id: number }, paths));
}

export function listLogs(dir) {
  return names(dir, true)
    .filter((e) => e.isFile() && e.name.endsWith(".log"))
    .map((e) => entryOf({ id: e.name }, [path.join(dir, e.name)]));
}

const isOlder = (ms, now, days) => now - ms > days * DAY_MS;

export function planAged(entries, { now, days }) {
  const selected = [];
  const kept = [];
  for (const entry of entries) (isOlder(entry.newestMs, now, days) ? selected : kept).push(entry);
  return { selected, kept };
}

const isCompleted = (state) => state?.state === "CLOSED" && state.stateReason === "COMPLETED";

// Session ids that ran a `<n>.resume.js` of a ticket that is not closed-completed.
export function unfinishedSessions({ holding, resumeNumbers, states }) {
  const keep = new Set();
  for (const [session, issues] of holding) {
    if ([...issues].some((n) => resumeNumbers.has(n) && !isCompleted(states.get(n)))) keep.add(session);
  }
  return keep;
}

export function planSessions(units, { now, keep = new Set() }) {
  const selected = [];
  const kept = [];
  for (const unit of units) (isOlder(unit.newestMs, now, RETENTION.sessionDays) && !keep.has(unit.id) ? selected : kept).push(unit);
  return { selected, kept };
}

export function planTickets(groups, states, now) {
  const selected = [];
  const kept = [];
  const unknown = [];
  for (const group of groups) {
    const state = states.get(group.id);
    if (!isCompleted(state)) unknown.push(group);
    else if (isOlder(Date.parse(state.closedAt), now, RETENTION.ticketDays)) selected.push(group);
    else kept.push(group);
  }
  return { selected, kept, unknown };
}

export function fetchIssueStates(numbers, query = graphql) {
  const states = new Map();
  const name = REPO.split("/")[1];
  for (let from = 0; from < numbers.length; from += STATES_PER_REQUEST) {
    const batch = numbers.slice(from, from + STATES_PER_REQUEST);
    const fields = batch.map((n) => `i${n}: issueOrPullRequest(number: ${n}) { __typename ... on Issue { state stateReason closedAt } }`).join("\n");
    const { repository } = query(`query ($owner: String!, $name: String!) { repository(owner: $owner, name: $name) { ${fields} } }`, { owner: OWNER, name });
    for (const n of batch) {
      const node = repository?.[`i${n}`];
      states.set(n, node?.__typename === "Issue" ? { state: node.state, stateReason: node.stateReason, closedAt: node.closedAt } : null);
    }
  }
  return states;
}

const inside = (roots, target) =>
  roots.some((root) => {
    const rel = path.relative(root, target);
    return rel !== "" && !rel.startsWith("..") && !path.isAbsolute(rel);
  });

const removeTree = (target) => rmSync(target, { recursive: true, force: true });

function removeEntries(entries, { roots, remove }) {
  const deleted = [];
  const failed = [];
  for (const entry of entries) {
    try {
      for (const p of entry.paths) {
        if (!inside(roots, p)) throw Object.assign(new Error(`outside the cleaned roots: ${p}`), { code: "OUTSIDE" });
        remove(p);
      }
      deleted.push(entry);
    } catch (err) {
      failed.push({ entry, error: err.code ?? err.message });
    }
  }
  return { deleted, failed };
}

const sum = (entries, key) => entries.reduce((total, e) => total + e[key], 0);

export function runClean({ apply = false, now = Date.now(), repoRoot = REPO_ROOT, configDir, fetchStates = fetchIssueStates, remove = removeTree } = {}) {
  const issuesDir = path.join(repoRoot, "skarbiec-plan", "issues");
  const runsDir = path.join(repoRoot, "skarbiec-plan", "autobuild-runs");
  const playwrightRoots = [path.join(repoRoot, "e2e", "test-results"), path.join(repoRoot, "e2e", "playwright-report"), path.join(repoRoot, ".playwright-mcp")];
  const sessionDirs = historyDirs({ repoRoot, configDir });
  const roots = [...sessionDirs, ...playwrightRoots, issuesDir, runsDir];

  const groups = listTicketFiles(issuesDir);
  let states = new Map();
  let github = null;
  if (groups.length) {
    try {
      states = fetchStates(groups.map((g) => g.id));
    } catch (err) {
      github = String(err.message ?? err).split(/\r?\n/)[0].slice(0, 120);
    }
  }
  const tickets = planTickets(groups, states, now);

  const units = listSessionUnits(sessionDirs);
  const resumeNumbers = new Set(groups.filter((g) => g.paths.some((p) => p.endsWith(".resume.js"))).map((g) => g.id));
  let keep = new Set();
  if (resumeNumbers.size) {
    const expired = units.filter((u) => isOlder(u.newestMs, now, RETENTION.sessionDays));
    const holding = sessionsHoldingResume(expired.flatMap((u) => u.paths.filter((p) => p.endsWith(".jsonl"))));
    keep = unfinishedSessions({ holding, resumeNumbers, states });
  }
  const sessions = planSessions(units, { now, keep });

  const playwright = planAged(
    playwrightRoots.flatMap((root) => listTopLevel(root)),
    { now, days: RETENTION.playwrightDays },
  );
  const logs = planAged(listLogs(runsDir), { now, days: RETENTION.logDays });

  const plans = [
    { id: "sessions", selected: sessions.selected, unknown: [] },
    { id: "playwright", selected: playwright.selected, unknown: [] },
    { id: "tickets", selected: tickets.selected, unknown: tickets.unknown },
    { id: "logs", selected: logs.selected, unknown: [] },
  ];
  const rows = [];
  const failures = [];
  for (const plan of plans) {
    let done = plan.selected;
    let failed = [];
    if (apply) ({ deleted: done, failed } = removeEntries(plan.selected, { roots, remove }));
    for (const f of failed) failures.push({ category: CATEGORIES[plan.id], path: f.entry.paths[0], error: f.error });
    const notes = [];
    if (plan.unknown.length) notes.push(`skipped (stan nieznany): ${sum(plan.unknown, "files")} plików${github ? `, GitHub niedostępny: ${github}` : ""}`);
    rows.push({
      id: plan.id,
      category: CATEGORIES[plan.id],
      files: sum(done, "files"),
      bytes: sum(done, "bytes"),
      skipped: sum(failed.map((f) => f.entry), "files") + sum(plan.unknown, "files"),
      notes,
    });
  }
  return { apply, rows, failures, github };
}

const megabytes = (bytes) => (bytes / (1024 * 1024)).toFixed(1);

export function formatReport(report) {
  const header = ["category", "files", report.apply ? "MB freed" : "MB to free", "skipped"];
  const table = [header, ...report.rows.map((r) => [r.category, String(r.files), megabytes(r.bytes), String(r.skipped)])];
  const widths = header.map((_, i) => Math.max(...table.map((row) => row[i].length)));
  const lines = table.map((row) => row.map((cell, i) => (i === 0 ? cell.padEnd(widths[i]) : cell.padStart(widths[i]))).join("  "));
  for (const row of report.rows) for (const note of row.notes) lines.push(`${row.category}: ${note}`);
  for (const f of report.failures) lines.push(`skipped: ${f.path} (${f.error})`);
  if (!report.apply) lines.push("", "preview only - re-run with --apply to delete");
  return lines.join("\n");
}

function main(argv) {
  const unknown = argv.filter((a) => a !== "--apply");
  if (unknown.length) {
    console.error(`clean.mjs: unknown argument(s): ${unknown.join(" ")}\n${USAGE}`);
    process.exit(1);
  }
  console.log(formatReport(runClean({ apply: argv.includes("--apply") })));
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    main(process.argv.slice(2));
  } catch (err) {
    console.error(`clean.mjs: ${err.message}`);
    process.exit(1);
  }
}
