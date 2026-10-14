import assert from "node:assert/strict";
import { test } from "node:test";
import { DAY, T0, historyDir, makeSandbox, measurementsOf, runFacts, writeRun } from "./helpers.mjs";

test("facts uses only the newest 10 samples of a key and the mean of the middle two as an even-count median", () => {
  const sb = makeSandbox();
  try {
    const h = historyDir(sb);
    for (let i = 1; i <= 13; i++) {
      const durationSec = i <= 3 ? 5000 : 10 * (i - 3);
      const type = i >= 10 ? "reviewer" : "implementer";
      const label = i >= 10 ? "re-review (round 1)" : "implement the spec";
      writeRun(h, { id: `s${i}`, startMs: T0 + i * DAY, durationSec, status: "ready", agents: [{ type, label, atSec: 0 }] });
    }
    const run = runFacts(sb, ["--history", h]);
    assert.equal(run.status, 0, run.output);
    const m = measurementsOf(sb);
    const total = m["run:build-feature"];
    assert.deepEqual([total.n, total.median, total.max], [10, 55, 100]);
    const rr = m["agent:reviewer:re-review"];
    assert.deepEqual([rr.n, rr.median, rr.max], [4, 85, 100]);
  } finally {
    sb.cleanup();
  }
});
