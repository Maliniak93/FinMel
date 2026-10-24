import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, readdirSync, rmSync, statSync, utimesSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { slugOf } from "./claude-history.mjs";
import { RETENTION, fetchIssueStates, formatReport, planAged, planSessions, planTickets, runClean, unfinishedSessions } from "./clean.mjs";
import { buildState, createSandbox } from "./fake-gh-sandbox.mjs";

const SCRIPTS = path.dirname(fileURLToPath(import.meta.url));
const DAY = 24 * 60 * 60 * 1000;
const NOW = Date.UTC(2026, 9, 24, 12, 0, 0);
const MIB = 1024 * 1024;
const ORPHAN_OLD = "5b1e6a40-0000-4000-8000-000000000001";
const ORPHAN_NEW = "5b1e6a40-0000-4000-8000-000000000002";

function stamp(target, days, now) {
  const at = new Date(now - days * DAY);
  utimesSync(target, at, at);
}

function setDirs(dir, days, now) {
  for (const e of readdirSync(dir, { withFileTypes: true })) if (e.isDirectory()) setDirs(path.join(dir, e.name), days, now);
  stamp(dir, days, now);
}

function makeTree(repo, config, now = NOW) {
  const history = path.join(config, "projects", slugOf(repo));
  const put = (file, days, content = "x") => {
    mkdirSync(path.dirname(file), { recursive: true });
    writeFileSync(file, content);
    stamp(file, days, now);
    return file;
  };
  // Folder mtimes would otherwise be "now" and decide the age of an entry; the files carry it.
  const settle = () => {
    setDirs(repo, 400, now);
    setDirs(config, 400, now);
  };
  return {
    repo,
    config,
    history,
    put,
    settle,
    issue: (name, days = 1) => put(path.join(repo, "skarbiec-plan", "issues", name), days),
    run: (options = {}) => {
      settle();
      return runClean({ now, repoRoot: repo, configDir: config, fetchStates: () => new Map(), ...options });
    },
    row: (report, id) => report.rows.find((r) => r.id === id),
  };
}

function withTree(body) {
  const base = mkdtempSync(path.join(os.tmpdir(), "clean-"));
  const repo = path.join(base, "repo");
  const config = path.join(base, "claude");
  mkdirSync(repo);
  mkdirSync(config);
  try {
    return body(makeTree(repo, config));
  } finally {
    rmSync(base, { recursive: true, force: true });
  }
}

const entry = (id, days, extra = {}) => ({ id, paths: [id], files: 1, bytes: 10, newestMs: NOW - days * DAY, ...extra });

const workflowLine = (issue) =>
  JSON.stringify({ message: { content: [{ type: "tool_use", name: "Workflow", input: { scriptPath: `C:\\repo\\skarbiec-plan\\issues\\${issue}.resume.js`, args: { issue } } }] } });

test("selects only expired entries per category", () => {
  assert.deepEqual(RETENTION, { sessionDays: 30, playwrightDays: 7, ticketDays: 7, logDays: 30 });
  const ids = (list) => list.map((e) => e.id);

  const sessions = planSessions([entry("old", 31), entry("edge", 29), entry("new", 1)], { now: NOW });
  assert.deepEqual(ids(sessions.selected), ["old"]);
  assert.deepEqual(ids(sessions.kept), ["edge", "new"]);

  const playwright = planAged([entry("old", 8), entry("edge", 6), entry("new", 0)], { now: NOW, days: RETENTION.playwrightDays });
  assert.deepEqual(ids(playwright.selected), ["old"]);

  const logs = planAged([entry("old.log", 31), entry("new.log", 29)], { now: NOW, days: RETENTION.logDays });
  assert.deepEqual(ids(logs.selected), ["old.log"]);

  withTree((t) => {
    t.put(path.join(t.history, "old.jsonl"), 40);
    t.put(path.join(t.history, "mid.jsonl"), 29);
    t.put(path.join(t.history, "new.jsonl"), 5);
    t.put(path.join(t.repo, "e2e", "test-results", "old-run", "trace.zip"), 8);
    t.put(path.join(t.repo, "e2e", "test-results", "new-run", "trace.zip"), 6);
    t.put(path.join(t.repo, "e2e", "playwright-report", "index.html"), 10);
    t.put(path.join(t.repo, ".playwright-mcp", "old.yml"), 8);
    t.put(path.join(t.repo, ".playwright-mcp", "new.yml"), 1);
    t.put(path.join(t.repo, "skarbiec-plan", "autobuild-runs", "old.log"), 31);
    t.put(path.join(t.repo, "skarbiec-plan", "autobuild-runs", "new.log"), 29);
    t.put(path.join(t.repo, "skarbiec-plan", "autobuild-runs", "e2e-repairs.json"), 90);

    const report = t.run();
    assert.equal(t.row(report, "sessions").files, 1);
    assert.equal(t.row(report, "playwright").files, 3);
    assert.equal(t.row(report, "logs").files, 1);
    assert.equal(t.row(report, "tickets").files, 0);
  });
});

