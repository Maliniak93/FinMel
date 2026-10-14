import assert from "node:assert/strict";
import { test } from "node:test";
import { historyDir, makeSandbox, measurementsOf, putEpicLog, runFacts } from "./helpers.mjs";

test("facts reads epic logs: a built part gives build and CI samples, a ship retried part only CI, a run with no done part nothing", () => {
  const sb = makeSandbox();
  try {
    putEpicLog(sb, 100, "20260301-100000", [
      "10:00:00  epic #100 · log skarbiec-plan/epic-runs/100/20260301-100000.log",
      "10:50:00  epic #100: done",
      "10:50:00  done in this run: #11 PR #50 (build 42 min, CI 9 min); #12 PR #51 (ship retried, CI 7 min)",
    ]);
    putEpicLog(sb, 100, "20260302-100000", [
      "10:00:00  epic #100 · log skarbiec-plan/epic-runs/100/20260302-100000.log",
      "10:30:00  epic #100: usage — limit reached",
      "10:30:00  resume: node scripts/epic.mjs 100",
    ]);
    const run = runFacts(sb, ["--history", historyDir(sb)]);
    assert.equal(run.status, 0, run.output);
    const m = measurementsOf(sb);
    assert.deepEqual([m["epic:build"].n, m["epic:build"].median, m["epic:build"].max], [1, 42, 42]);
    assert.deepEqual([m["epic:ci"].n, m["epic:ci"].median, m["epic:ci"].max], [2, 8, 9]);
  } finally {
    sb.cleanup();
  }
});
