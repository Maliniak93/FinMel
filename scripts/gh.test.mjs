import assert from "node:assert/strict";
import { test } from "node:test";
import { cardFromIssueNode, checkBuckets, listPages, reportUsage, usageDelta, usageLine } from "./gh.mjs";

const RESET = 1_800_000_000;
const snapshot = (graphqlRemaining, restRemaining, reset = RESET) => ({
  graphql: { limit: 5000, remaining: graphqlRemaining, reset },
  core: { limit: 5000, remaining: restRemaining, reset },
});

test("usageDelta same window", () => {
  const delta = usageDelta(snapshot(4900, 4990), snapshot(4897, 4985));
  assert.equal(delta.reset, false);
  assert.equal(delta.graphql, 3);
  assert.equal(delta.rest, 5);
});

test("usageDelta across reset", () => {
  const delta = usageDelta(snapshot(100, 100), snapshot(4999, 4999, RESET + 3600));
  assert.equal(delta.reset, true);
});

test("usageLine format", () => {
  const line = usageLine(snapshot(4812, 4990));
  assert.match(line, /^GitHub: GraphQL 4812\/5000, REST 4990\/5000, reset \d{2}:\d{2}$/);
});

const issueNode = (overrides = {}) => ({
  number: 347,
  title: "GitHub API budget 1",
  body: "# body\r\n",
  state: "OPEN",
  url: "https://github.com/Maliniak93/FinMel/issues/347",
  labels: { nodes: [{ name: "spec" }, { name: "skip-tests" }] },
  parent: { number: 346 },
  subIssues: { nodes: [] },
  projectItems: {
    nodes: [
      {
        id: "PVTI_1",
        project: { number: 1 },
        status: { name: "In progress" },
        tier: { name: "2" },
        kind: { name: "Change" },
        branch: { text: "feat/gh-api-lean-queries" },
      },
    ],
  },
  ...overrides,
});

test("cardFromIssueNode maps fields", () => {
  const card = cardFromIssueNode(issueNode());
  assert.equal(card.number, 347);
  assert.equal(card.title, "GitHub API budget 1");
  assert.equal(card.url, "https://github.com/Maliniak93/FinMel/issues/347");
  assert.equal(card.state, "OPEN");
  assert.deepEqual(card.labels, ["spec", "skip-tests"]);
  assert.equal(card.parent, 346);
  assert.equal(card.inProject, true);
  assert.equal(card.status, "In progress");
  assert.equal(card.tier, "2");
  assert.equal(card.kind, "Change");
  assert.equal(card.branch, "feat/gh-api-lean-queries");
  assert.equal(card.epic, false);
  assert.equal(card.skipTests, true);
  assert.equal(card.e2e, false);
  assert.deepEqual(card.subIssues, []);
});

test("cardFromIssueNode maps an epic outside the project", () => {
  const card = cardFromIssueNode(
    issueNode({
      labels: { nodes: [{ name: "epic" }] },
      parent: null,
      subIssues: { nodes: [{ number: 348, title: "part 2", state: "open" }] },
      projectItems: { nodes: [] },
    }),
  );
  assert.equal(card.epic, true);
  assert.equal(card.parent, null);
  assert.equal(card.inProject, false);
  assert.equal(card.status, null);
  assert.equal(card.branch, null);
  assert.deepEqual(card.subIssues, [{ number: 348, title: "part 2", state: "OPEN" }]);
});

test("listPages follows endCursor", async () => {
  const cursors = [];
  const pages = {
    start: { nodes: [1, 2], pageInfo: { hasNextPage: true, endCursor: "c1" } },
    c1: { nodes: [3], pageInfo: { hasNextPage: true, endCursor: "c2" } },
    c2: { nodes: [4, 5], pageInfo: { hasNextPage: false, endCursor: null } },
  };
  const all = await listPages(async (cursor) => {
    cursors.push(cursor ?? null);
    return pages[cursor ?? "start"];
  });
  assert.deepEqual(all, [1, 2, 3, 4, 5]);
  assert.deepEqual(cursors, [null, "c1", "c2"]);
});

test("checkBuckets maps conclusions", () => {
  const run = (name, status, conclusion) => ({ name, status, conclusion, html_url: `https://x/${name}` });
  const buckets = checkBuckets([
    run("ok", "completed", "success"),
    run("skipped", "completed", "skipped"),
    run("neutral", "completed", "neutral"),
    run("bad", "completed", "failure"),
    run("late", "completed", "timed_out"),
    run("stopped", "completed", "cancelled"),
    run("running", "in_progress", null),
    run("queued", "queued", null),
  ]);
  const byName = Object.fromEntries(buckets.map((c) => [c.name, c.bucket]));
  assert.deepEqual(byName, {
    ok: "pass",
    skipped: "skipping",
    neutral: "skipping",
    bad: "fail",
    late: "fail",
    stopped: "cancel",
    running: "pending",
    queued: "pending",
  });
  assert.deepEqual(checkBuckets([]), []);
});

test("reportUsage start and end", () => {
  const start = snapshot(4900, 4990);
  const end = snapshot(4850, 4950);
  const line = reportUsage(start, end);
  assert.equal(line.includes("\n"), false);
  assert.ok(line.includes(usageLine(start)));
  assert.ok(line.includes(usageLine(end)));
  assert.ok(line.indexOf(usageLine(start)) < line.indexOf(usageLine(end)));
});