test("never leaves its roots", () => {
  withTree((t) => {
    const outside = [
      t.put(path.join(t.repo, "skarbiec-plan", "archive", "old.md"), 400),
      t.put(path.join(t.repo, "skarbiec-plan", "prompts", "done.md"), 400),
      t.put(path.join(t.repo, "skarbiec-plan", "przewodnik", "index.html"), 400),
      t.put(path.join(t.repo, ".git", "verify-stop-1.json"), 400),
      t.put(path.join(t.repo, "node_modules", "x", "index.js"), 400),
      t.put(path.join(t.repo, "e2e", "tests", "a.spec.ts"), 400),
      t.put(path.join(t.repo, "e2e", "old.txt"), 400),
      t.put(path.join(t.repo, "skarbiec-plan", "issues", "notes.txt"), 400),
      t.put(path.join(t.repo, "skarbiec-plan", "issues", "12.unknown.txt"), 400),
      t.put(path.join(t.repo, "skarbiec-plan", "autobuild-runs", "e2e-repairs.json"), 400),
      t.put(path.join(t.config, "projects", "other-project", "a.jsonl"), 400),
      t.put(path.join(t.config, "projects", `${slugOf(t.repo)}X`, "a.jsonl"), 400),
      t.put(path.join(t.config, "settings.json"), 400),
    ];
    const inRoots = [
      t.put(path.join(t.history, "old.jsonl"), 400),
      t.put(path.join(t.config, "projects", `${slugOf(t.repo)}-wt`, "old.jsonl"), 400),
      t.put(path.join(t.repo, ".playwright-mcp", "old.yml"), 400),
      t.put(path.join(t.repo, "skarbiec-plan", "autobuild-runs", "old.log"), 400),
      t.issue("30.md", 400),
    ];
    const states = new Map([[30, { state: "CLOSED", stateReason: "COMPLETED", closedAt: new Date(NOW - 20 * DAY).toISOString() }]]);
    const removed = [];
    t.run({
      apply: true,
      fetchStates: () => states,
      remove: (p) => {
        removed.push(p);
        rmSync(p, { recursive: true, force: true });
      },
    });
    for (const file of outside) assert.ok(existsSync(file), `${file} must stay`);
    for (const file of inRoots) assert.ok(!existsSync(file), `${file} must go`);
    assert.equal(removed.length, inRoots.length);
  });
});

test("session age is the newest mtime of the unit", () => {
  withTree((t) => {
    t.put(path.join(t.history, "live.jsonl"), 40);
    t.put(path.join(t.history, "live", "subagents", "agent-1.jsonl"), 1);
    t.put(path.join(t.history, "idle.jsonl"), 40);
    t.put(path.join(t.history, "idle", "tool-results", "r.txt"), 35);
    t.put(path.join(t.history, ORPHAN_OLD, "subagents", "a.jsonl"), 40);
    t.put(path.join(t.history, ORPHAN_NEW, "subagents", "a.jsonl"), 2);
    t.put(path.join(t.history, "memory", "MEMORY.md"), 400);
    t.put(path.join(t.config, "projects", `${slugOf(t.repo)}-wt`, "sibling.jsonl"), 40);

    const report = t.run({ apply: true });
    assert.ok(existsSync(path.join(t.history, "live.jsonl")));
    assert.ok(existsSync(path.join(t.history, "live", "subagents", "agent-1.jsonl")));
    assert.ok(!existsSync(path.join(t.history, "idle.jsonl")));
    assert.ok(!existsSync(path.join(t.history, "idle")));
    assert.ok(!existsSync(path.join(t.history, ORPHAN_OLD)));
    assert.ok(existsSync(path.join(t.history, ORPHAN_NEW)));
    assert.ok(existsSync(path.join(t.history, "memory", "MEMORY.md")), "memory/ is not a session");
    assert.equal(t.row(report, "sessions").files, 4);
  });
});

