#!/usr/bin/env node
// Usage: node scripts/run-cost.mjs [--since YYYY-MM-DD] [--issue <n>] [--runs <N>] [--json] [--sessions] [--timeline]
//   --since    only runs started on or after this date (default: 30 days before the newest run; --sessions: 30 days ago)
//   --issue    only runs for this spec issue
//   --runs     how many of the newest runs to list one by one (default 15)
//   --json     one JSON object instead of the tables
//   --timeline per-agent wall time of the newest matching run (respects --issue), with the minutes spent in verify.mjs and dotnet test
//   --sessions main sessions by first slash command and non-workflow subagents modified since --since, instead of build runs

import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const CONFIG_DIR = process.env.CLAUDE_CONFIG_DIR || path.join(os.homedir(), ".claude");
// Claude Code names the history folder after the path with every non-alphanumeric character as "-".
const HISTORY = path.join(CONFIG_DIR, "projects", REPO_ROOT.replace(/[^A-Za-z0-9]/g, "-"));
// USD per million tokens at list price. The haiku-5-5-long tier applies above 100k prompt tokens per request.
const PRICES = {
  opus: { input: 4, output: 20, cacheRead: 0.2 },
  sonnet: { input: 2, output: 10, cacheRead: 0.2 },
  "haiku-5-5": { input: 0.1, output: 0.5, cacheRead: 0.01 },
  "haiku-5-5-long": { input: 0.5, output: 2.5, cacheRead: 0.05 },
  "haiku-4-5": { input: 1, output: 5, cacheRead: 0.1 },
};
const LONG_PROMPT_TOKENS = 100e3;

function parseArgs(argv) {
  const args = { since: null, issue: null, runs: 15, json: false, sessions: false, timeline: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === "--json") args.json = true;
    else if (a === "--sessions") args.sessions = true;
    else if (a === "--timeline") args.timeline = true;
    else if (a === "--since") args.since = argv[++i];
    else if (a === "--issue") args.issue = Number(String(argv[++i]).replace(/^#/, ""));
    else if (a === "--runs") args.runs = Number(argv[++i]);
    else throw new Error(`unknown argument '${a}' — usage: run-cost.mjs [--since YYYY-MM-DD] [--issue n] [--runs N] [--json] [--sessions] [--timeline]`);
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

function priceOf(model, promptTokens) {
  if (/^claude-opus-/.test(model)) return PRICES.opus;
  if (/^claude-sonnet-/.test(model)) return PRICES.sonnet;
  if (model === "claude-haiku-5-5") return promptTokens > LONG_PROMPT_TOKENS ? PRICES["haiku-5-5-long"] : PRICES["haiku-5-5"];
  if (/^claude-haiku-4-5/.test(model)) return PRICES["haiku-4-5"];
  return null;
}

function callCost(model, u) {
  const input = u.input_tokens || 0;
  const cacheRead = u.cache_read_input_tokens || 0;
  const cacheWrite = u.cache_creation_input_tokens || 0;
  const oneHourWrite = u.cache_creation?.ephemeral_1h_input_tokens || 0;
  const p = priceOf(model, input + cacheRead + cacheWrite);
  if (!p) return 0;
  const usd =
    input * p.input +
    (cacheWrite - oneHourWrite) * p.input * 1.25 +
    oneHourWrite * p.input * 2 +
    (u.output_tokens || 0) * p.output +
    cacheRead * p.cacheRead;
  return usd / 1e6;
}

function transcriptCalls(file) {
  const seen = new Set();
  const calls = [];
  let first = null;
  for (const line of readFileSync(file, "utf8").split("\n")) {
    if (!line) continue;
    let entry;
    try {
      entry = JSON.parse(line);
    } catch {
      continue;
    }
    if (first === null && entry.type === "user" && typeof entry.message?.content === "string") {
      first = entry.message.content.match(/<command-name>\/?([\w:-]+)/)?.[1] ?? "chat";
    }
    const u = entry.message?.usage;
    const model = entry.message?.model;
    if (entry.type !== "assistant" || !u || model === "<synthetic>" || seen.has(entry.message.id)) continue;
    seen.add(entry.message.id);
    calls.push({ model, usage: u });
  }
  return { first, calls };
}

function agentUsage(file) {
  const seen = new Set();
  const models = {};
  const usage = { calls: 0, cacheRead: 0, cacheWrite: 0, input: 0, output: 0, cost: 0, maxContext: 0, bash: {}, model: null };
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
    const model = entry.message.model;
    if (!u || model === "<synthetic>" || seen.has(entry.message.id)) continue;
    seen.add(entry.message.id);
    models[model] = (models[model] || 0) + 1;
    usage.calls++;
    usage.cacheRead += u.cache_read_input_tokens || 0;
    usage.cacheWrite += u.cache_creation_input_tokens || 0;
    usage.input += u.input_tokens || 0;
    usage.output += u.output_tokens || 0;
    usage.cost += callCost(model, u);
    usage.maxContext = Math.max(usage.maxContext, (u.cache_read_input_tokens || 0) + (u.cache_creation_input_tokens || 0) + (u.input_tokens || 0));
  }
  usage.model = Object.entries(models).sort((a, b) => b[1] - a[1])[0]?.[0] ?? null;
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
        deviations: Array.isArray(result.deviations) ? result.deviations.length : null,
        minutes: Math.round((run.durationMs || 0) / 60000),
        cost: sum(agents, "cost"),
        agents,
      });
    }
  }
  return runs.sort((a, b) => String(a.startedAt).localeCompare(String(b.startedAt)));
}

