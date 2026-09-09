#!/usr/bin/env node
// PostToolUse(Edit|Write) hook: keeps web/ source files formatted without spending a model turn on
// it. Runs `prettier --write` on the just-touched file when it's a web/ source file prettier owns.
//
// This hook never makes a permission decision (PostToolUse can't deny a tool call that already ran)
// — it is a pure side effect. It always exits 0 and never writes to stdout; a prettier failure (a
// syntax error mid-edit, a missing binary, ...) is reported as one short stderr line and otherwise
// ignored, so a formatting hiccup never blocks the agent.
//
// Fast path: every non-matching file (not under web/, under an excluded subtree, or an extension
// prettier doesn't touch here) returns before any process is spawned.

import { readFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT =
  process.env.CLAUDE_PROJECT_DIR || path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..");
const WEB_DIR = path.join(REPO_ROOT, "web");
const NPX = process.platform === "win32" ? "npx.cmd" : "npx";

// Generated (hey-api) and build/dependency output — never hand-formatted, and reformatting them
// would fight the generator or waste time on files that get wiped on the next build anyway.
const EXCLUDED_PREFIXES = ["node_modules/", "dist/", "src/app/api/"];
const EXCLUDED_EXACT = new Set(["node_modules", "dist", "src/app/api"]);

const FORMATTABLE_EXTENSIONS = new Set([".ts", ".html", ".scss", ".css", ".json", ".md"]);

function isExcluded(webRelPosix) {
  return EXCLUDED_EXACT.has(webRelPosix) || EXCLUDED_PREFIXES.some((p) => webRelPosix.startsWith(p));
}

function quoteArg(a) {
  const s = String(a);
  if (s === "") return '""';
  if (/[\s"&|<>^%]/.test(s)) return `"${s.replace(/"/g, '\\"')}"`;
  return s;
}

function main() {
  const payload = JSON.parse(readFileSync(0, "utf8"));

  const filePath = payload?.tool_input?.file_path;
  if (typeof filePath !== "string" || !filePath) return;

  const absPath = path.resolve(REPO_ROOT, filePath);
  const webRel = path.relative(WEB_DIR, absPath);
  if (webRel.startsWith("..") || path.isAbsolute(webRel)) return; // not under web/

  const webRelPosix = webRel.split(path.sep).join("/");
  if (isExcluded(webRelPosix)) return;
  if (!FORMATTABLE_EXTENSIONS.has(path.extname(absPath).toLowerCase())) return;

  const cmdStr = `${NPX} prettier --write ${quoteArg(absPath)}`;
  const res = spawnSync(cmdStr, { cwd: WEB_DIR, shell: true, encoding: "utf8", timeout: 20_000 });

  if (res.error || res.status !== 0) {
    const raw = res.error ? res.error.message : res.stderr || res.stdout || `prettier exited ${res.status}`;
    const firstLine = String(raw).trim().split(/\r?\n/)[0] || "unknown error";
    process.stderr.write(`format-on-edit: ${webRelPosix}: ${firstLine}\n`);
  }
}

try {
  main();
} catch (err) {
  process.stderr.write(`format-on-edit: ${err?.message ?? String(err)}\n`);
}
process.exit(0);
