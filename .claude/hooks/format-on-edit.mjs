#!/usr/bin/env node

import { readFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT =
  process.env.CLAUDE_PROJECT_DIR || path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..");
const WEB_DIR = path.join(REPO_ROOT, "web");
const NPX = process.platform === "win32" ? "npx.cmd" : "npx";

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
  if (webRel.startsWith("..") || path.isAbsolute(webRel)) return;

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
