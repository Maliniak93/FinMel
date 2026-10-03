#!/usr/bin/env node
// Usage: node scripts/prune-branches.mjs [--apply]
//   --apply  delete the local branches whose PR was merged (dry run otherwise)

import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const KEEP = new Set(["master", "main"]);

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
    .filter((b) => !KEEP.has(b.name) && b.name !== current);

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
    // A commit after the merge is work the PR never carried, so the branch stays.
    if (new Date(b.lastCommit) > new Date(pr.mergedAt)) keep.push({ ...b, pr });
    else prune.push({ ...b, pr });
  }

  if (!prune.length && !keep.length) {
    console.log("nothing to prune — no local branch has a merged PR");
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