function agentTimes(file) {
  const open = new Map();
  const t = { verifyMs: 0, testMs: 0, hookCancelled: false };
  for (const line of readFileSync(file, "utf8").split("\n")) {
    if (!line) continue;
    let entry;
    try {
      entry = JSON.parse(line);
    } catch {
      continue;
    }
    if (entry.attachment?.type === "hook_cancelled") t.hookCancelled = true;
    const ts = Date.parse(entry.timestamp);
    if (!Array.isArray(entry.message?.content) || Number.isNaN(ts)) continue;
    for (const block of entry.message.content) {
      if (block.type === "tool_use" && block.name === "Bash") {
        const cmd = String(block.input?.command ?? "");
        const kind = cmd.includes("verify.mjs") ? "verifyMs" : /^\s*(cd\s+[^&;]+(&&|;)\s*)?dotnet test/.test(cmd) ? "testMs" : null;
        if (kind) open.set(block.id, { kind, ts });
      } else if (block.type === "tool_result" && open.has(block.tool_use_id)) {
        const o = open.get(block.tool_use_id);
        t[o.kind] += Math.max(0, ts - o.ts);
        open.delete(block.tool_use_id);
      }
    }
  }
  return t;
}

function timelineReport(issue) {
  let best = null;
  for (const session of dirs(HISTORY)) {
    const wfDir = path.join(HISTORY, session, "workflows");
    if (!existsSync(wfDir)) continue;
    for (const f of readdirSync(wfDir).filter((x) => /^wf_.*\.json$/.test(x))) {
      const run = readJson(path.join(wfDir, f));
      if (!run || run.workflowName !== "build-feature" || (issue != null && run.args?.issue !== issue)) continue;
      if (!best || String(run.timestamp) > String(best.run.timestamp)) best = { run, session, runId: run.runId || f.replace(/\.json$/, "") };
    }
  }
  if (!best) throw new Error(`no build-feature run found${issue != null ? ` for #${issue}` : ""}`);
  const { run, session, runId } = best;
  const progress = (run.workflowProgress || []).filter((x) => x.type === "workflow_agent" && x.startedAt).sort((a, b) => a.startedAt - b.startedAt);
  const end = run.startTime + (run.durationMs || 0);
  const min = (ms) => Math.round(ms / 600) / 100;
  const agents = progress.map((a, i) => {
    const file = path.join(HISTORY, session, "subagents", "workflows", runId, `agent-${a.agentId}.jsonl`);
    const t = existsSync(file) ? agentTimes(file) : { verifyMs: 0, testMs: 0, hookCancelled: false };
    return {
      index: i + 1,
      label: a.label,
      agent: a.agentType,
      model: short(a.model),
      wallMin: min((progress[i + 1]?.startedAt ?? end) - a.startedAt),
      verifyMin: min(t.verifyMs),
      dotnetTestMin: min(t.testMs),
      notes: t.hookCancelled ? "hook cancelled" : "",
    };
  });
  return { runId, issue: run.args?.issue ?? null, tier: run.args?.tier ?? null, totalMin: min(run.durationMs || 0), agents };
}

function sessionReport(since) {
  const cutoff = new Date(since);
  const commands = {};
  for (const f of readdirSync(HISTORY).filter((x) => x.endsWith(".jsonl"))) {
    const file = path.join(HISTORY, f);
    if (statSync(file).mtime < cutoff) continue;
    const { first, calls } = transcriptCalls(file);
    const command = first ?? "chat";
    const g = (commands[command] ??= { command, sessions: 0, cost: 0 });
    g.sessions++;
    g.cost += calls.reduce((n, c) => n + callCost(c.model, c.usage), 0);
  }
  const subagents = {};
  for (const session of dirs(HISTORY)) {
    const dir = path.join(HISTORY, session, "subagents");
    if (!existsSync(dir)) continue;
    for (const f of readdirSync(dir).filter((x) => x.endsWith(".jsonl"))) {
      const file = path.join(dir, f);
      if (statSync(file).mtime < cutoff) continue;
      const type = readJson(file.replace(/\.jsonl$/, ".meta.json"))?.agentType || "?";
      const touched = new Set();
      for (const c of transcriptCalls(file).calls) {
        const key = `${type}|${c.model}`;
        const g = (subagents[key] ??= { type, model: c.model, agents: 0, calls: 0, cost: 0 });
        if (!touched.has(key)) {
          touched.add(key);
          g.agents++;
        }
        g.calls++;
        g.cost += callCost(c.model, c.usage);
      }
    }
  }
  return {
    since,
    mainSessions: Object.values(commands).sort((x, y) => y.cost - x.cost),
    subagents: Object.values(subagents).sort((x, y) => y.cost - x.cost),
  };
}

const sum = (list, key) => list.reduce((n, x) => n + (x[key] || 0), 0);
const M = (n) => `${(n / 1e6).toFixed(1)}M`;
const K = (n) => `${Math.round(n / 1e3)}k`;
const D = (n) => `$${n.toFixed(1)}`;
const short = (model) => String(model ?? "").replace(/^claude-/, "");
const daysAgo = (n) => new Date(Date.now() - n * 864e5).toISOString().slice(0, 10);

function byType(agents) {
  const groups = {};
  for (const a of agents) {
    const key = `${a.type}|${a.model}`;
    const g = (groups[key] ??= { type: a.type, model: a.model, agents: 0, calls: 0, cacheRead: 0, cacheWrite: 0, output: 0, cost: 0, maxContext: 0, bash: {} });
    g.agents++;
    g.calls += a.calls;
    g.cacheRead += a.cacheRead;
    g.cacheWrite += a.cacheWrite;
    g.output += a.output;
    g.cost += a.cost;
    g.maxContext = Math.max(g.maxContext, a.maxContext);
    for (const [k, v] of Object.entries(a.bash)) g.bash[k] = (g.bash[k] || 0) + v;
  }
  return Object.values(groups).sort((x, y) => y.cacheRead - x.cacheRead);
}

function typeCell(agents, type) {
  const list = agents.filter((a) => a.type === type);
  return list.length ? `${sum(list, "calls")}c/${K(Math.max(...list.map((a) => a.maxContext)))}` : "";
}

function table(rows, columns) {
  const widths = columns.map((c) => Math.max(c.length, ...rows.map((r) => String(r[c] ?? "").length)));
  const line = (cells) => cells.map((c, i) => String(c ?? "").padEnd(widths[i])).join("  ");
  return [line(columns), line(widths.map((w) => "-".repeat(w))), ...rows.map((r) => line(columns.map((c) => r[c])))].join("\n");
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  if (!existsSync(HISTORY)) throw new Error(`no Claude Code history for this repo at ${HISTORY}`);

  if (args.timeline) {
    const report = timelineReport(args.issue);
    if (args.json) {
      console.log(JSON.stringify(report, null, 2));
      return;
    }
    console.log(`run ${report.runId} · #${report.issue} tier ${report.tier} · ${report.totalMin.toFixed(1)} min`);
    console.log(
      table(
        report.agents.map((a) => ({
          "#": a.index,
          label: a.label,
          agent: a.agent,
          model: a.model,
          "wall min": a.wallMin.toFixed(1),
          "verify min": a.verifyMin.toFixed(1),
          "dotnet test min": a.dotnetTestMin.toFixed(1),
          notes: a.notes,
        })),
        ["#", "label", "agent", "model", "wall min", "verify min", "dotnet test min", "notes"],
      ),
    );
    return;
  }

  if (args.sessions) {
    const report = sessionReport(args.since || daysAgo(30));
    if (args.json) {
      console.log(JSON.stringify(report, null, 2));
      return;
    }
    const mainCost = sum(report.mainSessions, "cost");
    console.log(`main sessions modified since ${report.since}: ${sum(report.mainSessions, "sessions")} (${D(mainCost)})\n`);
    console.log("Main sessions by first slash command");
    console.log(
      table(
        report.mainSessions.map((c) => ({ command: c.command, sessions: c.sessions, $: D(c.cost), "avg $": D(c.cost / c.sessions) })),
        ["command", "sessions", "$", "avg $"],
      ),
    );
    console.log(`\nNon-workflow subagents modified since ${report.since}`);
    console.log(
      table(
        report.subagents.map((s) => ({ agent: s.type, model: short(s.model), agents: s.agents, calls: s.calls, $: D(s.cost) })),
        ["agent", "model", "agents", "calls", "$"],
      ),
    );
    return;
  }

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
    cost: sum(runs, "cost"),
    costPerRun: runs.length ? sum(runs, "cost") / runs.length : 0,
  };

  if (args.json) {
    console.log(JSON.stringify({ summary, byType: types, runs }, null, 2));
    return;
  }

  console.log(`build-feature runs since ${since}${args.issue != null ? ` for #${args.issue}` : ""}: ${summary.runs} (${summary.shipped} shipped, ${summary.blocked} blocked)`);
  console.log(
    `cache read ${M(summary.cacheRead)} · cache write ${M(summary.cacheWrite)} · output ${K(summary.output)} · cache read per run ${M(summary.perRun)} · cost ${D(summary.cost)} · ${D(summary.costPerRun)} per run\n`,
  );

  console.log("By agent type");
  console.log(
    table(
      types.map((t) => {
        const bashTotal = Object.values(t.bash).reduce((a, b) => a + b, 0) || 1;
        return {
          agent: t.type,
          model: short(t.model),
          agents: t.agents,
          "calls/agent": Math.round(t.calls / t.agents),
          "cache read": M(t.cacheRead),
          share: `${Math.round((t.cacheRead / (totalRead || 1)) * 100)}%`,
          "cache write": M(t.cacheWrite),
          "max ctx": K(t.maxContext),
          "bash explore": `${Math.round(((t.bash.explore || 0) / bashTotal) * 100)}%`,
          $: D(t.cost),
        };
      }),
      ["agent", "model", "agents", "calls/agent", "cache read", "share", "cache write", "max ctx", "bash explore", "$"],
    ),
  );

  console.log(`\nNewest ${Math.min(args.runs, runs.length)} run(s)`);
  console.log(
    table(
      runs.slice(-args.runs).map((r) => ({
        started: String(r.startedAt).slice(0, 16).replace("T", " "),
        issue: r.issue != null ? `#${r.issue}` : "",
        tier: r.tier ?? "",
        status: r.stage ? `${r.status}@${r.stage}` : r.status,
        min: r.minutes,
        rounds: r.rounds ?? "",
        tests: r.tests ?? "",
        "cache read": M(sum(r.agents, "cacheRead")),
        $: D(r.cost),
        dev: r.deviations ?? "",
        "test-writer": typeCell(r.agents, "test-writer"),
        implementer: typeCell(r.agents, "implementer"),
      })),
      ["started", "issue", "tier", "status", "min", "rounds", "tests", "cache read", "$", "dev", "test-writer", "implementer"],
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
