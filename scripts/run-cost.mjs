#!/usr/bin/env node
// Usage: node scripts/run-cost.mjs [--since YYYY-MM-DD] [--issue <n>] [--runs <N>] [--json]
//   --since  only runs started on or after this date (default: 30 days before the newest run)
//   --issue  only runs for this spec issue
//   --runs   how many of the newest runs to list one by one (default 15)
//   --json   one JSON object instead of the tables

import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const CONFIG_DIR = process.env.CLAUDE_CONFIG_DIR || path.join(os.homedir(), ".claude");
// Claude Code names the history folder after the path with every non-alphanumeric character as "-".
const HISTORY = path.join(CONFIG_DIR, "projects", REPO_ROOT.replace(/[^A-Za-z0-9]/g, "-"));

function parseArgs(argv) {
  const args = { since: null, issue: null, runs: 15, json: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === "--json") args.json = true;
    else if (a === "--since") args.since = argv[++i];
    else if (a === "--issue") args.issue = Number(String(argv[++i]).replace(/^#/, ""));
    else if (a === "--runs") args.runs = Number(argv[++i]);
    else throw new Error(`unknown argument '${a}' — usage: run-cost.mjs [--since YYYY-MM-DD] [--issue n] [--runs N] [--json]`);
  }
  return args;
}

const readJson = (file) => {
  try {
    return JSON.parse(readFileSync(file, "utf8"));
  } catch {
    return null;
  }
};
const dirs = (dir) => (existsSync(dir) ? readdirSync(dir).filter((d) => statSync(path.join(dir, d)).isDirectory()) : []);

function bashClass(command) {
  const cmd = String(command)
    .replace(/^\s*cd\s+[^&;]+(&&|;)\s*/, "")
    .trim();
  if (/^(cat|sed -n|head|tail|less|grep|rg|git grep|ls|find|tree|wc)\b/.test(cmd)) return "explore";
  if (/^(dotnet test|npm test|npx ng test|npx vitest|(\(?timeout \d+ )?(npx ng test|npm test|dotnet test))/.test(cmd)) return "test";
  if (/^(dotnet build|dotnet format|npm run (build|typecheck|lint|format)|npx (tsc|prettier|eslint))/.test(cmd)) return "build/format";
  if (/^git (diff|show|status|log)\b/.test(cmd)) return "git-read";
  if (/^node scripts\/verify/.test(cmd)) return "verify";
  return "other";
}

function agentUsage(file) {
  const seen = new Set();
  const usage = { calls: 0, cacheRead: 0, cacheWrite: 0, input: 0, output: 0, maxContext: 0, bash: {}, model: null };
  for (const line of readFileSync(file, "utf8").split("\n")) {
    if (!line) continue;
    let entry;
    try {
      entry = JSON.parse(line);
    } catch {
      continue;
    }
    if (entry.type !== "assistant" || !entry.message) continue;
    for (const block of entry.message.content || []) {
      if (block.type === "tool_use" && block.name === "Bash") {
        const k = bashClass(block.input?.command);
        usage.bash[k] = (usage.bash[k] || 0) + 1;
      }
    }
    const u = entry.message.usage;
    if (!u || seen.has(entry.message.id)) continue;
    seen.add(entry.message.id);
    usage.model ??= entry.message.model;
    usage.calls++;
    usage.cacheRead += u.cache_read_input_tokens || 0;
    usage.cacheWrite += u.cache_creation_input_tokens || 0;
    usage.input += u.input_tokens || 0;
    usage.output += u.output_tokens || 0;
    usage.maxContext = Math.max(usage.maxContext, (u.cache_read_input_tokens || 0) + (u.cache_creation_input_tokens || 0) + (u.input_tokens || 0));
  }
  return usage;
}

function collectRuns() {
  const runs = [];
  for (const session of dirs(HISTORY)) {
    const wfDir = path.join(HISTORY, session, "workflows");
    if (!existsSync(wfDir)) continue;
    for (const f of readdirSync(wfDir).filter((x) => /^wf_.*\.json$/.test(x))) {
      const run = readJson(path.join(wfDir, f));
      if (!run) continue;
      const runId = run.runId || f.replace(/\.json$/, "");
      const agentDir = path.join(HISTORY, session, "subagents", "workflows", runId);
      const agents = existsSync(agentDir)
        ? readdirSync(agentDir)
            .filter((x) => x.endsWith(".jsonl"))
            .map((x) => {
              const meta = readJson(path.join(agentDir, x.replace(/\.jsonl$/, ".meta.json"))) || {};
              return { type: meta.agentType || "?", phase: meta.workflowPhase || null, ...agentUsage(path.join(agentDir, x)) };
            })
            .filter((a) => a.calls > 0)
        : [];
      const result = run.result || {};
      runs.push({
        runId,
        workflow: run.workflowName,
        startedAt: run.timestamp || null,
        issue: run.args?.issue ?? null,
        tier: run.args?.tier ?? null,
        status: result.status || run.status || null,
        stage: result.stage || null,
        rounds: result.rounds ?? null,
        tests: Array.isArray(result.tests) ? result.tests.length : null,
        minutes: Math.round((run.durationMs || 0) / 60000),
        agents,
      });
    }
  }
  return runs.sort((a, b) => String(a.startedAt).localeCompare(String(b.startedAt)));
}

const sum = (list, key) => list.reduce((n, x) => n + (x[key] || 0), 0);
const M = (n) => `${(n / 1e6).toFixed(1)}M`;
const K = (n) => `${Math.round(n / 1e3)}k`;

function byType(agents) {
  const groups = {};
  for (const a of agents) {
    const g = (groups[a.type] ??= { type: a.type, agents: 0, calls: 0, cacheRead: 0, cacheWrite: 0, output: 0, maxContext: 0, bash: {} });
    g.agents++;
    g.calls += a.calls;
    g.cacheRead += a.cacheRead;
    g.cacheWrite += a.cacheWrite;
    g.output += a.output;
    g.maxContext = Math.max(g.maxContext, a.maxContext);
    for (const [k, v] of Object.entries(a.bash)) g.bash[k] = (g.bash[k] || 0) + v;
  }
  return Object.values(groups).sort((x, y) => y.cacheRead - x.cacheRead);
}

function table(rows, columns) {
  const widths = columns.map((c) => Math.max(c.length, ...rows.map((r) => String(r[c] ?? "").length)));
  const line = (cells) => cells.map((c, i) => String(c ?? "").padEnd(widths[i])).join("  ");
  return [line(columns), line(widths.map((w) => "-".repeat(w))), ...rows.map((r) => line(columns.map((c) => r[c])))].join("\n");
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  if (!existsSync(HISTORY)) throw new Error(`no Claude Code history for this repo at ${HISTORY}`);
  let runs = collectRuns().filter((r) => r.workflow === "build-feature");
  if (!runs.length) throw new Error("no build-feature runs found in the local history");
  const newest = new Date(runs.at(-1).startedAt);
  const since = args.since || new Date(newest.getTime() - 30 * 864e5).toISOString().slice(0, 10);
  runs = runs.filter((r) => String(r.startedAt) >= since && (args.issue == null || r.issue === args.issue));

  const allAgents = runs.flatMap((r) => r.agents);
  const types = byType(allAgents);
  const totalRead = sum(allAgents, "cacheRead");
  const summary = {
    since,
    runs: runs.length,
    shipped: runs.filter((r) => r.status === "shipped").length,
    blocked: runs.filter((r) => r.status === "blocked").length,
    cacheRead: totalRead,
    cacheWrite: sum(allAgents, "cacheWrite"),
    output: sum(allAgents, "output"),
    perRun: runs.length ? Math.round(totalRead / runs.length) : 0,
  };

  if (args.json) {
    console.log(JSON.stringify({ summary, byType: types, runs }, null, 2));
    return;
  }

  console.log(`build-feature runs since ${since}${args.issue != null ? ` for #${args.issue}` : ""}: ${summary.runs} (${summary.shipped} shipped, ${summary.blocked} blocked)`);
  console.log(`cache read ${M(summary.cacheRead)} · cache write ${M(summary.cacheWrite)} · output ${K(summary.output)} · cache read per run ${M(summary.perRun)}\n`);

  console.log("By agent type");
  console.log(
    table(
      types.map((t) => {
        const bashTotal = Object.values(t.bash).reduce((a, b) => a + b, 0) || 1;
        return {
          agent: t.type,
          agents: t.agents,
          "calls/agent": Math.round(t.calls / t.agents),
          "cache read": M(t.cacheRead),
          share: `${Math.round((t.cacheRead / (totalRead || 1)) * 100)}%`,
          "cache write": M(t.cacheWrite),
          "max ctx": K(t.maxContext),
          "bash explore": `${Math.round(((t.bash.explore || 0) / bashTotal) * 100)}%`,
        };
      }),
      ["agent", "agents", "calls/agent", "cache read", "share", "cache write", "max ctx", "bash explore"],
    ),
  );

  console.log(`\nNewest ${Math.min(args.runs, runs.length)} run(s)`);
  console.log(
    table(
      runs.slice(-args.runs).map((r) => {
        const t = Object.fromEntries(byType(r.agents).map((g) => [g.type, g]));
        return {
          started: String(r.startedAt).slice(0, 16).replace("T", " "),
          issue: r.issue != null ? `#${r.issue}` : "",
          tier: r.tier ?? "",
          status: r.stage ? `${r.status}@${r.stage}` : r.status,
          min: r.minutes,
          rounds: r.rounds ?? "",
          tests: r.tests ?? "",
          "cache read": M(sum(r.agents, "cacheRead")),
          "test-writer": t["test-writer"] ? `${t["test-writer"].calls}c/${K(t["test-writer"].maxContext)}` : "",
          implementer: t.implementer ? `${t.implementer.calls}c/${K(t.implementer.maxContext)}` : "",
        };
      }),
      ["started", "issue", "tier", "status", "min", "rounds", "tests", "cache read", "test-writer", "implementer"],
    ),
  );
  console.log("\n(test-writer / implementer: model calls / largest context reached)");
}

try {
  main();
} catch (err) {
  console.error(`run-cost.mjs: ${err.message}`);
  process.exit(1);
}
