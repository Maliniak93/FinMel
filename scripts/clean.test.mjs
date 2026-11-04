import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, readdirSync, rmSync, utimesSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { slugOf } from "./claude-history.mjs";
import { RETENTION, formatReport, planAged, planSessions, runClean } from "./clean.mjs";

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
  const tmp = path.join(path.dirname(repo), "tmp");
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
    if (existsSync(tmp)) setDirs(tmp, 400, now);
  };
  return {
    repo,
    config,
    tmp,
    history,
    put,
    settle,
    issue: (name, days = 1) => put(path.join(repo, "skarbiec-plan", "issues", name), days),
    run: (options = {}) => {
      settle();
      return runClean({ now, repoRoot: repo, configDir: config, tmpRoot: tmp, ...options });
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
  mkdirSync(path.join(base, "tmp"));
  try {
    return body(makeTree(repo, config));
  } finally {
    rmSync(base, { recursive: true, force: true });
  }
}

const entry = (id, days, extra = {}) => ({ id, paths: [id], files: 1, bytes: 10, newestMs: NOW - days * DAY, ...extra });

test("selects only expired entries per category", () => {
  assert.deepEqual(RETENTION, { sessionDays: 30, playwrightDays: 7, ticketDays: 14, logDays: 30, verifyDays: 7, openapiDays: 7, tempDays: 3 });
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
      t.put(path.join(t.repo, ".git", "verify-cache.json"), 400),
      t.put(path.join(t.repo, "web", "package.json"), 400),
      t.put(path.join(t.tmp, "skarbiec-autobuild.stop"), 400),
      t.put(path.join(t.tmp, "other-dir", "a.txt"), 400),
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
      t.put(path.join(t.repo, ".git", "verify-stop-1.json"), 400),
      t.put(path.join(t.repo, "web", "openapi-ts-error-1.log"), 400),
      t.put(path.join(t.tmp, "gh-sandbox-1", "a.txt"), 400),
    ];
    const removed = [];
    t.run({
      apply: true,
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

test("sessions are removed by age alone", () => {
  withTree((t) => {
    t.put(path.join(t.history, "aaa.jsonl"), 40, '{"input":{"scriptPath":"skarbiec-plan/issues/77.resume.js"}}\n');
    t.put(path.join(t.history, "bbb.jsonl"), 29);
    t.issue("77.resume.js", 1);
    const report = t.run({ apply: true });
    assert.ok(!existsSync(path.join(t.history, "aaa.jsonl")));
    assert.ok(existsSync(path.join(t.history, "bbb.jsonl")));
    assert.equal(t.row(report, "sessions").files, 1);
  });
});

test("ticket files are selected by their own age", () => {
  withTree((t) => {
    const old = t.issue("612.run.json", 15);
    const young = t.issue("612.md", 13);
    const report = t.run({ apply: true });
    assert.ok(!existsSync(old));
    assert.ok(existsSync(young));
    assert.equal(t.row(report, "tickets").files, 1);
  });
});

test("ticket cleaning keeps unknown names and future files", () => {
  withTree((t) => {
    const kept = [t.issue("612.manual.md.bak", 40), t.issue("notes.txt", 40), t.issue("613.md", -5)];
    const report = t.run({ apply: true });
    for (const file of kept) assert.ok(existsSync(file), file);
    assert.equal(t.row(report, "tickets").files, 0);
  });
});

test("verify run files are swept after 7 days", () => {
  withTree((t) => {
    const gone = ["verify-result.json", "verify-result-2.json", "verify-result.json.log", "verify-before.json", "verify-stop-1.json"].map((n) => t.put(path.join(t.repo, ".git", n), 8));
    const kept = ["verify-cache.json", "e2e-result.json", "verify.pid", "verify-result.json.tmp"].map((n) => t.put(path.join(t.repo, ".git", n), 8));
    const young = t.put(path.join(t.repo, ".git", "verify-result-3.json"), 6);
    const report = t.run({ apply: true });
    for (const file of gone) assert.ok(!existsSync(file), file);
    for (const file of [...kept, young]) assert.ok(existsSync(file), file);
    assert.equal(t.row(report, "verify").files, 5);
  });
});

test("openapi error logs are swept after 7 days", () => {
  withTree((t) => {
    const log = t.put(path.join(t.repo, "web", "openapi-ts-error-1.log"), 8);
    const pkg = t.put(path.join(t.repo, "web", "package.json"), 400);
    const report = t.run({ apply: true });
    assert.ok(!existsSync(log));
    assert.ok(existsSync(pkg));
    assert.equal(t.row(report, "openapi").files, 1);
  });
});

test("temp leftovers are swept after 3 days", () => {
  withTree((t) => {
    const gone = [
      t.put(path.join(t.tmp, "skarbiec-compose-x", "a.txt"), 4),
      t.put(path.join(t.tmp, "skarbiec-compose-x", "sub", "b.txt"), 5),
      t.put(path.join(t.tmp, "gh-sandbox-1-aux"), 4),
      t.put(path.join(t.tmp, "skarbiec-autobuild-5-comment.md"), 4),
      t.put(path.join(t.tmp, "verify-cache-a"), 4),
    ];
    const kept = [t.put(path.join(t.tmp, "skarbiec-autobuild.stop"), 40), t.put(path.join(t.tmp, "other-dir", "a.txt"), 40), t.put(path.join(t.tmp, "skarbiec-autobuild-6-skip.md"), 1)];
    const report = t.run({ apply: true });
    for (const file of gone) assert.ok(!existsSync(file), file);
    for (const file of kept) assert.ok(existsSync(file), file);
    assert.ok(existsSync(t.tmp));
    assert.equal(t.row(report, "temp").files, 5);
  });
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

test("preview lists the new categories", () => {
  withTree((t) => {
    t.put(path.join(t.repo, ".git", "verify-result.json"), 8);
    const text = formatReport(t.run());
    for (const label of ["wyniki verify", "logi openapi", "pliki tymczasowe"]) assert.match(text, new RegExp(label));
    assert.ok(existsSync(path.join(t.repo, ".git", "verify-result.json")));
  });
});
