import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import path from "node:path";
import { test } from "node:test";
import { REPO_ROOT } from "./helpers.mjs";

test("verify.mjs scripts-test runs the flows tests", () => {
  const verify = readFileSync(path.join(REPO_ROOT, "scripts", "verify.mjs"), "utf8");
  const body = verify.slice(verify.indexOf("async function runScriptsTest"));
  assert.match(body.slice(0, body.indexOf("\n}\n")), /scripts\/flows\/\*\.test\.mjs/);
});

test("skarbiec-plan/flows/ is git-ignored", () => {
  const res = spawnSync("git", ["check-ignore", "-v", "skarbiec-plan/flows/index.html"], { cwd: REPO_ROOT, encoding: "utf8" });
  assert.equal(res.status, 0, res.stderr);
});

test("the flows skill is sonnet/medium and model-invocation is disabled", () => {
  const text = readFileSync(path.join(REPO_ROOT, ".claude", "skills", "flows", "SKILL.md"), "utf8");
  const front = text.match(/^---\r?\n([\s\S]*?)\r?\n---/)?.[1] ?? "";
  assert.match(front, /^model:\s*sonnet\s*$/m);
  assert.match(front, /^effort:\s*medium\s*$/m);
  assert.match(front, /^disable-model-invocation:\s*true\s*$/m);
});
