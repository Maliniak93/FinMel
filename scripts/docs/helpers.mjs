import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { cpSync, existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
export const REPO_ROOT = path.resolve(HERE, "..", "..");
export const DOCS_CLI = path.join(REPO_ROOT, "scripts", "docs.mjs");
const FIXTURE = path.join(HERE, "fixtures", "mini");

function git(cwd, ...args) {
  const res = spawnSync("git", args, { cwd, encoding: "utf8", windowsHide: true });
  if (res.status !== 0) throw new Error(`git ${args.join(" ")} failed: ${res.stderr}`);
  return res.stdout.trim();
}

export function makeSandbox() {
  const dir = mkdtempSync(path.join(os.tmpdir(), "docs-test-"));
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

export function runDocs(args) {
  const res = spawnSync(process.execPath, [DOCS_CLI, ...args], {
    cwd: REPO_ROOT,
    encoding: "utf8",
    timeout: 120000,
    windowsHide: true,
  });
  const stdout = res.stdout ?? "";
  const lines = stdout.split(/\r?\n/).filter((l) => l.trim() !== "");
  const last = lines.at(-1) ?? "";
  let result = null;
  if (last.startsWith("DOCS_RESULT: ")) result = JSON.parse(last.slice("DOCS_RESULT: ".length));
  return { status: res.status, stdout, stderr: res.stderr ?? "", output: stdout + (res.stderr ?? ""), result };
}

export function runFacts(sb, extra = []) {
  return runDocs(["facts", "--root", sb.root, "--out", sb.out, ...extra]);
}

export function runBuild(sb) {
  return runDocs(["build", "--root", sb.root, "--out", sb.out]);
}

export function readFacts(sb) {
  return JSON.parse(readFileSync(path.join(sb.out, ".work", "facts.json"), "utf8"));
}

export function sequenceProse(count) {
  return Array.from(
    { length: count },
    (_, i) => `<pre class="mermaid">sequenceDiagram\n  Klient${i}->>Portfolio: kupno</pre>`,
  ).join("\n");
}

export function writeProse(sb) {
  const facts = readFacts(sb);
  const dir = path.join(sb.out, ".work", "prose");
  mkdirSync(dir, { recursive: true });
  for (const [slug, page] of Object.entries(facts.pages)) {
    const body = slug === "eventy" ? sequenceProse(4) : `<p>Opis strony ${slug}.</p>`;
    writeFileSync(path.join(dir, `${slug}.html`), `<!-- facts:${page.hash} -->\n${body}\n`);
  }
}

export function prosePath(sb, slug) {
  return path.join(sb.out, ".work", "prose", `${slug}.html`);
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

export function endpointsOf(service) {
  return [...service.slices.flatMap((s) => s.endpoints), ...service.inlineEndpoints]
    .map((e) => `${e.verb} ${e.path}`)
    .sort();
}

export function serviceOf(facts, name) {
  return facts.pages.architektura.facts.services.find((s) => s.name === name);
}

export function byJson(items) {
  return [...items].sort((a, b) => JSON.stringify(a).localeCompare(JSON.stringify(b)));
}
