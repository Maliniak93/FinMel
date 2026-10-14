import assert from "node:assert/strict";
import { test } from "node:test";
import { OTHER, T0, VERIFY, historyDir, makeSandbox, measurementsOf, runFacts, writeRun } from "./helpers.mjs";

const IMPL = "agent:implementer:implement the spec";
const IMPL_ESC = "agent:implementer:implement the spec (escalated)";
const REVIEW = "agent:reviewer:adversarial review";
const REREVIEW = "agent:reviewer:re-review";
const TESTS = "agent:test-writer:write the tests";

function seed(sb) {
  const h = historyDir(sb);
  writeRun(h, {
    id: "r1",
    startMs: T0,
    durationSec: 400,
    status: "ready",
    agents: [
      { type: "test-writer", label: "write the tests", atSec: 0 },
      {
        type: "implementer",
        label: "implement the spec",
        atSec: 60,
        bash: [
          { seconds: 100, command: VERIFY },
          { seconds: 30, command: OTHER },
          { seconds: 50, command: VERIFY },
        ],
      },
      { type: "reviewer", label: "adversarial review (round 1)", atSec: 330 },
    ],
  });
  writeRun(h, {
    id: "r2",
    startMs: T0 + 1000000,
    durationSec: 330,
    status: "ready",
    agents: [
      { type: "test-writer", label: "write the tests", atSec: 0 },
      { type: "implementer", label: "implement the spec", atSec: 80 },
      { type: "reviewer", label: "adversarial review (round 1)", atSec: 280 },
    ],
  });
  writeRun(h, {
    id: "r3",
    startMs: T0 + 2000000,
    durationSec: 700,
    status: "ready",
    agents: [
      { type: "test-writer", label: "write the tests", atSec: 0 },
      { type: "implementer", label: "implement the spec", atSec: 90 },
      { type: "implementer", label: "implement the spec (escalated)", atSec: 390 },
      { type: "reviewer", label: "adversarial review (round 1)", atSec: 590 },
      { type: "reviewer", label: "re-review (round 2)", atSec: 640 },
    ],
  });
  writeRun(h, {
    id: "r4",
    startMs: T0 + 3000000,
    durationSec: 46,
    status: "ready",
    agents: [{ type: "reviewer", label: "re-review (round 1)", atSec: 0 }],
  });
  writeRun(h, {
    id: "rb",
    startMs: T0 + 4000000,
    durationSec: 9999,
    status: "blocked",
    agents: [
      { type: "implementer", label: "implement the spec", atSec: 0, bash: [{ seconds: 999, command: VERIFY }] },
      { type: "reviewer", label: "adversarial review (round 1)", atSec: 5000 },
    ],
  });
  writeRun(h, {
    id: "rf",
    startMs: T0 + 5000000,
    durationSec: 8888,
    status: "failed",
    agents: [{ type: "reviewer", label: "re-review (round 1)", atSec: 0 }],
  });
  return h;
}

test("facts with a history of ready, blocked, failed, escalated and resumed runs samples only the ready runs, per agent wall time, escalated under its own key", () => {
  const sb = makeSandbox();
  try {
    const run = runFacts(sb, ["--history", seed(sb)]);
    assert.equal(run.status, 0, run.output);
    const m = measurementsOf(sb);

    assert.deepEqual([m[IMPL].n, m[IMPL].median, m[IMPL].max], [3, 270, 300]);
    assert.deepEqual([m[IMPL_ESC].n, m[IMPL_ESC].median, m[IMPL_ESC].max], [1, 200, 200]);
    assert.deepEqual([m[REVIEW].n, m[REVIEW].median, m[REVIEW].max], [3, 50, 70]);
    assert.deepEqual([m[REREVIEW].n, m[REREVIEW].median, m[REREVIEW].max], [2, 53, 60]);
    assert.deepEqual([m[TESTS].n, m[TESTS].median, m[TESTS].max], [3, 80, 90]);
    assert.deepEqual([m["run:build-feature"].n, m["run:build-feature"].median, m["run:build-feature"].max], [4, 365, 700]);
  } finally {
    sb.cleanup();
  }
});

test("facts sums the verify.mjs Bash calls of a counted implementer into one verify:implementer sample and ignores a blocked run", () => {
  const sb = makeSandbox();
  try {
    const run = runFacts(sb, ["--history", seed(sb)]);
    assert.equal(run.status, 0, run.output);
    const v = measurementsOf(sb)["verify:implementer"];
    assert.deepEqual([v.n, v.median, v.max], [1, 150, 150]);
  } finally {
    sb.cleanup();
  }
});
