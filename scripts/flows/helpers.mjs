import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { cpSync, existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
export const REPO_ROOT = path.resolve(HERE, "..", "..");
export const FLOWS_CLI = path.join(REPO_ROOT, "scripts", "flows.mjs");
const FIXTURE = path.join(HERE, "fixtures", "mini");

function git(cwd, ...args) {
  const res = spawnSync("git", args, { cwd, encoding: "utf8", windowsHide: true });
  if (res.status !== 0) throw new Error(`git ${args.join(" ")} failed: ${res.stderr}`);
  return res.stdout.trim();
}

export function makeSandbox() {
  const dir = mkdtempSync(path.join(os.tmpdir(), "flows-test-"));
  const root = path.join(dir, "repo");
  const out = path.join(dir, "out");
  cpSync(FIXTURE, root, { recursive: true });
  git(root, "init", "-q");
  git(root, "-c", "user.name=t", "-c", "user.email=t@t", "add", "-A");
  git(root, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "fixture");
  return {
    dir,
    root,
    out,
    commit: git(root, "rev-parse", "--short", "HEAD"),
    cleanup: () => rmSync(dir, { recursive: true, force: true }),
  };
}

export function put(sb, rel, content) {
  const file = path.join(sb.root, rel);
  mkdirSync(path.dirname(file), { recursive: true });
  writeFileSync(file, content);
}

export function runFlows(args) {
  const res = spawnSync(process.execPath, [FLOWS_CLI, ...args], {
    cwd: REPO_ROOT,
    encoding: "utf8",
    timeout: 120000,
    windowsHide: true,
  });
  const stdout = res.stdout ?? "";
  const lines = stdout.split(/\r?\n/).filter((l) => l.trim() !== "");
  const last = lines.at(-1) ?? "";
  let result = null;
  if (last.startsWith("FLOWS_RESULT: ")) result = JSON.parse(last.slice("FLOWS_RESULT: ".length));
  return { status: res.status, stdout, stderr: res.stderr ?? "", output: stdout + (res.stderr ?? ""), result };
}

export function runFacts(sb, extra = []) {
  return runFlows(["facts", "--root", sb.root, "--out", sb.out, ...extra]);
}

export function runBuild(sb) {
  return runFlows(["build", "--root", sb.root, "--out", sb.out]);
}

export function readFacts(sb) {
  return JSON.parse(readFileSync(path.join(sb.out, ".work", "facts.json"), "utf8"));
}

export const descPath = (sb, id) => path.join(sb.out, ".work", "desc", `${id.replace(":", "-")}.json`);

export function descriptionFor(id, hash, extra = {}) {
  return {
    schema: 1,
    sources: hash,
    summary: `Podsumowanie ${id}`,
    steps: [
      { id: "s1", title: `Unikat-${id}-1`, detail: "pierwszy krok" },
      { id: "s2", title: `Unikat-${id}-2`, detail: "drugi krok", condition: "gdy trzeba", retries: 2, limit: "10 min" },
    ],
    ...extra,
  };
}

export function writeDescriptions(sb, { skip = [] } = {}) {
  const facts = readFacts(sb);
  mkdirSync(path.join(sb.out, ".work", "desc"), { recursive: true });
  for (const [id, node] of Object.entries(facts.nodes)) {
    if (skip.includes(id)) continue;
    writeFileSync(descPath(sb, id), JSON.stringify(descriptionFor(id, node.hash), null, 2));
  }
}

export function readyToBuild(sb, opts) {
  const facts = runFacts(sb);
  if (facts.status !== 0) throw new Error(facts.output);
  writeDescriptions(sb, opts);
}

export function snapshot(dir, { skip = [] } = {}) {
  const map = {};
  const walk = (current) => {
    for (const entry of readdirSync(current, { withFileTypes: true })) {
      const full = path.join(current, entry.name);
      const rel = path.relative(dir, full).split(path.sep).join("/");
      if (skip.some((s) => rel === s || rel.startsWith(`${s}/`))) continue;
      if (entry.isDirectory()) walk(full);
      else map[rel] = createHash("sha256").update(readFileSync(full)).digest("hex");
    }
  };
  if (existsSync(dir)) walk(dir);
  return map;
}

export const nodeIds = (facts) => Object.keys(facts.nodes).sort();

export const edgeCount = (facts, from, to) => facts.edges.filter((e) => e.from === from && e.to === to).length;

export const has = (node, text) => JSON.stringify(node).includes(text);

export const markerOf = (node, label) => (node.markers ?? []).find((m) => m.label === label);
