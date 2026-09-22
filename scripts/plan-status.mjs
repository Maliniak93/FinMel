#!/usr/bin/env node
// Live status of the plan: every spec in skarbiec-plan/specs/ against what git and GitHub actually
// say. Derived, never hand-written — the README's status table drifted three ways within two weeks
// of being written, because nothing recomputed it.
//
// Usage: node scripts/plan-status.mjs [--write] [--no-gh]
//
//   (no flags)   print the status block to stdout. This is what the SessionStart hook injects, so
//                every session starts from live facts even when the committed README lags.
//   --write      also replace the block between the `status:start` / `status:end` markers in
//                skarbiec-plan/README.md. Everything outside those markers is hand-written and is
//                never touched — "Open loops" is judgement, not data.
//   --no-gh      skip the GitHub query (open PRs). Implied when `gh` is missing or unauthenticated.
//
// Never mutates the repository: only `git` plumbing reads and one `gh pr list`. Exit code 0 in every
// normal case including a missing `gh` — a status report that fails a session start would be worse
// than a slightly thinner one. Exit 2 only when `--write` cannot find its markers, which is a real
// misconfiguration the user must fix.
//
// Node >= 22, ESM, zero npm dependencies. Resolves the repo root from this file's own location and
// gives every child process an explicit cwd, so it runs from anywhere (hooks run from wherever the
// session happens to be). `gh` gets a short timeout: a session start must not wait on the network.

import { readFileSync, writeFileSync, readdirSync, existsSync } from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const SPECS_DIR = path.join(REPO_ROOT, "skarbiec-plan", "specs");
const README = path.join(REPO_ROOT, "skarbiec-plan", "README.md");
const START = "<!-- status:start -->";
const END = "<!-- status:end -->";
const GH_TIMEOUT_MS = 8000;
const TRUNK = "master";

// ---------------------------------------------------------------------------------------------
// shell
// ---------------------------------------------------------------------------------------------

function run(bin, argv, timeout = 15000) {
  const res = spawnSync(bin, argv, {
    cwd: REPO_ROOT,
    encoding: "utf8",
    timeout,
    windowsHide: true,
    shell: false,
  });
  if (res.error || res.status !== 0) return null;
  return res.stdout.trim();
}

const git = (...argv) => run("git", argv);
const lines = (text) => (text ? text.split(/\r?\n/).map((l) => l.trim()).filter(Boolean) : []);

// ---------------------------------------------------------------------------------------------
// specs
// ---------------------------------------------------------------------------------------------

