import assert from "node:assert/strict";
import { test } from "node:test";
import { historyDir, makeSandbox, measurementsOf, putAutobuildLog, runFacts } from "./helpers.mjs";

test("facts reads autobuild logs: a built part gives build and CI samples, a ship retried part only CI, a run with no done part nothing", () => {
  const sb = makeSandbox();
  try {
    putAutobuildLog(sb, 100, "20260301-100000", [
      "10:00:00  epic #100 · log skarbiec-plan/autobuild-runs/100/20260301-100000.log",
      "10:50:00  epic #100: done",
      "10:50:00  done in this run: #11 PR #50 (build 42 min, CI 9 min); #12 PR #51 (ship retried, CI 7 min); #13 PR #52 (build 10 min, CI 5 min, CI rerun 1)",
    ]);
    putAutobuildLog(sb, 100, "20260302-100000", [
      "10:00:00  epic #100 · log skarbiec-plan/autobuild-runs/100/20260302-100000.log",
      "10:30:00  epic #100: usage — limit reached",
      "10:30:00  resume: node scripts/autobuild.mjs 100",
    ]);
    const run = runFacts(sb, ["--history", historyDir(sb)]);
    assert.equal(run.status, 0, run.output);
    const m = measurementsOf(sb);
    assert.deepEqual([m["autobuild:build"].n, m["autobuild:build"].median, m["autobuild:build"].max], [2, 26, 42]);
    assert.deepEqual([m["autobuild:ci"].n, m["autobuild:ci"].median, m["autobuild:ci"].max], [3, 7, 9]);
  } finally {
    sb.cleanup();
  }
});
