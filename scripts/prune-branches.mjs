#!/usr/bin/env node
// Deletes local lane branches (feat/*, fix/*, chore/*) whose pull request has been merged. PRs are
// squash-merged, so git never sees those branches as merged (`git branch -d` refuses them) and they
// pile up — and every session start lists them as "lane branches with no spec issue and no PR".
//
// Usage: node scripts/prune-branches.mjs [--apply]
//
//   (default)  dry run: print the branches that would go, with their PR number, and change nothing
//   --apply    delete them locally with `git branch -D` (never the current branch, never a branch
//              whose merged PR is older than a commit made on it since — that work would be lost)
//
// Remote branches are left alone (GitHub deletes them on merge when that setting is on).
// Run by the user or by the ops agent; git-guard asks before `git branch -D` from an agent.
//
// Node >= 22, ESM, zero npm dependencies; needs `gh` authenticated.

import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const LANE = /^(feat|fix|chore)\//;

function run(bin, argv) {
  const res = spawnSync(bin, argv, { cwd: REPO_ROOT, encoding: "utf8", timeout: 60_000 });
  if (res.error || res.status !== 0) {
    throw new Error(`${bin} ${argv.join(" ")} failed: ${(res.stderr || res.error?.message || "").trim()}`);
  }
  return res.stdout.trim();
}

function main() {
  const apply = process.argv.includes("--apply");
  const unknown = process.argv.slice(2).filter((a) => a !== "--apply");
  if (unknown.length) throw new Error(`unknown argument(s): ${unknown.join(" ")} — usage: prune-branches.mjs [--apply]`);

  const current = run("git", ["rev-parse", "--abbrev-ref", "HEAD"]);
  const local = run("git", ["for-each-ref", "--format=%(refname:short) %(committerdate:iso-strict)", "refs/heads"])
    .split(/\r?\n/)
    .filter(Boolean)
    .map((l) => {
      const [name, date] = l.split(" ");
      return { name, lastCommit: date };
    })
    .filter((b) => LANE.test(b.name) && b.name !== current);

  const merged = new Map(
    JSON.parse(run("gh", ["pr", "list", "--state", "merged", "--limit", "500", "--json", "number,headRefName,mergedAt"])).map((pr) => [
      pr.headRefName,
      pr,
    ]),
  );

  const prune = [];
  const keep = [];
  for (const b of local) {
    const pr = merged.get(b.name);
    if (!pr) continue;
    // A commit after the merge is work the PR never carried — keep the branch.
    if (new Date(b.lastCommit) > new Date(pr.mergedAt)) keep.push({ ...b, pr });
    else prune.push({ ...b, pr });
  }

  if (!prune.length && !keep.length) {
    console.log("nothing to prune — no local lane branch has a merged PR");
    return;
  }
  for (const b of keep) console.log(`keep    ${b.name} — committed after PR #${b.pr.number} merged`);
  for (const b of prune) {
    if (apply) run("git", ["branch", "-D", b.name]);
    console.log(`${apply ? "deleted" : "would delete"}  ${b.name} — PR #${b.pr.number} merged ${b.pr.mergedAt.slice(0, 10)}`);
  }
  if (!apply) console.log(`\n${prune.length} branch(es) to prune — re-run with --apply to delete them`);
}

try {
  main();
} catch (err) {
  console.error(`prune-branches.mjs: ${err.message}`);
  process.exit(1);
}
