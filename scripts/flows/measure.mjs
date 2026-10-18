import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import os from "node:os";
import path from "node:path";

const SAMPLE_LIMIT = 10;
const WORKFLOW = "build-feature";

export const MEASURED_NODES = { "wf:build-feature": /^(agent|run|verify):/, "script:epic": /^epic:/ };

const KNOWN_KEYS = [
  "run:build-feature",
  "agent:test-writer:failing tests from acceptance criteria",
  "agent:implementer:implement the spec",
  "agent:implementer:implement the spec (escalated)",
  "agent:implementer:address blocking findings",
  "agent:implementer:address blocking findings (escalated)",
  "agent:e2e:e2e: automate the epic plan and run the suite",
  "agent:e2e:e2e: automate the epic plan and run the suite (escalated)",
  "agent:e2e:address blocking findings",
  "agent:e2e:address blocking findings (escalated)",
  "agent:reviewer:adversarial review",
  "agent:reviewer:re-review",
  "verify:implementer",
  "epic:build",
  "epic:ci",
];

export function defaultHistory(root) {
  const config = process.env.CLAUDE_CONFIG_DIR || path.join(os.homedir(), ".claude");
  return path.join(config, "projects", root.replace(/[^A-Za-z0-9]/g, "-"));
}

const readJson = (file) => {
  try {
    return JSON.parse(readFileSync(file, "utf8"));
  } catch {
    return null;
  }
};

const subdirs = (dir) => (existsSync(dir) ? readdirSync(dir).filter((d) => statSync(path.join(dir, d)).isDirectory()) : []);

function verifyMs(file) {
  const open = new Map();
  let total = 0;
  let calls = 0;
  for (const line of readFileSync(file, "utf8").split("\n")) {
    if (!line) continue;
    let entry;
    try {
      entry = JSON.parse(line);
    } catch {
      continue;
    }
    const ts = Date.parse(entry.timestamp);
    if (!Array.isArray(entry.message?.content) || Number.isNaN(ts)) continue;
    for (const block of entry.message.content) {
      if (block.type === "tool_use" && block.name === "Bash" && String(block.input?.command ?? "").includes("verify.mjs")) {
        open.set(block.id, ts);
      } else if (block.type === "tool_result" && open.has(block.tool_use_id)) {
        total += Math.max(0, ts - open.get(block.tool_use_id));
        calls++;
        open.delete(block.tool_use_id);
      }
    }
  }
  return calls > 0 ? total : null;
}

function workflowSamples(history, add) {
  for (const session of subdirs(history)) {
    const wfDir = path.join(history, session, "workflows");
    if (!existsSync(wfDir)) continue;
    for (const file of readdirSync(wfDir).filter((f) => /^wf_.*\.json$/.test(f))) {
      const run = readJson(path.join(wfDir, file));
      if (!run || run.workflowName !== WORKFLOW || run.result?.status !== "ready" || !Number.isFinite(run.startTime)) continue;
      const runId = run.runId || file.replace(/\.json$/, "");
      const end = run.startTime + (run.durationMs || 0);
      add(`run:${WORKFLOW}`, run.startTime, (run.durationMs || 0) / 1000);
      const agents = (run.workflowProgress ?? [])
        .filter((x) => x.type === "workflow_agent" && x.startedAt)
        .sort((a, b) => a.startedAt - b.startedAt);
      agents.forEach((a, i) => {
        const label = String(a.label ?? "").replace(/\s*\(round \d+\)/, "");
        add(`agent:${a.agentType}:${label}`, run.startTime, ((agents[i + 1]?.startedAt ?? end) - a.startedAt) / 1000);
        if (a.agentType !== "implementer") return;
        const transcript = path.join(history, session, "subagents", "workflows", runId, `agent-${a.agentId}.jsonl`);
        const ms = existsSync(transcript) ? verifyMs(transcript) : null;
        if (ms !== null) add("verify:implementer", run.startTime, ms / 1000);
      });
    }
  }
}

function epicSamples(base, add) {
  const logsIn = (dir) => (existsSync(dir) ? readdirSync(dir).filter((f) => /^\d{8}-\d{6}\.log$/.test(f)).map((f) => path.join(dir, f)) : []);
  const files = [...logsIn(base), ...subdirs(base).flatMap((epic) => logsIn(path.join(base, epic)))];
  for (const file of files) {
    const [, y, mo, d, h, mi, s] = path.basename(file).match(/^(\d{4})(\d{2})(\d{2})-(\d{2})(\d{2})(\d{2})/);
    const started = new Date(+y, +mo - 1, +d, +h, +mi, +s).getTime();
    for (const line of readFileSync(file, "utf8").split(/\r?\n/)) {
      const at = line.indexOf("done in this run:");
      if (at < 0) continue;
      for (const part of line.slice(at).split(";")) {
        const m = part.match(/#\d+ PR #\d+ \((?:build (\d+) min|ship retried), CI (\d+) min\)/);
        if (!m) continue;
        if (m[1] !== undefined) add("epic:build", started, Number(m[1]));
        add("epic:ci", started, Number(m[2]));
      }
    }
  }
}

const day = (ms) => new Date(ms).toISOString().slice(0, 10);

function summarize(samples) {
  const newest = [...samples].sort((a, b) => b.at - a.at).slice(0, SAMPLE_LIMIT);
  if (!newest.length) return { n: 0 };
  const values = newest.map((s) => s.value).sort((a, b) => a - b);
  const mid = values.length >> 1;
  const median = values.length % 2 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
  const times = newest.map((s) => s.at);
  return { n: values.length, median: Math.round(median), max: Math.round(values.at(-1)), from: day(Math.min(...times)), to: day(Math.max(...times)) };
}

export const defaultEpicRuns = (root) => path.join(root, "skarbiec-plan", "epic-runs");

export function collectMeasurements(history, epicRuns) {
  const buckets = new Map(KNOWN_KEYS.map((key) => [key, []]));
  const add = (key, at, value) => {
    if (!buckets.has(key)) buckets.set(key, []);
    buckets.get(key).push({ at, value });
  };
  workflowSamples(history, add);
  epicSamples(epicRuns, add);
  return Object.fromEntries(
    [...buckets].map(([key, samples]) => [key, { unit: key.startsWith("epic:") ? "min" : "s", ...summarize(samples) }]),
  );
}

export function measurableKeys(nodeId, measurements) {
  const pattern = MEASURED_NODES[nodeId];
  return pattern ? Object.keys(measurements).filter((key) => pattern.test(key)) : [];
}

export function formatDuration(value, unit) {
  if (unit === "min") return `${value} min`;
  const minutes = Math.floor(value / 60);
  const seconds = value % 60;
  if (!minutes) return `${seconds} s`;
  return seconds ? `${minutes} min ${seconds} s` : `${minutes} min`;
}
