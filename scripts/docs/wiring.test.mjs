import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { test } from "node:test";
import { REPO_ROOT } from "./helpers.mjs";

const read = (...parts) => readFileSync(path.join(REPO_ROOT, ...parts), "utf8");

test("the generated guide folder is gitignored", () => {
  const res = spawnSync("git", ["check-ignore", "skarbiec-plan/przewodnik/index.html"], { cwd: REPO_ROOT, encoding: "utf8" });
  assert.equal(res.status, 0);
});

test("the docs skill declares sonnet and medium effort and is manual-only", () => {
  const skill = read(".claude", "skills", "docs", "SKILL.md");
  const front = /^---\r?\n([\s\S]*?)\r?\n---/.exec(skill)?.[1] ?? "";
  assert.match(front, /^model:\s*sonnet\s*$/m);
  assert.match(front, /^effort:\s*medium\s*$/m);
  assert.match(front, /^disable-model-invocation:\s*true\s*$/m);
});

test("the tracked domain.html export is gone and no tracked file references it", () => {
  assert.equal(existsSync(path.join(REPO_ROOT, "skarbiec-plan", "domain.html")), false);
  const refs = spawnSync(
    "git",
    ["grep", "-l", "-F", "domain.html", "--", ".", ":(exclude)scripts/docs/wiring.test.mjs"],
    { cwd: REPO_ROOT, encoding: "utf8" },
  );
  assert.equal(refs.stdout.trim(), "");
});

test("verify.mjs runs a scripts-test step and the check skill lists it", () => {
  assert.match(read("scripts", "verify.mjs"), /scripts-test/);
  assert.match(read(".claude", "skills", "check", "SKILL.md"), /scripts-test/);
});
