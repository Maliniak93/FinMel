#!/usr/bin/env node
// Usage: node scripts/flows.mjs <verb> [--root <repo>] [--out <dir>]
//   facts [--all] [--node <id>]   extract the workflow facts into <out>/.work/facts.json; lists the nodes whose description (<out>/.work/desc/<id>.json) is missing or stale (all with --all); --node prints one node's facts and source files; last line FLOWS_RESULT: {json}
//   build                         validate every description and render the offline map and node cards; all-or-nothing replace of <out>/index.html and <out>/manifest.json

import { spawnSync } from "node:child_process";
import { mkdirSync, renameSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { descDir, DESC_SCHEMA_VERSION, isStale, readDescription, removeOrphans, validateDescription } from "./flows/describe.mjs";
import { collect, ExtractError } from "./flows/extract.mjs";
import { renderPage } from "./flows/render.mjs";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

function fail(message) {
  throw new ExtractError(message);
}

function parseFlags(argv) {
  const flags = {};
  for (let i = 0; i < argv.length; i++) {
    if (!argv[i].startsWith("--")) continue;
    const name = argv[i].slice(2);
    if (name === "all") flags[name] = true;
    else flags[name] = argv[++i];
  }
  return flags;
}

function resolveTargets(flags) {
  const root = path.resolve(flags.root ?? REPO_ROOT);
  const out = path.resolve(flags.out ?? path.join(root, "skarbiec-plan", "flows"));
  return { root, out };
}

function git(root, ...args) {
  const res = spawnSync("git", ["--no-optional-locks", ...args], { cwd: root, encoding: "utf8", windowsHide: true });
  return res.status === 0 ? res.stdout.trim() : null;
}

function facts(argv) {
  const flags = parseFlags(argv);
  const { root, out } = resolveTargets(flags);
  const found = collect(root);
  if (flags.node !== undefined && !(flags.node in found.nodes)) {
    fail(`unknown node "${flags.node}" (nodes: ${Object.keys(found.nodes).join(", ")})`);
  }
  mkdirSync(path.join(out, ".work"), { recursive: true });
  writeFileSync(path.join(out, ".work", "facts.json"), `${JSON.stringify({ schema: DESC_SCHEMA_VERSION, ...found }, null, 2)}\n`);

  const stale = Object.keys(found.nodes).filter((id) => flags.all || isStale(out, id, found.nodes[id]));
  if (flags.node !== undefined) {
    const calls = (key, other) => found.edges.filter((e) => e[key] === flags.node).map((e) => e[other]);
    const node = found.nodes[flags.node];
    console.log(JSON.stringify({ id: flags.node, ...node, callers: calls("to", "from"), callees: calls("from", "to") }, null, 2));
  }
  console.log(stale.length ? `stale nodes: ${stale.join(", ")}` : "no stale nodes");
  console.log(`FLOWS_RESULT: ${JSON.stringify({ ok: true, out, stale, nodes: Object.keys(found.nodes) })}`);
}

function build(argv) {
  const flags = parseFlags(argv);
  const { root, out } = resolveTargets(flags);
  const found = collect(root);

  const problems = [];
  const descriptions = {};
  for (const id of Object.keys(found.nodes)) {
    const description = readDescription(out, id);
    if (!description.exists) continue;
    const problem = description.error ?? validateDescription(description.value, found.nodes);
    if (problem) problems.push(`node ${id} (${path.relative(root, description.file) || description.file}): ${problem}`);
    else descriptions[id] = description.value;
  }
  if (problems.length) fail(problems.join("\n"));

  const stale = new Set(Object.keys(found.nodes).filter((id) => isStale(out, id, found.nodes[id])));
  const commit = git(root, "rev-parse", "--short", "HEAD") ?? "unknown";
  const dirty = Boolean(git(root, "status", "--porcelain"));
  const generatedAt = new Date().toISOString();
  const meta = { commit, dirty, timestamp: `${generatedAt.slice(0, 16).replace("T", " ")} UTC` };

  const stage = path.join(out, ".work", "out");
  rmSync(stage, { recursive: true, force: true });
  mkdirSync(stage, { recursive: true });
  writeFileSync(path.join(stage, "index.html"), renderPage(found, descriptions, stale, meta));
  const manifest = {
    generatedAt,
    commit,
    dirty,
    nodes: Object.fromEntries(Object.entries(found.nodes).map(([id, node]) => [id, node.hash])),
    stale: [...stale],
  };
  writeFileSync(path.join(stage, "manifest.json"), `${JSON.stringify(manifest, null, 2)}\n`);

  const removed = removeOrphans(out, found.nodes);
  for (const file of ["index.html", "manifest.json"]) renameSync(path.join(stage, file), path.join(out, file));
  rmSync(stage, { recursive: true, force: true });

  console.log(`built ${Object.keys(found.nodes).length} nodes in ${out} (${stale.size} without a current description)`);
  console.log(`FLOWS_RESULT: ${JSON.stringify({ ok: true, out, stale: [...stale], removed, descriptions: descDir(out) })}`);
}

const VERBS = { facts, build };

const [command, ...rest] = process.argv.slice(2);
try {
  if (!VERBS[command]) fail(`usage: node scripts/flows.mjs <${Object.keys(VERBS).join("|")}> [--root <repo>] [--out <dir>]`);
  VERBS[command](rest);
} catch (error) {
  if (!(error instanceof ExtractError)) throw error;
  console.error(`flows ${command ?? ""}: ${error.message}`);
  console.log(`FLOWS_RESULT: ${JSON.stringify({ ok: false, error: error.message })}`);
  process.exit(1);
}
