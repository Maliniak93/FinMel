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
    const args = (title, slug) => ["create", "--title", title, "--body-file", sandbox.bodyFile, "--slug", slug, "--tier", "1", "--kind", "change", "--area", "Backend"];

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

const lastJson = (stdout) => JSON.parse(stdout.trim().split(/\r?\n/).pop());
const areaOptions = ["Backend", "Frontend", "E2E", "Claude", "Scripts", "CI/Infra", "Docs", "Fullstack"];

function areaState(extra = {}) {
  return buildState({
    issues: [
      { number: 1, status: "Done", area: "Backend" },
      { number: 4, status: "Todo", labels: ["spec"], area: "CI/Infra" },
      { number: 3, status: "Todo", labels: ["spec"] },
      { number: 9, status: "Todo", labels: ["epic"] },
      { number: 8, status: "Todo", labels: ["spec"], parent: 9 },
    ],
    ...extra,
  });
}

const createArgs = (sandbox, slug, extra = []) => ["create", "--title", slug, "--body-file", sandbox.bodyFile, "--slug", slug, "--tier", "1", "--kind", "change", ...extra];
const issueCreates = (sandbox) => sandbox.log().filter((e) => e.kind === "issueCreate");

test("list and get include area", () => {
  const sandbox = createSandbox(areaState());
  try {
    const listed = sandbox.run("gh-project.mjs", ["list"]);
    assert.equal(listed.code, 0, listed.out);
    const cards = lastJson(listed.stdout);
    assert.equal(cards.find((c) => c.number === 1).area, "Backend");
    assert.equal(cards.find((c) => c.number === 4).area, "CI/Infra");
    assert.equal(cards.find((c) => c.number === 3).area, null);

    const queued = sandbox.run("gh-project.mjs", ["queue"]);
    assert.equal(queued.code, 0, queued.out);
    const queue = lastJson(queued.stdout);
    assert.equal(queue.find((c) => c.number === 4).area, "CI/Infra");
    assert.equal(queue.find((c) => c.number === 3).area, null);

    const got = sandbox.run("gh-project.mjs", ["get", "4"]);
    assert.equal(got.code, 0, got.out);
    assert.equal(lastJson(got.stdout).area, "CI/Infra");
    assert.equal(lastJson(sandbox.run("gh-project.mjs", ["get", "3"]).stdout).area, null);
  } finally {
    sandbox.cleanup();
  }
});

test("init creates Area", () => {
  const sandbox = createSandbox(areaState({ withArea: false }));
  try {
    const run = sandbox.run("gh-project.mjs", ["init"]);

    assert.equal(run.code, 0, run.out);
    const created = sandbox.log().filter((e) => e.kind === "fieldCreate");
    assert.equal(created.length, 1);
    assert.equal(created[0].name, "Area");
    assert.deepEqual(created[0].options, areaOptions);
  } finally {
    sandbox.cleanup();
  }
});

test("create sets Area", () => {
  const sandbox = createSandbox(areaState());
  try {
    const run = sandbox.run("gh-project.mjs", createArgs(sandbox, "areaful", ["--area", "fullstack"]));

    assert.equal(run.code, 0, run.out);
    const { number } = lastJson(run.stdout);
    assert.equal(sandbox.state().issues[number].area, "Fullstack");
  } finally {
    sandbox.cleanup();
  }
});

test("create rejects a bad or epic area", () => {
  const sandbox = createSandbox(areaState());
  try {
    const bad = sandbox.run("gh-project.mjs", createArgs(sandbox, "bad", ["--area", "nope"]));
    assert.notEqual(bad.code, 0, bad.out);
    for (const option of areaOptions) assert.ok(bad.out.includes(option), `${option} listed in: ${bad.out}`);

    const epic = sandbox.run("gh-project.mjs", createArgs(sandbox, "epicarea", ["--area", "Backend", "--epic"]));
    assert.notEqual(epic.code, 0, epic.out);
    assert.match(epic.out, /--area/);

    assert.deepEqual(issueCreates(sandbox), []);
  } finally {
    sandbox.cleanup();
  }
});

test("create requires area unless epic", () => {
  const sandbox = createSandbox(areaState());
  try {
    const missing = sandbox.run("gh-project.mjs", createArgs(sandbox, "noarea"));
    assert.notEqual(missing.code, 0, missing.out);
    assert.match(missing.out, /--area/);
    assert.deepEqual(issueCreates(sandbox), []);

    const epic = sandbox.run("gh-project.mjs", createArgs(sandbox, "splitepic", ["--epic"]));
    assert.equal(epic.code, 0, epic.out);
    assert.equal(sandbox.state().issues[lastJson(epic.stdout).number].area ?? null, null);

    const e2e = sandbox.run("gh-project.mjs", createArgs(sandbox, "e2epart", ["--parent", "9", "--e2e"]));
    assert.equal(e2e.code, 0, e2e.out);
    assert.equal(sandbox.state().issues[lastJson(e2e.stdout).number].area, "E2E");
  } finally {
    sandbox.cleanup();
  }
});

test("e2e part defaults to E2E", () => {
  const sandbox = createSandbox(areaState());
  try {
    const run = sandbox.run("gh-project.mjs", createArgs(sandbox, "e2epart", ["--parent", "9", "--e2e"]));

    assert.equal(run.code, 0, run.out);
    const { number } = lastJson(run.stdout);
    assert.equal(sandbox.state().issues[number].area, "E2E");
  } finally {
    sandbox.cleanup();
  }
});

test("edit sets Area", () => {
  const sandbox = createSandbox(areaState());
  try {
    const run = sandbox.run("gh-project.mjs", ["edit", "3", "--area", "Docs"]);

    assert.equal(run.code, 0, run.out);
    assert.equal(sandbox.state().issues[3].area, "Docs");
  } finally {
    sandbox.cleanup();
  }
});

test("create without the Area field fails first", () => {
  const sandbox = createSandbox(areaState({ withArea: false }));
  try {
    const run = sandbox.run("gh-project.mjs", createArgs(sandbox, "noarea", ["--area", "Backend"]));

    assert.notEqual(run.code, 0, run.out);
    assert.match(run.out, /node scripts\/gh-project\.mjs init/);
    assert.deepEqual(issueCreates(sandbox), []);
  } finally {
    sandbox.cleanup();
  }
});

test("prepare warns without area", () => {
  const warning = "Brak obszaru (Area) na #5 — ustaw: node scripts/gh-project.mjs set 5 Area <obszar>";
  const without = createSandbox(prepareState("Todo"), { branch: "feat/part-one" });
  try {
    const run = without.run("gh-project.mjs", ["prepare", "5"]);

    assert.equal(run.code, 0, run.out);
    const out = lastJson(run.stdout);
    assert.equal(out.ok, true);
    assert.ok(out.notes.includes(warning), JSON.stringify(out.notes));
    assert.ok(run.stderr.includes(warning), run.stderr);
  } finally {
    without.cleanup();
  }

  const state = prepareState("Todo");
  state.issues[5].area = "Docs";
  const withArea = createSandbox(state, { branch: "feat/part-one" });
  try {
    const run = withArea.run("gh-project.mjs", ["prepare", "5"]);

    assert.equal(run.code, 0, run.out);
    assert.ok(!lastJson(run.stdout).notes.some((n) => n.includes("Brak obszaru")));
    assert.ok(!run.stderr.includes("Brak obszaru"));
  } finally {
    withArea.cleanup();
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
