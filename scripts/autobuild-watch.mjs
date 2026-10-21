// Usage: imported by autobuild.mjs - awake clock with sleep detection, hang detector, hang restart policy, summary lines; pure, injectable clock, runs nothing on import
import { spawn } from "node:child_process";

const MIN = 60e3;
export const HANG_MS = 45 * MIN;
export const BUILD_LIMIT_MS = 4 * 60 * MIN;
const SLEEP_GAP_MS = 3 * MIN;
const SAMPLE_AFTER_MS = 5 * MIN;
const MAX_HANG_RESTARTS = 1;

const hhmm = (ms) => new Date(ms).toTimeString().slice(0, 5);
const kTokens = (n) => `${Math.round((n ?? 0) / 1000)}k`;

export function createAwakeClock({ now = Date.now, gapMs = SLEEP_GAP_MS } = {}) {
  const startedAt = now();
  let lastTick = startedAt;
  let sleptMs = 0;
  return {
    tick() {
      const at = now();
      const from = lastTick;
      lastTick = at;
      const gap = at - from;
      if (gap <= gapMs) return null;
      sleptMs += gap;
      return { from, to: at, ms: gap, line: `PC spał ${hhmm(from)}–${hhmm(at)} (${Math.round(gap / MIN)} min)` };
    },
    awakeMs: () => now() - startedAt - sleptMs,
    sleptMs: () => sleptMs,
    expired: (limitMs) => now() - startedAt - sleptMs > limitMs,
  };
}

const WORK_NAME = /^(dotnet|testhost|vstest[\w.]*)(\.exe)?$/i;
const WORK_NODE = /scripts[\\/]verify\.mjs|scripts[\\/]e2e\.mjs|vitest|playwright/i;
const NOT_WORK = /mcp|nodeReuse|VBCSCompiler|build-server/i;

export function isWorkProcess({ name, commandLine }) {
  const cmd = String(commandLine ?? "");
  if (NOT_WORK.test(cmd)) return false;
  if (WORK_NAME.test(String(name ?? ""))) return true;
  return /^node(\.exe)?$/i.test(String(name ?? "")) && WORK_NODE.test(cmd);
}

export function hasWorkProcess(processes, rootPid) {
  const children = new Map();
  for (const p of processes) children.set(p.parentPid, [...(children.get(p.parentPid) ?? []), p]);
  const seen = new Set([rootPid]);
  const queue = [rootPid];
  while (queue.length) {
    for (const p of children.get(queue.shift()) ?? []) {
      if (seen.has(p.pid)) continue;
      seen.add(p.pid);
      if (isWorkProcess(p)) return true;
      queue.push(p.pid);
    }
  }
  return false;
}

const LIST_SCRIPT = "Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,Name,CommandLine | ConvertTo-Json -Compress";

function listProcesses() {
  return new Promise((resolve) => {
    let out = "";
    const encoded = Buffer.from(LIST_SCRIPT, "utf16le").toString("base64");
    const child = spawn("powershell.exe", ["-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-EncodedCommand", encoded], { windowsHide: true, stdio: ["ignore", "pipe", "ignore"] });
    const backstop = setTimeout(() => {
      child.kill();
      resolve(null);
    }, 60e3);
    child.stdout.setEncoding("utf8");
    child.stdout.on("data", (chunk) => {
      out += chunk;
    });
    child.on("error", () => {
      clearTimeout(backstop);
      resolve(null);
    });
    child.on("close", () => {
      clearTimeout(backstop);
      try {
        const parsed = JSON.parse(out);
        resolve((Array.isArray(parsed) ? parsed : [parsed]).map((p) => ({ pid: p.ProcessId, parentPid: p.ParentProcessId, name: p.Name, commandLine: p.CommandLine })));
      } catch {
        resolve(null);
      }
    });
  });
}

// An unreadable process list holds the timer: the 4 h limit is the backstop, a wrongly killed verify is not
export async function liveWork(rootPid) {
  if (process.platform !== "win32") return false;
  const processes = await listProcesses();
  return processes ? hasWorkProcess(processes, rootPid) : true;
}

export function createHangDetector({ clock, hasWork = async () => false, limitMs = HANG_MS, sampleAfterMs = SAMPLE_AFTER_MS }) {
  let idleMs = 0;
  let lastAwake = clock.awakeMs();
  let last = {};
  const reset = () => {
    idleMs = 0;
    lastAwake = clock.awakeMs();
  };
  return {
    progress(snapshot) {
      const changed = snapshot.tokens !== last.tokens || snapshot.tools !== last.tools;
      last = { ...snapshot };
      if (changed) reset();
    },
    agentEvent: reset,
    idleMs: () => idleMs,
    async check() {
      const awake = clock.awakeMs();
      const next = idleMs + Math.max(0, awake - lastAwake);
      lastAwake = awake;
      if (next >= sampleAfterMs && (await hasWork())) return { hung: false };
      idleMs = next;
      return idleMs >= limitMs ? { hung: true, phase: last.phase, agent: last.agent, tokens: last.tokens, tools: last.tools } : { hung: false };
    },
  };
}

export function hangLabel(hang) {
  return [hang.phase, hang.agent, `${kTokens(hang.tokens)} tokens`, `${hang.tools ?? 0} tools`].filter(Boolean).join(" · ");
}

export function createHangPolicy(maxRestarts = MAX_HANG_RESTARTS) {
  const counts = new Map();
  return {
    decide(part) {
      const seen = counts.get(part) ?? 0;
      if (seen >= maxRestarts) return { action: "stop", reason: "hang" };
      counts.set(part, seen + 1);
      return { action: "restart", reason: "hang" };
    },
    restarts: () => [...counts.values()].reduce((a, b) => a + b, 0),
  };
}

// `build` kills the hung session's tree before it returns { hung }; the stack is stopped next, then the same part is built again as a plain replay
export async function buildThroughHangs({ part, build, policy, stopStack, resumeFrom = () => null, log }) {
  while (true) {
    const result = await build(part);
    if (!result.hung) return result;
    const decision = policy.decide(part.number);
    if (decision.action === "stop") return { ...result, hangStop: true };
    log(`zawieszenie: brak postępu ${Math.round(HANG_MS / MIN)} min (${hangLabel(result.hung)}) — restart #${part.number}`);
    await stopStack();
    const from = resumeFrom(part);
    if (from) log(`restart #${part.number}: powtórka wznawia od ${from}`);
  }
}

export function summaryLines({ restarts = 0, sleptMs = 0, guardWarning = null }) {
  const lines = [];
  if (restarts > 0) lines.push(`hang restarts: ${restarts}`);
  if (sleptMs > 0) lines.push(`PC spał łącznie ${Math.round(sleptMs / MIN)} min`);
  if (guardWarning) lines.push(guardWarning);
  return lines;
}
