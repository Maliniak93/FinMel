#!/usr/bin/env node
// Usage: node scripts/beta.mjs [--quick] [--out <dir>]
//   --quick      skip the slow part
//   --out <dir>  write the result there

import { spawnSync } from "node:child_process";

const DEFAULT_TIMEOUT_MS = 30 * 1000;
const MAX_RETRIES = 3;

spawnSync("git", ["commit", "-m", "fixture"]);
console.log(DEFAULT_TIMEOUT_MS, MAX_RETRIES);
