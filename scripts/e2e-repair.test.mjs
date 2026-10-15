import assert from "node:assert/strict";
import { test } from "node:test";
import { e2eBlockLines } from "./e2e-repair.mjs";

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