// Minimal frontmatter reader: `key: value` pairs between the first two `---` fences. Enough for the
// spec template's flat frontmatter — deliberately not a YAML parser.
function frontmatter(file) {
  const text = readFileSync(file, "utf8");
  const match = text.match(/^---\r?\n([\s\S]*?)\r?\n---/);
  if (!match) return {};

  const out = {};
  for (const line of match[1].split(/\r?\n/)) {
    const hit = line.match(/^([A-Za-z_][\w-]*):\s*(.*)$/);
    if (!hit) continue;
    out[hit[1]] = hit[2].trim().replace(/^["']|["']$/g, "");
  }
  return out;
}

function readSpecs() {
  if (!existsSync(SPECS_DIR)) return [];
  return readdirSync(SPECS_DIR)
    .filter((f) => f.endsWith(".md") && f !== "_template.md")
    .sort()
    .map((f) => {
      const fm = frontmatter(path.join(SPECS_DIR, f));
      return {
        slug: f.replace(/\.md$/, ""),
        title: fm.title || "",
        status: fm.status || "?",
        tier: fm.tier || "?",
        branch: fm.branch || "",
        skip: fm.skip && fm.skip !== "[]" ? fm.skip : "",
      };
    });
}

// ---------------------------------------------------------------------------------------------
// git / gh facts
// ---------------------------------------------------------------------------------------------

function repoFacts() {
  const head = git("rev-parse", "--abbrev-ref", "HEAD");
  const dirty = lines(git("status", "--porcelain"));
  const localBranches = new Set(lines(git("for-each-ref", "--format=%(refname:short)", "refs/heads")));
  const remoteBranches = new Set(
    lines(git("for-each-ref", "--format=%(refname:short)", "refs/remotes/origin")).map((b) =>
      b.replace(/^origin\//, ""),
    ),
  );
  const mergedLocal = new Set(lines(git("branch", "--merged", TRUNK, "--format=%(refname:short)")));
  const behindTrunk = git("rev-list", "--count", `${TRUNK}..origin/${TRUNK}`);

  return { head, dirty, localBranches, remoteBranches, mergedLocal, behindTrunk };
}

function openPullRequests(enabled) {
  if (!enabled) return null;
  const raw = run(
    process.platform === "win32" ? "gh.exe" : "gh",
    ["pr", "list", "--state", "open", "--limit", "30", "--json", "number,title,headRefName"],
    GH_TIMEOUT_MS,
  );
  if (!raw) return null;
  try {
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

// Where a spec's branch actually stands, in one phrase. Order matters: the most decisive fact wins.
function branchState(spec, facts, prs) {
  if (!spec.branch) return "no branch named in the spec";

  const local = facts.localBranches.has(spec.branch);
  const remote = facts.remoteBranches.has(spec.branch);
  if (!local && !remote) return spec.status === "done" ? "shipped, branch cleaned up" : "not started";

  const ref = local ? spec.branch : `origin/${spec.branch}`;
  const ahead = Number(git("rev-list", "--count", `${TRUNK}..${ref}`) ?? "0");
  const pr = (prs || []).find((p) => p.headRefName === spec.branch);

  if (pr) return `PR #${pr.number} open, ${ahead} commit(s)`;
  if (ahead === 0) return local && facts.mergedLocal.has(spec.branch) ? "merged, branch not deleted" : "branch cut, nothing committed yet";
  return `${ahead} commit(s) ${local && remote ? "pushed" : local ? "local only" : "on origin only"}, no open PR`;
}

// Lane branches, local or on origin, that belong to no spec and no open PR — the ones that quietly
// rot after their PR is merged. Spec branches are already covered by the table above.
function strayBranches(facts, prs, specs) {
  const owned = new Set(specs.map((s) => s.branch).filter(Boolean));
  const withPr = new Set((prs || []).map((p) => p.headRefName));
  const candidates = new Set([...facts.localBranches, ...facts.remoteBranches]);

  return [...candidates]
    .filter((b) => /^(feat|chore|fix)\//.test(b) && !owned.has(b) && !withPr.has(b))
    .sort()
    .map((b) => {
      const local = facts.localBranches.has(b);
      const where = local && facts.remoteBranches.has(b) ? "" : local ? ", local only" : ", origin only";
      const ahead = Number(git("rev-list", "--count", `${TRUNK}..${local ? b : `origin/${b}`}`) ?? "0");
      return `\`${b}\` (${ahead === 0 ? "merged" : `${ahead} commit(s) ahead`}${where})`;
    });
}

// ---------------------------------------------------------------------------------------------
// render
// ---------------------------------------------------------------------------------------------

function render(specs, facts, prs) {
  const out = [];
  out.push("<!-- Generated by `node scripts/plan-status.mjs --write`. Do not edit by hand. -->");
  out.push("");

  if (specs.length) {
    out.push("| Spec | Status | Tier | Where it stands |");
    out.push("|---|---|---|---|");
    for (const s of specs) {
      const status = s.skip ? `${s.status} · skip ${s.skip}` : s.status;
      out.push(`| \`${s.slug}\` | ${status} | ${s.tier} | ${branchState(s, facts, prs)} |`);
    }
  } else {
    out.push("No specs in `skarbiec-plan/specs/`.");
  }
  out.push("");

  const head = facts.head || "unknown";
  const dirt = facts.dirty.length ? `${facts.dirty.length} uncommitted file(s)` : "clean";
  const behind = facts.behindTrunk && facts.behindTrunk !== "0" ? `, ${facts.behindTrunk} behind \`origin/${TRUNK}\`` : "";
  out.push(`**Checkout:** \`${head}\`, ${dirt}${behind}.`);

  if (prs === null) {
    out.push("**Open PRs:** not checked (`gh` unavailable or skipped).");
  } else if (!prs.length) {
    out.push("**Open PRs:** none.");
  } else {
    out.push(`**Open PRs (${prs.length}):** ` + prs.map((p) => `#${p.number} ${p.title}`).join(" · "));
  }

  const strays = strayBranches(facts, prs, specs);
  if (strays.length) out.push(`**Lane branches with no spec and no PR:** ${strays.join(" · ")}`);

  return out.join("\n");
}

// ---------------------------------------------------------------------------------------------
// main
// ---------------------------------------------------------------------------------------------

const argv = process.argv.slice(2);
const wantWrite = argv.includes("--write");
const useGh = !argv.includes("--no-gh");

if (!git("rev-parse", "--is-inside-work-tree")) {
  process.stdout.write("plan-status: not a git repository — nothing to report.\n");
  process.exit(0);
}

const specs = readSpecs();
const facts = repoFacts();
const prs = openPullRequests(useGh);
const block = render(specs, facts, prs);

process.stdout.write(`# Plan status — live, from git and GitHub (skarbiec-plan/README.md may lag)\n\n${block}\n`);

if (!wantWrite) process.exit(0);

const readme = readFileSync(README, "utf8");
const from = readme.indexOf(START);
const to = readme.indexOf(END);

if (from === -1 || to === -1 || to < from) {
  process.stderr.write(
    `plan-status: ${path.relative(REPO_ROOT, README)} has no ${START} / ${END} markers — add them around the generated block first.\n`,
  );
  process.exit(2);
}

writeFileSync(README, `${readme.slice(0, from + START.length)}\n${block}\n${readme.slice(to)}`, "utf8");
process.stderr.write(`plan-status: rewrote the status block in ${path.relative(REPO_ROOT, README)}\n`);
