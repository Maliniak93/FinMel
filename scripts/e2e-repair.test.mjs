import assert from "node:assert/strict";
import { test } from "node:test";
import { E2E_APP_BLOCKED, E2E_DIAGNOSIS, appBlocks, e2eBlockLines, newestDiagnosis, parseFixResult, rebuildInputs, repairOutcome } from "./e2e-repair.mjs";

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

test("app blocks counted across test repairs", () => {
  const app = c(`${E2E_APP_BLOCKED} — x`);
  const comments = [app, c(DIAGNOSIS), app, c("ship report"), c(DIAGNOSIS), app, c(DIAGNOSIS), app, c("**Blocked** at `e2e` (environment)")];
  assert.equal(appBlocks(comments), 4);
  assert.equal(appBlocks([]), 0);
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
