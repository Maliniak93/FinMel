import assert from "node:assert/strict";
import { readFileSync, readdirSync, statSync } from "node:fs";
import path from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const CLAUDE_MD_BUDGET = 11264;
const WORKFLOW = ".claude/workflows/build-feature.js";

const read = (rel) => readFileSync(path.join(root, rel), "utf8");
const filesUnder = (rel) =>
  readdirSync(path.join(root, rel)).flatMap((name) => {
    const child = path.join(rel, name);
    return statSync(path.join(root, child)).isDirectory() ? filesUnder(child) : [child];
  });

const MODEL_ID = /\bclaude-(?:fable|opus|sonnet|haiku)-\d+(?:-\d+)*\b/g;
const PAIR = /\b(?:haiku|sonnet|opus|fable)\/(?:low|medium|high|xhigh|max)\b/gi;

function levelsBlock(source) {
  const start = source.indexOf("const LEVELS = {");
  assert.notEqual(start, -1, `${WORKFLOW} has no LEVELS object`);
  const end = source.indexOf("\n}\n", start);
  assert.notEqual(end, -1, `${WORKFLOW}: LEVELS is not closed`);
  return { start, end: end + 3 };
}

test("one pinned model id across agents, skills, workflows and autobuild", () => {
  const files = [...filesUnder(".claude/agents"), ...filesUnder(".claude/skills"), ...filesUnder(".claude/workflows"), "scripts/autobuild.mjs"];
  const seen = new Map();
  for (const file of files) for (const id of read(file).match(MODEL_ID) || []) seen.set(id, [...(seen.get(id) || []), file]);
  assert.equal(seen.size, 1, `expected one model id, found: ${[...seen].map(([id, where]) => `${id} in ${[...new Set(where)].join(", ")}`).join("; ")}`);
});

test("no model/effort pair outside the LEVELS object", () => {
  const docs = ["CLAUDE.md", "skarbiec-plan/workflow.md", ...filesUnder(".claude/agents").filter((f) => f.endsWith(".md"))];
  docs.push(...readdirSync(path.join(root, ".claude/skills")).map((name) => path.join(".claude/skills", name, "SKILL.md")).filter((f) => statSync(path.join(root, f), { throwIfNoEntry: false })));
  const hits = docs.flatMap((file) => (read(file).match(PAIR) || []).map((pair) => `${file}: ${pair}`));
  const source = read(WORKFLOW);
  const { start, end } = levelsBlock(source);
  hits.push(...((source.slice(0, start) + source.slice(end)).match(PAIR) || []).map((pair) => `${WORKFLOW}: ${pair}`));
  assert.deepEqual(hits, []);
});

test("CLAUDE.md stays within its byte budget", () => {
  const size = Buffer.byteLength(read("CLAUDE.md"), "utf8");
  assert.ok(size <= CLAUDE_MD_BUDGET, `CLAUDE.md is ${size} bytes, budget ${CLAUDE_MD_BUDGET}`);
});
