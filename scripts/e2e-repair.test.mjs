import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";
import { E2E_APP_BLOCKED, E2E_DIAGNOSIS, blockAfterBuild, ciFixOutcome, e2eBlockLines, failedTests, loadRepairState, newestDiagnosis, noPrReason, parseFixResult, rebuildInputs, recordRepair, repairBudget, repairOutcome, saveRepairState } from "./e2e-repair.mjs";

const SELF_CHECK = "rewrote the total with expectProjected (30 s): still 0 PLN after the re-fetch";

const blocked = {
  status: "blocked",
  stage: "e2e",
  class: "app",
  failed: [{ test: "dashboard shows the PLN total", step: "expect the total", trace: "e2e/results/trace.zip" }],
  reportPath: "e2e/results/report.json",
  patch: "skarbiec-plan/patches/331-e2e.patch",
};

const BLOCK_LINES = [
  "- test `dashboard shows the PLN total` — expect the total — trace: `e2e/results/trace.zip`",
  "- report: `e2e/results/report.json`",
  "- patch: `skarbiec-plan/patches/331-e2e.patch`",
];

test("e2e block lines include the self-check", () => {
  const lines = e2eBlockLines({ ...blocked, selfCheck: SELF_CHECK });
  assert.deepEqual(lines.filter((l) => !l.startsWith("- self-check: ")), BLOCK_LINES);
  assert.deepEqual(lines.filter((l) => l.startsWith("- self-check: ")), [`- self-check: ${SELF_CHECK}`]);
});

test("e2e block lines omit a missing self-check", () => {
  assert.deepEqual(e2eBlockLines(blocked), BLOCK_LINES);
});

const c = (body) => ({ body });
const DIAGNOSIS = `${E2E_DIAGNOSIS} — dashboard shows the PLN total, Krok 3\nthe test does not re-fetch`;

test("parses the last FIX_RESULT line", () => {
  const text = ["log", 'FIX_RESULT: {"ok":true,"kind":"plan","summary":"old"}', "more", 'FIX_RESULT: {"ok":true,"kind":"test","summary":"waits wrongly"}', ""].join("\n");
  assert.deepEqual(parseFixResult(text), { ok: true, kind: "test", summary: "waits wrongly" });
  assert.equal(parseFixResult("no result here"), null);
  assert.equal(parseFixResult("FIX_RESULT: {broken"), null);
});

test("parses and judges a ci fix result", () => {
  const ok = parseFixResult('log\nFIX_RESULT: {"ok":true,"kind":"ci","summary":"fixed the build"}\n');
  assert.deepEqual(ok, { ok: true, kind: "ci", summary: "fixed the build" });
  assert.deepEqual(ciFixOutcome(ok), { ok: true, summary: "fixed the build" });
  assert.equal(ciFixOutcome(parseFixResult('FIX_RESULT: {"ok":false,"kind":"stop","summary":"no reproduction"}')).ok, false);
  assert.match(ciFixOutcome({ ok: false, kind: "stop", summary: "no reproduction" }).reason, /no reproduction/);
  assert.match(ciFixOutcome(null).reason, /no FIX_RESULT/);
  assert.match(ciFixOutcome({ ok: true, kind: "plan", summary: "x" }).reason, /not "ci"/);
  assert.equal(repairOutcome(ok, { part: 332, nextOpen: 332 }).action, "stop");
});

test("test result rebuilds the e2e part", () => {
  const r = repairOutcome({ ok: true, kind: "test", summary: "s" }, { part: 332, nextOpen: 332 });
  assert.equal(r.action, "rebuild");
  assert.equal(typeof r.reason, "string");
});

test("unknown or failed result stops", () => {
  const ctx = { part: 332, nextOpen: 332 };
  assert.equal(repairOutcome({ ok: false, summary: "x" }, ctx).action, "stop");
  assert.equal(repairOutcome(null, ctx).action, "stop");
  assert.equal(repairOutcome({ ok: true, kind: "weird", summary: "x" }, ctx).action, "stop");
  assert.equal(repairOutcome({ ok: true, kind: "test", summary: "x" }, { part: 332, nextOpen: 400 }).action, "stop");
});

test("app and plan outcomes unchanged", () => {
  assert.equal(repairOutcome({ ok: true, kind: "app", issues: [400, 401], summary: "x" }, { part: 332, nextOpen: 400 }).action, "next");
  assert.equal(repairOutcome({ ok: true, kind: "app", issues: [400], summary: "x" }, { part: 332, nextOpen: 332 }).action, "stop");
  assert.equal(repairOutcome({ ok: true, kind: "app", summary: "x" }, { part: 332, nextOpen: 400 }).action, "stop");
  assert.equal(repairOutcome({ ok: true, kind: "plan", summary: "x" }, { part: 332, nextOpen: 332 }).action, "rebuild");
});

const OLD_BLOCK = `${E2E_APP_BLOCKED} — x\n- test \`b test\` — Krok 2\n- test \`a test\` — Krok 1\n- report: \`r.json\``;

