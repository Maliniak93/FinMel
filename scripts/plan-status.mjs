#!/usr/bin/env node
// Usage: node scripts/plan-status.mjs [--write] [--no-gh]
//   --write  also replace the status:start/status:end block in skarbiec-plan/README.md
//   --no-gh  skip the GitHub queries and report git alone

import { readFileSync, writeFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const GH_PROJECT = path.join(REPO_ROOT, "scripts", "gh-project.mjs");
const README = path.join(REPO_ROOT, "skarbiec-plan", "README.md");
const START = "<!-- status:start -->";
const END = "<!-- status:end -->";
const GH_TIMEOUT_MS = 8000;
const PROJECT_TIMEOUT_MS = 20000;
const TRUNK = "master";

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

function readSpecs(enabled) {
  if (!enabled) return null;
  const raw = run(process.execPath, [GH_PROJECT, "list"], PROJECT_TIMEOUT_MS);
  if (!raw) return null;
  try {
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

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

function pullRequests(enabled) {
  if (!enabled) return null;
  const raw = run(
    process.platform === "win32" ? "gh.exe" : "gh",
    ["pr", "list", "--state", "all", "--limit", "60", "--json", "number,title,headRefName,state"],
    GH_TIMEOUT_MS,
  );
  if (!raw) return null;
  try {
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

function nextUp(specs) {
  const byNumber = new Map(specs.map((s) => [s.number, s]));
  const inEpic = new Set(specs.flatMap((s) => (s.epic ? s.subIssues || [] : [])));
  const ready = [];
  for (const epic of specs.filter((s) => s.epic && s.status !== "Done")) {
    const first = (epic.subIssues || []).map((n) => byNumber.get(n)).find((s) => s && s.status !== "Done");
    if (first?.status === "Todo") ready.push({ ...first, via: `epic #${epic.number}` });
  }
  const standalone = specs
    .filter((s) => !s.epic && s.status === "Todo" && !inEpic.has(s.number))
    .sort((a, b) => Number(a.tier ?? 9) - Number(b.tier ?? 9));
  return [...ready, ...standalone];
}

function mismatches(specs, facts, prs) {
  const merged = new Set((prs || []).filter((p) => p.state === "MERGED").map((p) => p.headRefName));
  const out = [];
  for (const s of specs.filter((x) => !x.epic && x.branch && x.status !== "Done")) {
    const exists = facts.localBranches.has(s.branch) || facts.remoteBranches.has(s.branch);
    if (merged.has(s.branch)) out.push(`#${s.number}: PR from \`${s.branch}\` merged but the issue is open (branch not linked to the issue) — close it`);
    else if (s.status === "Todo" && exists) out.push(`#${s.number}: card is Todo but \`${s.branch}\` exists — a run started without moving the card`);
  }
  // GitHub never closes a parent issue on its own, so an epic outlives its last merged part.
  const byNumber = new Map(specs.map((s) => [s.number, s]));
  for (const e of specs.filter((x) => x.epic && x.status !== "Done" && x.subIssues?.length)) {
    if (e.subIssues.every((n) => byNumber.get(n)?.status === "Done")) out.push(`#${e.number}: every part is done — close the epic (\`gh issue close ${e.number}\`)`);
  }
  return out;
}

// The most decisive fact wins, so the order matters.
function branchState(spec, facts, prs) {
  if (spec.epic) return `epic of ${(spec.subIssues || []).map((n) => `#${n}`).join(", ") || "no sub-issues yet"}`;
  if (!spec.branch) return "no Branch field on the card";

  const local = facts.localBranches.has(spec.branch);
  const remote = facts.remoteBranches.has(spec.branch);
  if (!local && !remote) return "not started";

  const ref = local ? spec.branch : `origin/${spec.branch}`;
  const ahead = Number(git("rev-list", "--count", `${TRUNK}..${ref}`) ?? "0");
  const pr = (prs || []).find((p) => p.headRefName === spec.branch);

  if (pr) return `PR #${pr.number} open, ${ahead} commit(s)`;
  if (ahead === 0) return local && facts.mergedLocal.has(spec.branch) ? "merged, branch not deleted" : "branch cut, nothing committed yet";
  return `${ahead} commit(s) ${local && remote ? "pushed" : local ? "local only" : "on origin only"}, no open PR`;
}

function strayBranches(facts, prs, specs) {
  const owned = new Set((specs || []).map((s) => s.branch).filter(Boolean));
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

function render(specs, facts, allPrs) {
  const prs = allPrs && allPrs.filter((p) => p.state === "OPEN");
  const out = [];
  out.push("<!-- Generated by `node scripts/plan-status.mjs --write`. Do not edit by hand. -->");
  out.push("");

  const open = (specs || []).filter((s) => s.status !== "Done");
  if (specs === null) {
    out.push("**Spec issues:** not checked (`gh` unavailable or skipped).");
  } else if (open.length) {
    out.push("| Issue | Title | Status | Tier · Kind | Where it stands |");
    out.push("|---|---|---|---|---|");
    for (const s of open) {
      const kind = [s.tier ?? "?", s.kind ?? (s.epic ? "Epic" : "?"), s.skipTests && "skip-tests"].filter(Boolean).join(" · ");
      out.push(`| #${s.number} | ${s.title.replace(/\|/g, "\\|")} | ${s.status ?? "?"} | ${kind} | ${branchState(s, facts, prs)} |`);
    }
  } else {
    out.push("No open spec issues on the project.");
  }
  if (specs?.length > open.length) out.push(`\n${specs.length - open.length} spec issue(s) done.`);
  out.push("");

  if (specs) {
    const next = nextUp(specs);
    out.push(
      next.length
        ? `**Next up:** ${next.map((s) => `\`/build #${s.number}\` ${s.title} (tier ${s.tier ?? "?"}${s.via ? `, ${s.via}` : ""})`).join(" · ")}`
        : "**Next up:** nothing in Todo — `/design` something.",
    );
    const wrong = mismatches(specs, facts, allPrs);
    if (wrong.length) out.push(`**Mismatches:** ${wrong.join(" · ")}`);
    out.push("");
  }

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
  if (specs && strays.length) out.push(`**Lane branches with no spec issue and no PR:** ${strays.join(" · ")}`);

  return out.join("\n");
}

const argv = process.argv.slice(2);
const wantWrite = argv.includes("--write");
const useGh = !argv.includes("--no-gh");

if (!git("rev-parse", "--is-inside-work-tree")) {
  process.stdout.write("plan-status: not a git repository — nothing to report.\n");
  process.exit(0);
}

const specs = readSpecs(useGh);
const facts = repoFacts();
const prs = pullRequests(useGh);
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