test("keeps the session of an unfinished ticket", () => {
  const holding = new Map([
    ["aaa", new Set([77])],
    ["bbb", new Set([78])],
  ]);
  const resumeNumbers = new Set([77, 78]);
  const done = { state: "CLOSED", stateReason: "COMPLETED", closedAt: new Date(NOW - 3 * DAY).toISOString() };
  assert.deepEqual([...unfinishedSessions({ holding, resumeNumbers, states: new Map([[77, { state: "OPEN" }], [78, done]]) })], ["aaa"]);
  assert.deepEqual([...unfinishedSessions({ holding, resumeNumbers, states: new Map() })].sort(), ["aaa", "bbb"]);
  assert.deepEqual([...unfinishedSessions({ holding, resumeNumbers, states: new Map([[77, done], [78, done]]) })], []);
  assert.deepEqual([...unfinishedSessions({ holding, resumeNumbers: new Set(), states: new Map() })], []);

  withTree((t) => {
    t.put(path.join(t.history, "aaa.jsonl"), 40, `${workflowLine(77)}\n`);
    t.put(path.join(t.history, "bbb.jsonl"), 40, `{"type":"user"}\n`);
    t.issue("77.resume.js", 40);
    const sessionFiles = (report) => t.row(report, "sessions").files;

    const open = new Map([[77, { state: "OPEN", stateReason: null, closedAt: null }]]);
    assert.equal(sessionFiles(t.run({ fetchStates: () => open })), 1);
    assert.equal(sessionFiles(t.run({ fetchStates: () => new Map() })), 1);

    const completed = new Map([[77, { state: "CLOSED", stateReason: "COMPLETED", closedAt: new Date(NOW - 3 * DAY).toISOString() }]]);
    assert.equal(sessionFiles(t.run({ fetchStates: () => completed })), 2);

    const notPlanned = new Map([[77, { state: "CLOSED", stateReason: "NOT_PLANNED", closedAt: new Date(NOW - 30 * DAY).toISOString() }]]);
    assert.equal(sessionFiles(t.run({ fetchStates: () => notPlanned })), 1);
  });
});

test("ticket files need a completed issue closed over 7 days ago", () => {
  const closed = (reason, days) => ({ state: "CLOSED", stateReason: reason, closedAt: new Date(NOW - days * DAY).toISOString() });
  const states = new Map([
    [1, { state: "OPEN", stateReason: null, closedAt: null }],
    [2, closed("NOT_PLANNED", 20)],
    [3, closed("COMPLETED", 3)],
    [4, closed("COMPLETED", 10)],
    [5, null],
  ]);
  const groups = [1, 2, 3, 4, 5, 6].map((n) => entry(n, 40, { files: 7 }));
  const plan = planTickets(groups, states, NOW);
  assert.deepEqual(plan.selected.map((g) => g.id), [4]);
  assert.deepEqual(plan.kept.map((g) => g.id), [3]);
  assert.deepEqual(plan.unknown.map((g) => g.id), [1, 2, 5, 6]);

  withTree((t) => {
    const kinds = ["md", "run.json", "resume.js", "manual.md", "e2e.patch", "diagnosis.md", "ci-fix.md"];
    for (const n of [1, 2, 3, 4, 5, 6]) for (const kind of kinds) t.issue(`${n}.${kind}`, 40);
    const report = t.run({ apply: true, fetchStates: () => states });

    for (const kind of kinds) assert.ok(!existsSync(path.join(t.repo, "skarbiec-plan", "issues", `4.${kind}`)), `4.${kind}`);
    for (const n of [1, 2, 3, 5, 6]) for (const kind of kinds) assert.ok(existsSync(path.join(t.repo, "skarbiec-plan", "issues", `${n}.${kind}`)), `${n}.${kind}`);
    const row = t.row(report, "tickets");
    assert.equal(row.files, 7);
    assert.equal(row.skipped, 28);
    assert.match(row.notes.join("\n"), /skipped \(stan nieznany\)/);
  });
});

test("ticket states come from batched GraphQL requests", () => {
  const calls = [];
  const numbers = Array.from({ length: 120 }, (_, i) => i + 1);
  const states = fetchIssueStates(numbers, (query) => {
    calls.push(query);
    const repository = {};
    for (const m of query.matchAll(/i(\d+): issueOrPullRequest\(number: \d+\)/g)) {
      const n = Number(m[1]);
      repository[`i${n}`] = n === 2 ? { __typename: "PullRequest" } : n === 3 ? null : { __typename: "Issue", state: "CLOSED", stateReason: "COMPLETED", closedAt: "2026-10-01T00:00:00Z" };
    }
    return { repository };
  });
  assert.equal(calls.length, 3);
  assert.deepEqual(calls.map((q) => (q.match(/issueOrPullRequest/g) ?? []).length), [50, 50, 20]);
  assert.equal(states.size, 120);
  assert.equal(states.get(2), null);
  assert.equal(states.get(3), null);
  assert.deepEqual(states.get(1), { state: "CLOSED", stateReason: "COMPLETED", closedAt: "2026-10-01T00:00:00Z" });
});