test("failing test names are parsed from a block comment", () => {
  assert.deepEqual(failedTests(OLD_BLOCK), ["a test", "b test"]);
  assert.deepEqual(failedTests("nothing"), []);
});

test("a build that posted no comment since its start did not start", () => {
  assert.equal(blockAfterBuild({ pr: null, commented: false, block: { cls: "app" } }), null);
  assert.deepEqual(blockAfterBuild({ pr: null, commented: true, block: { cls: "app" } }), { cls: "app" });
  assert.equal(blockAfterBuild({ pr: { number: 1 }, commented: true, block: { cls: "app" } }), null);
  assert.match(noPrReason({ part: 560, commented: false, code: 0 }), /did not start/);
  assert.doesNotMatch(noPrReason({ part: 560, commented: true, code: 0 }), /did not start/);
});

test("the repair budget survives a reload and a merged fix, and refuses the same failure", () => {
  const dir = mkdtempSync(path.join(os.tmpdir(), "repairs-"));
  const file = path.join(dir, "e2e-repairs.json");
  try {
    const tests = ["a test"];
    assert.equal(repairBudget(loadRepairState(file), { part: 560, tests, sha: "s1", max: 3 }).ok, true);
    let state = recordRepair({}, { part: 560, tests, sha: "s1", fixes: [564] });
    saveRepairState(file, state);
    state = loadRepairState(file);
    assert.equal(state[560].count, 1);
    assert.deepEqual(state[560].fixes, [564]);

    const same = repairBudget(state, { part: 560, tests, sha: "s1", max: 3 });
    assert.equal(same.ok, false);
    assert.match(same.reason, /a test/);
    assert.match(same.reason, /s1/);

    assert.equal(repairBudget(state, { part: 560, tests, sha: "s2", max: 3 }).ok, true);
    assert.equal(repairBudget(state, { part: 560, tests: ["other"], sha: "s1", max: 3 }).ok, true);
    assert.equal(repairBudget(state, { part: 561, tests, sha: "s1", max: 3 }).ok, true);

    for (const sha of ["s2", "s3"]) state = recordRepair(state, { part: 560, tests, sha });
    assert.equal(state[560].count, 3);
    const fourth = repairBudget(state, { part: 560, tests: ["new"], sha: "s9", max: 3 });
    assert.equal(fourth.ok, false);
    assert.match(fourth.reason, /3 unattended repairs/);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test("the fix skill documents the unattended e2e repair rules", () => {
  const skill = readFileSync(path.join(path.dirname(fileURLToPath(import.meta.url)), "..", ".claude", "skills", "fix", "SKILL.md"), "utf8");
  const start = skill.indexOf("## Unattended e2e repair");
  const section = skill.slice(start, skill.indexOf("\n## ", start + 5));
  assert.ok(section.length > 200);
  for (const word of ["Never start the local stack", "AppHost", "dotnet run", "compose.mjs", "skarbiec-e2e", "scratch test", "**open** `Fix:` part", "create nothing"]) {
    assert.ok(section.includes(word), word);
  }
});

test("only the newest comment is a diagnosis", () => {
  assert.equal(newestDiagnosis([c("older"), c(DIAGNOSIS)]), DIAGNOSIS);
  assert.equal(newestDiagnosis([c(DIAGNOSIS), c("ship report")]), null);
  assert.equal(newestDiagnosis([]), null);
});

test("rebuild inputs only for a fresh e2e branch with a newest diagnosis", () => {
  const comments = [c("x"), c(DIAGNOSIS)];
  const base = { e2e: true, comments, freshBranch: true, patchExists: true };
  assert.deepEqual(rebuildInputs(base), { diagnosis: DIAGNOSIS, applyPatch: true });
  assert.deepEqual(rebuildInputs({ ...base, patchExists: false }), { diagnosis: DIAGNOSIS, applyPatch: false });
  assert.deepEqual(rebuildInputs({ ...base, freshBranch: false }), { diagnosis: null, applyPatch: false });
  assert.deepEqual(rebuildInputs({ ...base, e2e: false }), { diagnosis: null, applyPatch: false });
  assert.deepEqual(rebuildInputs({ ...base, comments: [c(DIAGNOSIS), c("later")] }), { diagnosis: null, applyPatch: false });
});

test("the fix skill documents the ci mode", () => {
  const skill = readFileSync(path.join(path.dirname(fileURLToPath(import.meta.url)), "..", ".claude", "skills", "fix", "SKILL.md"), "utf8");
  const section = skill.slice(skill.indexOf("## Unattended CI fix (`ci:#<n>`)"), skill.indexOf("## Hard constraints"));
  assert.ok(section.length > 200);
  for (const word of ["ci-digest.mjs", "implementer → reviewer", "Skip =", "it.skip", "xit", "test.fixme", "weakened", "timeout-widened", "ship.mjs <n> --fix-pr", "FIX_RESULT", '"kind":"ci"', '"kind":"stop"', "areas.mjs profile", "build-feature", "gh-project.mjs prepare"]) {
    assert.ok(section.includes(word), word);
  }
});
