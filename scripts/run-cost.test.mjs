import assert from "node:assert/strict";
import { test } from "node:test";
import { byTier, byType, callCost, priceOf, summarize, unpriced } from "./run-cost.mjs";

const usage = (u) => ({ input_tokens: 0, output_tokens: 0, cache_read_input_tokens: 0, cache_creation_input_tokens: 0, ...u });
const million = 1e6;

test("prices every current model family at its list price", () => {
  assert.equal(priceOf("claude-fable-5-2", 1).input, 8);
  assert.equal(priceOf("claude-opus-5-5", 1).output, 20);
  assert.equal(priceOf("claude-sonnet-5-5", 1).cacheRead, 0.1);
  assert.equal(callCost("claude-fable-5-2", usage({ output_tokens: million })), 40);
  assert.equal(callCost("claude-sonnet-5-5", usage({ cache_read_input_tokens: million })), 0.1);
});

test("prices cache writes at 1.25x input for 5 minutes and 2x for 1 hour", () => {
  const cost = callCost("claude-opus-5-5", usage({ cache_creation_input_tokens: 2 * million, cache_creation: { ephemeral_1h_input_tokens: million } }));
  assert.equal(cost, 4 * 1.25 + 4 * 2);
});

test("switches Haiku 5.5 to the long-prompt tier above 100k prompt tokens", () => {
  assert.equal(priceOf("claude-haiku-5-5", 100e3).input, 0.1);
  assert.equal(priceOf("claude-haiku-5-5", 100e3 + 1).input, 0.5);
  assert.equal(callCost("claude-haiku-5-5", usage({ input_tokens: 50e3, cache_read_input_tokens: 60e3 })), (50e3 * 0.5 + 60e3 * 0.05) / million);
});

test("lists a model without a price instead of counting it silently", () => {
  unpriced.clear();
  assert.equal(callCost("claude-mystery-9", usage({ output_tokens: million })), 0);
  assert.deepEqual([...unpriced], ["claude-mystery-9"]);
});

const run = (tier, status, stage, labels, cost, minutes) => ({ tier, status, stage, labels, cost, minutes, agents: [] });
const runs = [
  run(1, "ready", null, ["implement the spec", "adversarial review (round 1)"], 1, 10),
  run(1, "blocked", "implement", ["implement the spec", "implement the spec (escalated)"], 3, 30),
  run(2, "ready", null, ["implement the spec", "adversarial review (round 1)", "re-review (round 2)"], 4, 40),
  run(2, "blocked", "review", ["address blocking findings (round 1) (escalated)", "re-review (round 2)"], 2, 20),
  run(2, "blocked", "implement", ["implement the spec"], 0, 0),
];

test("counts a successful run by its ready status", () => {
  const s = summarize(runs);
  assert.equal(s.ready, 2);
  assert.equal(s.blocked, 3);
});

test("aggregates runs per tier", () => {
  const [t1, t2] = byTier(runs);
  assert.deepEqual(
    { tier: t1.tier, runs: t1.runs, ready: t1.ready, blockedAt: t1.blockedAt, escalated: t1.escalated, reReviewed: t1.reReviewed, costPerRun: t1.costPerRun, minutesPerRun: t1.minutesPerRun },
    { tier: 1, runs: 2, ready: 1, blockedAt: { implement: 1 }, escalated: 1, reReviewed: 0, costPerRun: 2, minutesPerRun: 20 },
  );
  assert.deepEqual({ runs: t2.runs, blocked: t2.blocked, blockedAt: t2.blockedAt, escalated: t2.escalated, reReviewed: t2.reReviewed, costPerRun: t2.costPerRun }, {
    runs: 3,
    blocked: 2,
    blockedAt: { review: 1, implement: 1 },
    escalated: 1,
    reReviewed: 2,
    costPerRun: 2,
  });
});

test("groups agents by type and model for per-agent figures", () => {
  const agent = (cost, cacheWrite) => ({ type: "reviewer", model: "claude-opus-5-5", calls: 1, cacheRead: 0, cacheWrite, output: 0, cost, maxContext: 0, bash: {} });
  const [g] = byType([agent(1, 100), agent(3, 300)]);
  assert.equal(g.agents, 2);
  assert.equal(g.cost / g.agents, 2);
  assert.equal(g.cacheWrite / g.agents, 200);
});