test("apply deletes, preview does not", () => {
  withTree((t) => {
    const old = t.put(path.join(t.repo, "skarbiec-plan", "autobuild-runs", "old.log"), 40, Buffer.alloc(MIB));
    const fresh = t.put(path.join(t.repo, "skarbiec-plan", "autobuild-runs", "new.log"), 1);

    const preview = t.run();
    assert.ok(existsSync(old));
    assert.equal(t.row(preview, "logs").files, 1);
    assert.equal(t.row(preview, "logs").bytes, MIB);
    assert.match(formatReport(preview), /MB to free/);
    assert.match(formatReport(preview), /1\.0/);
    assert.match(formatReport(preview), /--apply/);

    const applied = t.run({ apply: true });
    assert.ok(!existsSync(old));
    assert.ok(existsSync(fresh));
    assert.equal(t.row(applied, "logs").files, 1);
    assert.match(formatReport(applied), /MB freed/);
  });
});

test("busy files are skipped", () => {
  withTree((t) => {
    const busy = t.put(path.join(t.repo, "skarbiec-plan", "autobuild-runs", "busy.log"), 40);
    const free = t.put(path.join(t.repo, "skarbiec-plan", "autobuild-runs", "free.log"), 40);
    const report = t.run({
      apply: true,
      remove: (p) => {
        if (p === busy) throw Object.assign(new Error("resource busy"), { code: "EBUSY" });
        rmSync(p, { force: true });
      },
    });
    assert.ok(existsSync(busy));
    assert.ok(!existsSync(free));
    const row = t.row(report, "logs");
    assert.equal(row.files, 1);
    assert.equal(row.skipped, 1);
    assert.deepEqual(report.failures.map((f) => [f.path, f.error]), [[busy, "EBUSY"]]);
    assert.match(formatReport(report), /busy\.log.*EBUSY/);
  });
});

test("unknown flags exit 1 with the usage line", () => {
  const res = spawnSync(process.execPath, [path.join(SCRIPTS, "clean.mjs"), "--force"], { encoding: "utf8", windowsHide: true });
  assert.equal(res.status, 1);
  assert.match(res.stderr, /usage: clean\.mjs \[--apply\]/);
});

function sandboxTree(failIssueStates) {
  const config = mkdtempSync(path.join(os.tmpdir(), "clean-config-"));
  const before = process.env.CLAUDE_CONFIG_DIR;
  process.env.CLAUDE_CONFIG_DIR = config;
  const state = buildState({ issues: [{ number: 10, status: "Done" }, { number: 11, status: "Done" }] });
  state.issues[10].stateReason = "COMPLETED";
  state.issues[10].closedAt = new Date(Date.now() - 30 * DAY).toISOString();
  state.issues[11].stateReason = "COMPLETED";
  state.issues[11].closedAt = new Date(Date.now() - 2 * DAY).toISOString();
  state.failIssueStates = failIssueStates;
  const sandbox = createSandbox(state);
  process.env.CLAUDE_CONFIG_DIR = before ?? "";
  if (before === undefined) delete process.env.CLAUDE_CONFIG_DIR;
  const tree = makeTree(sandbox.dir, config, Date.now());
  const files = {
    ticket: tree.issue("10.md", 40),
    young: tree.issue("11.md", 40),
    log: tree.put(path.join(sandbox.dir, "skarbiec-plan", "autobuild-runs", "old.log"), 40),
    report: tree.put(path.join(sandbox.dir, "e2e", "playwright-report", "index.html"), 10),
    session: tree.put(path.join(tree.history, "old.jsonl"), 40),
  };
  tree.settle();
  return { sandbox, files, cleanup: () => {
    sandbox.cleanup();
    rmSync(config, { recursive: true, force: true });
  } };
}

test("survives an unreachable GitHub", () => {
  const { sandbox, files, cleanup } = sandboxTree(true);
  try {
    const res = sandbox.run("clean.mjs", ["--apply"]);
    assert.equal(res.code, 0, res.out);
    assert.ok(existsSync(files.ticket), "no ticket file is selected without a state");
    assert.ok(existsSync(files.young));
    assert.ok(!existsSync(files.log));
    assert.ok(!existsSync(files.report));
    assert.ok(!existsSync(files.session));
    assert.match(res.stdout, /skipped \(stan nieznany\)/);
    assert.match(res.stdout, /GitHub niedostępny/);
  } finally {
    cleanup();
  }
});

test("reachable GitHub deletes the completed ticket only", () => {
  const { sandbox, files, cleanup } = sandboxTree(false);
  try {
    const res = sandbox.run("clean.mjs", ["--apply"]);
    assert.equal(res.code, 0, res.out);
    assert.ok(!existsSync(files.ticket));
    assert.ok(existsSync(files.young));
    assert.equal(sandbox.log().filter((l) => l.kind === "issueStates").length, 1);
    assert.ok(statSync(path.join(sandbox.dir, "skarbiec-plan", "issues")).isDirectory());
  } finally {
    cleanup();
  }
});
