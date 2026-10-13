import assert from "node:assert/strict";
import { test } from "node:test";
import { makeSandbox, markerOf, put, readFacts, runFacts } from "./helpers.mjs";

test("facts marks an agent running node scripts/verify.mjs --all and not one that only mentions it in a sentence", () => {
  const sb = makeSandbox();
  try {
    put(
      sb,
      ".claude/agents/runner.md",
      "---\nname: runner\ndescription: Runs the full check.\nmodel: haiku\n---\n\nRun `node scripts/verify.mjs --all --fix` before you return.\n",
    );
    put(
      sb,
      ".claude/agents/denier.md",
      "---\nname: denier\ndescription: Denies it.\nmodel: haiku\n---\n\nThe full `verify.mjs --all` is not yours, only `--projects web`.\n",
    );

    const run = runFacts(sb);
    assert.equal(run.status, 0, run.output);
    const { nodes } = readFacts(sb);

    assert.ok(markerOf(nodes["agent:runner"], "pełny verify")?.direct);
    assert.equal(markerOf(nodes["agent:denier"], "pełny verify"), undefined);
  } finally {
    sb.cleanup();
  }
});
