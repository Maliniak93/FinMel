#!/usr/bin/env node
// Usage: node scripts/docs.mjs <verb> [--root <repo>] [--out <dir>]
//   facts [--all] [--page <slug>]   extract the code facts into <out>/.work/facts.json; lists the pages whose prose is missing or stale (all with --all); --page prints one page's facts; last line DOCS_RESULT: {json}
//   build                           render every page from the facts and its prose fragment in <out>/.work/prose/<slug>.html; all-or-nothing replace of the live guide in <out>

import { spawnSync } from "node:child_process";
import { copyFileSync, existsSync, mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { hashFacts, PAGES, proseStamp, renderPage } from "./docs/pages.mjs";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const MERMAID = path.join(REPO_ROOT, "scripts", "docs", "vendor", "mermaid.min.js");

function fail(message) {
  throw new Error(message);
}

function parseFlags(argv) {
  const flags = {};
  const positional = [];
  for (let i = 0; i < argv.length; i++) {
    if (argv[i].startsWith("--")) {
      const name = argv[i].slice(2);
      if (name === "all") flags[name] = true;
      else flags[name] = argv[++i];
    } else positional.push(argv[i]);
  }
  return { flags, positional };
}

function resolveTargets(flags) {
  const root = path.resolve(flags.root ?? REPO_ROOT);
  const out = path.resolve(flags.out ?? path.join(root, "skarbiec-plan", "przewodnik"));
  return { root, out };
}

function collect(root) {
  const pages = {};
  for (const page of PAGES) {
    const facts = page.extract(root);
    pages[page.slug] = { facts, hash: hashFacts(facts) };
  }
  return pages;
}

function readJson(file) {
  return existsSync(file) ? JSON.parse(readFileSync(file, "utf8")) : null;
}

function readProse(out, slug) {
  const file = path.join(out, ".work", "prose", `${slug}.html`);
  return existsSync(file) ? readFileSync(file, "utf8") : null;
}

function git(root, ...args) {
  const res = spawnSync("git", ["--no-optional-locks", ...args], { cwd: root, encoding: "utf8", windowsHide: true });
  return res.status === 0 ? res.stdout.trim() : null;
}

function facts(argv) {
  const { flags } = parseFlags(argv);
  const { root, out } = resolveTargets(flags);
  if (flags.page !== undefined && !PAGES.some((p) => p.slug === flags.page)) {
    fail(`unknown page "${flags.page}" (pages: ${PAGES.map((p) => p.slug).join(", ")})`);
  }

  const pages = collect(root);
  mkdirSync(path.join(out, ".work"), { recursive: true });
  writeFileSync(path.join(out, ".work", "facts.json"), `${JSON.stringify({ pages }, null, 2)}\n`);

  const manifest = readJson(path.join(out, "manifest.json"));
  const stale = PAGES.map((p) => p.slug).filter((slug) => {
    if (flags.all) return true;
    const current = pages[slug].hash;
    return manifest?.pages?.[slug] !== current || proseStamp(readProse(out, slug)) !== current;
  });

  if (flags.page !== undefined) console.log(JSON.stringify(pages[flags.page].facts, null, 2));
  console.log(stale.length ? `stale pages: ${stale.join(", ")}` : "no stale pages");
  console.log(`DOCS_RESULT: ${JSON.stringify({ ok: true, out, stale, pages: PAGES.map((p) => p.slug) })}`);
}

function build(argv) {
  const { flags } = parseFlags(argv);
  const { root, out } = resolveTargets(flags);

  const pages = collect(root);
  const problems = [];
  const prose = {};
  for (const page of PAGES) {
    const text = readProse(out, page.slug);
    if (text === null) problems.push(`page ${page.slug}: prose fragment missing (.work/prose/${page.slug}.html)`);
    else if (proseStamp(text) !== pages[page.slug].hash) {
      problems.push(`page ${page.slug}: prose fragment is stale (expected <!-- facts:${pages[page.slug].hash} -->)`);
    } else prose[page.slug] = text;
  }
  if (problems.length) fail(problems.join("\n"));

  const commit = git(root, "rev-parse", "--short", "HEAD") ?? "unknown";
  const dirty = Boolean(git(root, "status", "--porcelain"));
  const generatedAt = new Date().toISOString();
  const meta = { commit, dirty, timestamp: generatedAt.slice(0, 16).replace("T", " ") + " UTC" };

  const stage = path.join(out, ".work", "out");
  rmSync(stage, { recursive: true, force: true });
  mkdirSync(stage, { recursive: true });
  for (const page of PAGES) {
    writeFileSync(path.join(stage, `${page.slug}.html`), renderPage(page, pages[page.slug].facts, prose[page.slug], meta));
  }
  copyFileSync(MERMAID, path.join(stage, "mermaid.min.js"));
  const manifest = {
    generatedAt,
    commit,
    dirty,
    pages: Object.fromEntries(PAGES.map((p) => [p.slug, pages[p.slug].hash])),
  };
  writeFileSync(path.join(stage, "manifest.json"), `${JSON.stringify(manifest, null, 2)}\n`);

  const files = [...PAGES.map((p) => `${p.slug}.html`), "mermaid.min.js", "manifest.json"];
  for (const file of files) renameSync(path.join(stage, file), path.join(out, file));
  rmSync(stage, { recursive: true, force: true });

  console.log(`built ${PAGES.length} pages in ${out}`);
  console.log(`DOCS_RESULT: ${JSON.stringify({ ok: true, out, pages: PAGES.map((p) => p.slug), commit })}`);
}

const VERBS = { facts, build };

const [command, ...rest] = process.argv.slice(2);
try {
  const verb = VERBS[command];
  if (!verb) fail(`usage: docs.mjs ${Object.keys(VERBS).join(" | ")}`);
  verb(rest);
} catch (err) {
  console.error(err.message);
  process.exit(1);
}
