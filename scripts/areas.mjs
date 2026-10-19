#!/usr/bin/env node
// Usage: node scripts/areas.mjs [of <path…> | outside <Area> [path…]]   no argument prints the area -> paths table; `of` prints the area of the given files (— when none)
//   outside prints the paths outside the ticket Area, one per line (no paths: the uncommitted change incl. untracked files)

import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

export const AREAS = ["Backend", "Frontend", "E2E", "Claude", "Scripts", "CI/Infra", "Docs", "Fullstack"];

const PATH_MAP = [
  {
    area: "Backend",
    paths: ["services/", "gateway/", "contracts/", "Skarbiec.ServiceDefaults*", "Skarbiec.Testing*", "requests/"],
  },
  { area: "Frontend", paths: ["web/"] },
  { area: "E2E", paths: ["e2e/"] },
  { area: "Claude", paths: [".claude/", "CLAUDE.md", ".mcp.json"] },
  { area: "Scripts", paths: ["scripts/"] },
  {
    area: "CI/Infra",
    paths: [
      ".github/",
      "deploy/",
      "Skarbiec.AppHost/",
      "Directory.*.props",
      "Skarbiec.slnx",
      ".editorconfig",
      ".gitattributes",
      ".gitignore",
      ".dockerignore",
      "compose*.yml",
      "compose*.yaml",
      "docker-compose*.yml",
      "docker-compose*.yaml",
    ],
  },
  { area: "Docs", paths: ["skarbiec-plan/", "README.md", "*.md"] },
];

const playbook = (name) => `.claude/skills/${name}/SKILL.md`;
const CHECKLIST_SECTIONS = ["Common", "Backend", "Frontend", "Scripts", "Claude", "CI/Infra", "Docs", "E2E"];
const PROFILES = {
  Backend: { tests: true, implementer: ["backend-playbook"], testWriter: ["testing-playbook"], checklist: ["Backend"] },
  Frontend: { tests: true, implementer: ["frontend-playbook"], testWriter: ["frontend-playbook"], checklist: ["Frontend"] },
  Fullstack: {
    tests: true,
    implementer: ["backend-playbook", "frontend-playbook"],
    testWriter: ["testing-playbook", "frontend-playbook"],
    checklist: ["Backend", "Frontend"],
  },
  Scripts: { tests: false, implementer: ["scripts-playbook"], testWriter: [], checklist: ["Scripts"] },
  Claude: { tests: false, implementer: ["claude-playbook"], testWriter: [], checklist: ["Claude"] },
  "CI/Infra": { tests: false, implementer: [], testWriter: [], checklist: ["CI/Infra"] },
  Docs: { tests: false, implementer: [], testWriter: [], checklist: ["Docs"] },
  E2E: { tests: false, implementer: [], testWriter: [], checklist: ["E2E"] },
};
const NO_AREA_LABEL = "Brak obszaru — pełna ścieżka";

export function profileOf(area, { skip = [], e2e = false } = {}) {
  const known = Object.hasOwn(PROFILES, area ?? "");
  const entry = known ? PROFILES[area] : { tests: true, implementer: ["backend-playbook", "frontend-playbook"], testWriter: ["testing-playbook"], checklist: CHECKLIST_SECTIONS.slice(1) };
  const tests = entry.tests && !e2e && !skip.includes("tests");
  const review = !skip.includes("review");
  const phases = [...(e2e ? ["e2e"] : tests ? ["test-writer", "implementer"] : ["implementer"]), ...(review ? ["reviewer"] : [])];
  return {
    area: known ? area : null,
    known,
    tests,
    e2e,
    review,
    testWriterPlaybooks: tests ? entry.testWriter.map(playbook) : [],
    implementerPlaybooks: e2e ? [] : entry.implementer.map(playbook),
    checklist: ["Common", ...(e2e ? ["E2E"] : entry.checklist)],
    label: known ? `${area} (${phases.join(" → ")})` : NO_AREA_LABEL,
  };
}

const COMPANIONS =[/^web\/src\/app\/api\//, /^requests\//, /^Skarbiec\.AppHost\//];
const PRIMARY = new Set(["Backend", "Frontend", "E2E", "Scripts", "CI/Infra"]);
const SECONDARY = new Set(["Claude", "Docs"]);

function patternOf(pattern) {
  const escaped = pattern
    .replace(/[.+?^${}()|[\]\\]/g, "\\$&")
    .replace(/\*$/, "\u0000")
    .replaceAll("*", "[^/]*")
    .replace("\u0000", ".*");
  return new RegExp(pattern.endsWith("/") ? `^${escaped}` : `^${escaped}$`);
}

const RULES = PATH_MAP.flatMap(({ area, paths }) => paths.map((p) => ({ area, regex: patternOf(p) })));
const README = /(^|\/)README\.md$/;

export function areaOfPath(file) {
  const p = file.replaceAll("\\", "/").replace(/^\.\//, "");
  if (p.startsWith("deploy/")) return "CI/Infra";
  if (README.test(p)) return "Docs";
  return RULES.find((r) => r.regex.test(p))?.area ?? null;
}

export function parseArea(value) {
  const wanted = String(value ?? "").toLowerCase();
  return AREAS.find((a) => a.toLowerCase() === wanted) ?? null;
}

function pick(files, allowed) {
  const counts = new Map();
  for (const file of files) {
    const area = areaOfPath(file);
    if (area && (!allowed || allowed.has(area))) counts.set(area, (counts.get(area) ?? 0) + 1);
  }
  if (counts.size === 0) return null;
  if (counts.has("Backend") && counts.has("Frontend")) return "Fullstack";
  return [...counts].sort((a, b) => b[1] - a[1] || AREAS.indexOf(a[0]) - AREAS.indexOf(b[0]))[0][0];
}

export function areaOfFiles(paths) {
  const files = paths.map((p) => p.replaceAll("\\", "/"));
  const isCompanion = (f) => COMPANIONS.some((c) => c.test(f));
  const own = files.filter((f) => !isCompanion(f));
  return pick(own, PRIMARY) ?? pick(own, SECONDARY) ?? pick(files.filter(isCompanion));
}

export function outsideArea(area, paths) {
  const allowed = area === "Fullstack" ? new Set(["Backend", "Frontend"]) : new Set([area]);
  return paths.filter((f) => {
    const p = f.replaceAll("\\", "/").replace(/^\.\//, "");
    return !COMPANIONS.some((c) => c.test(p)) && !allowed.has(areaOfPath(p));
  });
}

function uncommittedFiles() {
  const res = spawnSync("git", ["status", "--porcelain", "-uall"], { encoding: "utf8", windowsHide: true, maxBuffer: 64 * 1024 * 1024 });
  if (res.error || res.status !== 0) throw new Error(res.error?.message ?? res.stderr.trim());
  return res.stdout
    .split(/\r?\n/)
    .filter(Boolean)
    .map((l) => l.slice(3).split(" -> ").pop().replace(/^"|"$/g, ""));
}

if (process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1])) {
  const [verb, ...rest] = process.argv.slice(2);
  if (verb === "of") console.log(areaOfFiles(rest) ?? "—");
  else if (verb === "outside") {
    const [area, ...paths] = rest;
    for (const f of outsideArea(area, paths.length ? paths : uncommittedFiles())) console.log(f);
  } else
    for (const { area, paths } of PATH_MAP) console.log(`${area.padEnd(9)} ${paths.join(" ")}`);
}
