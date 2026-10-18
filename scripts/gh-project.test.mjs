import assert from "node:assert/strict";
import test from "node:test";
import { buildState, createSandbox } from "./fake-gh-sandbox.mjs";

const positions = (sandbox) => sandbox.log().filter((e) => e.kind === "position");
const setFields = (sandbox) => sandbox.log().filter((e) => e.kind === "setField");

function boardState() {
  return buildState({
    issues: [
      { number: 1, status: "Done" },
      { number: 4, status: "Todo", labels: ["spec"] },
      { number: 3, status: "Todo", labels: ["spec"] },
      { number: 5, status: "Todo", labels: ["spec"] },
      { number: 9, status: "Todo", labels: ["epic"] },
      { number: 8, status: "Todo", labels: ["spec"], parent: 9 },
    ],
  });
}

test("move sends the position mutation", () => {
  const sandbox = createSandbox(boardState());
  try {
    const moved = sandbox.run("gh-project.mjs", ["move", "5", "before:3"]);

    assert.equal(moved.code, 0, moved.out);
    const sent = positions(sandbox);
    assert.equal(sent.length, 1);
    assert.equal(sent[0].itemId, "ITEM_5");
    assert.equal(sent[0].afterId, "ITEM_4");
    assert.match(sent[0].query, /updateProjectV2ItemPosition/);
    assert.match(sent[0].query, /afterId/);

    const refused = sandbox.run("gh-project.mjs", ["move", "8", "top"]);

    assert.equal(refused.code, 1, refused.out);
    assert.match(refused.out, /#8 is not in the queue/);
    assert.equal(positions(sandbox).length, 1);
  } finally {
    sandbox.cleanup();
  }
});

test("create places the card", () => {
  const sandbox = createSandbox(boardState());
  try {
    const args = (title, slug) => ["create", "--title", title, "--body-file", sandbox.bodyFile, "--slug", slug, "--tier", "1", "--kind", "change"];

    const first = sandbox.run("gh-project.mjs", [...args("First", "first"), "--queue", "after:3"]);
    assert.equal(first.code, 0, first.out);
    const firstNumber = JSON.parse(first.stdout.trim().split(/\r?\n/).pop()).number;

    const second = sandbox.run("gh-project.mjs", [...args("Second part", "second"), "--parent", "9"]);
    assert.equal(second.code, 0, second.out);
    const secondNumber = JSON.parse(second.stdout.trim().split(/\r?\n/).pop()).number;

    const sent = positions(sandbox);
    assert.equal(sent.length, 2);
    assert.equal(sent[0].itemId, `ITEM_${firstNumber}`);
    assert.equal(sent[0].afterId, "ITEM_3");
    assert.equal(sent[1].itemId, `ITEM_${secondNumber}`);
    assert.equal(sent[1].afterId, "ITEM_8");
  } finally {
    sandbox.cleanup();
  }
});

test("list reads board order", () => {
  const state = boardState();
  state.issues[5].labels.push("autobuild");
  const sandbox = createSandbox(state);
  try {
    const { code, stdout, out } = sandbox.run("gh-project.mjs", ["list"]);

    assert.equal(code, 0, out);
    const items = sandbox.log().filter((e) => e.kind === "items");
    assert.ok(items.length > 0);
    assert.match(JSON.stringify(items[0]), /orderBy/);
    assert.match(JSON.stringify(items[0]), /POSITION/);

    const cards = JSON.parse(stdout.trim().split(/\r?\n/).pop());
    assert.deepEqual(
      cards.map((c) => c.number),
      [1, 4, 3, 5, 9, 8],
    );
    for (const c of cards) assert.equal(typeof c.autobuild, "boolean", `#${c.number}`);
    assert.equal(cards.find((c) => c.number === 5).autobuild, true);
    assert.equal(cards.find((c) => c.number === 3).autobuild, false);
  } finally {
    sandbox.cleanup();
  }
});

function prepareState(epicStatus) {
  return buildState({
    issues: [
      { number: 9, status: epicStatus, labels: ["epic"], kind: null, tier: null },
      { number: 5, status: "Todo", labels: ["spec"], parent: 9, branch: "feat/part-one", body: "## Goal\n\nx\n" },
      { number: 6, status: "Todo", labels: ["spec"], parent: 9, branch: "feat/part-two" },
    ],
  });
}

test("prepare moves the epic with its part", () => {
  const todo = createSandbox(prepareState("Todo"), { branch: "feat/part-one" });
  try {
    const run = todo.run("gh-project.mjs", ["prepare", "5"]);

    assert.equal(run.code, 0, run.out);
    assert.equal(JSON.parse(run.stdout.trim().split(/\r?\n/).pop()).ok, true, run.out);
    const state = todo.state();
    assert.equal(state.issues[5].status, "In progress");
    assert.equal(state.issues[9].status, "In progress");
  } finally {
    todo.cleanup();
  }

  const started = createSandbox(prepareState("In progress"), { branch: "feat/part-one" });
  try {
    const run = started.run("gh-project.mjs", ["prepare", "5"]);

    assert.equal(run.code, 0, run.out);
    assert.equal(started.state().issues[5].status, "In progress");
    assert.deepEqual(
      setFields(started).filter((e) => e.item === "ITEM_9"),
      [],
    );
  } finally {
    started.cleanup();
  }
});
