#!/usr/bin/env node
// Stops a locally running Skarbiec stack so builds, tests and installs can proceed.
//
// Usage: node scripts/stop-stack.mjs [--dry-run]
//
// A running stack gets in the way of automated work: the Aspire AppHost and the services it launched
// hold their bin/ outputs open, so `dotnet build` / `dotnet test` fail with MSB3027 / MSB3021 ("the
// file is being used by another process"), and `ng serve` holds web/node_modules binaries (esbuild)
// and port 4200. Any agent that hits one of those may run this script and retry. `verify.mjs` calls
// it on its own when a build fails on a locked file.
//
// What counts as the stack:
//   - `dotnet run ... Skarbiec...` (the AppHost, or a service run by hand) — killed with its tree;
//   - any `Skarbiec.*` executable that is not a test host (the services, the Gateway, the AppHost);
//   - `ng serve` (started by `npm start` in web/).
// Test hosts, `dotnet build` / `dotnet test` and this script's own process chain are never touched.
//
// Prints one JSON line `{"stopped":[{pid,name,what}]}` and exits 0 — also when nothing was running.
// Node >= 22, ESM, zero npm dependencies. Also importable: `import { stopStack } from "./stop-stack.mjs"`.

import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const IS_WIN = process.platform === "win32";

function listProcesses() {
  if (IS_WIN) {
    const script =
      "Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,Name,CommandLine | ConvertTo-Json -Compress";
    const res = spawnSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], {
      encoding: "utf8",
      maxBuffer: 64 * 1024 * 1024,
      timeout: 60_000,
    });
    if (res.status !== 0) throw new Error(`could not list processes: ${res.stderr || res.error?.message}`);
    return JSON.parse(res.stdout || "[]").map((p) => ({
      pid: p.ProcessId,
      ppid: p.ParentProcessId,
      name: p.Name ?? "",
      cmd: p.CommandLine ?? "",
    }));
  }
  const res = spawnSync("ps", ["-eo", "pid=,ppid=,comm=,args="], { encoding: "utf8" });
  if (res.status !== 0) throw new Error(`could not list processes: ${res.stderr}`);
  return res.stdout
    .split("\n")
    .map((line) => line.trim().match(/^(\d+)\s+(\d+)\s+(\S+)\s+(.*)$/))
    .filter(Boolean)
    .map(([, pid, ppid, name, cmd]) => ({ pid: Number(pid), ppid: Number(ppid), name, cmd }));
}

function classify(p) {
  const name = p.name.toLowerCase();
  const cmd = p.cmd;
  if (/test/i.test(name) || /\.Tests\b/i.test(cmd) || /\btesthost\b/i.test(cmd)) return null;
  if (/^skarbiec\./.test(name)) return "service";
  if (/^dotnet(\.exe)?$/.test(name) && /\brun\b/.test(cmd) && /Skarbiec/i.test(cmd)) return "dotnet run";
  if (/^node(\.exe)?$/.test(name) && /\bng(\.js)?["']?\s+serve\b/.test(cmd)) return "ng serve";
  return null;
}

// This script's own ancestors (e.g. an agent's shell running `dotnet run` elsewhere) are never killed.
function ancestorsOf(pid, byPid) {
  const seen = new Set();
  for (let p = byPid.get(pid); p && !seen.has(p.pid); p = byPid.get(p.ppid)) seen.add(p.pid);
  return seen;
}

function findStack() {
  const all = listProcesses();
  const byPid = new Map(all.map((p) => [p.pid, p]));
  const own = ancestorsOf(process.pid, byPid);
  return all
    .map((p) => ({ ...p, what: classify(p) }))
    .filter((p) => p.what && !own.has(p.pid));
}

function kill(pid) {
  if (IS_WIN) spawnSync("taskkill", ["/PID", String(pid), "/T", "/F"], { encoding: "utf8" });
  else spawnSync("kill", ["-KILL", String(pid)]);
}

const sleep = (ms) => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);

export function stopStack({ dryRun = false } = {}) {
  const found = findStack();
  const stopped = found.map(({ pid, name, what }) => ({ pid, name, what }));
  if (dryRun || !found.length) return { stopped, dryRun };

  // Parents first: killing `dotnet run` with its tree takes most of the stack down in one go.
  const order = { "dotnet run": 0, "ng serve": 1, service: 2 };
  for (const p of [...found].sort((a, b) => order[a.what] - order[b.what])) kill(p.pid);

  // Wait until the processes are gone and their file handles are released.
  for (let i = 0; i < 20 && findStack().length; i++) sleep(1000);
  const left = findStack().map(({ pid, name, what }) => ({ pid, name, what }));
  return left.length ? { stopped, left } : { stopped };
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  console.log(JSON.stringify(stopStack({ dryRun: process.argv.includes("--dry-run") })));
}
