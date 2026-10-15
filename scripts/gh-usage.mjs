#!/usr/bin/env node
// Usage: node scripts/gh-usage.mjs [-- <command...>]
//   alone          print the current GraphQL and REST limit
//   with command   run it and print the GraphQL points and REST requests it consumed (or that the window reset meanwhile)
//   exit 4 when the GitHub limit is exhausted (reset time and a GITHUB_LIMIT line on stderr)

import { spawnSync } from "node:child_process";
import { GhLimitError, REPO_ROOT, exitOnGhLimit, rateLimit, usageDelta, usageLine } from "./gh.mjs";

const argv = process.argv.slice(2);
const split = argv.indexOf("--");
const command = split === -1 ? [] : argv.slice(split + 1);
if (split === -1 && argv.length) {
  console.error("usage: gh-usage.mjs [-- <command...>]");
  process.exit(1);
}

function readLimit() {
  try {
    return rateLimit();
  } catch (err) {
    if (err instanceof GhLimitError) exitOnGhLimit(err);
    throw err;
  }
}

const before = readLimit();
if (!command.length) {
  console.log(usageLine(before));
  process.exit(0);
}

const res = spawnSync(command[0], command.slice(1), { cwd: REPO_ROOT, stdio: "inherit", windowsHide: true, shell: false });
const after = readLimit();
const delta = usageDelta(before, after);
if (res.error) console.error(`${command.join(" ")}: ${res.error.message}`);
console.log(
  delta.reset
    ? "the rate-limit window reset during the run: no delta"
    : `GraphQL: ${delta.graphql} points, REST: ${delta.rest} requests`,
);
console.log(usageLine(after));
process.exit(res.status ?? 1);
