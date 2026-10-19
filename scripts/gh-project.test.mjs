import assert from "node:assert/strict";
import test from "node:test";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { BODY, buildState, createSandbox } from "./fake-gh-sandbox.mjs";

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

    const first = sandbox.run("gh-project.mjs", [...args("First", "first"), "--priority", "P2", "--queue", "after:3"]);
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

const createArgs = (sandbox, slug, extra = []) => ["create", "--title", slug, "--body-file", sandbox.bodyFile, "--slug", slug, "--tier", "1", "--kind", "change", ...(extra.includes("--parent") || extra.includes("--priority") ? [] : ["--priority", "P2"]), ...extra];
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

const P1 = "P1 Wysoki";
const P2 = "P2 Normalny";
const priorityOptions = ["P0 Pilne", "P1 Wysoki", "P2 Normalny", "P3 Niski"];
const writesOf = (sandbox) => sandbox.log().filter((e) => ["setField", "fieldBatch", "position", "mutation", "issueCreate"].includes(e.kind));

function priorityState(extra = {}) {
  return buildState({
    issues: [
      { number: 1, status: "Done" },
      { number: 440, status: "Todo", labels: ["spec"], priority: P1 },
      { number: 441, status: "Todo", labels: ["spec"], priority: P2 },
      { number: 442, status: "Todo", labels: ["spec"], priority: P2 },
      { number: 443, status: "Todo", labels: ["spec"] },
    ],
    ...extra,
  });
}

test("init creates Priority", () => {
  const sandbox = createSandbox(priorityState({ withPriority: false }));
  try {
    const run = sandbox.run("gh-project.mjs", ["init"]);

    assert.equal(run.code, 0, run.out);
    const created = sandbox.log().filter((e) => e.kind === "fieldCreate");
    assert.equal(created.length, 1);
    assert.equal(created[0].name, "Priority");
    assert.deepEqual(created[0].options, priorityOptions);
  } finally {
    sandbox.cleanup();
  }
});

test("list and get include priority", () => {
  const sandbox = createSandbox(priorityState());
  try {
    const listed = sandbox.run("gh-project.mjs", ["list"]);
    assert.equal(listed.code, 0, listed.out);
    const cards = lastJson(listed.stdout);
    assert.equal(cards.find((c) => c.number === 440).priority, P1);
    assert.equal(cards.find((c) => c.number === 443).priority, null);

    const queued = sandbox.run("gh-project.mjs", ["queue"]);
    assert.equal(queued.code, 0, queued.out);
    const queue = lastJson(queued.stdout);
    assert.equal(queue.find((c) => c.number === 441).priority, P2);
    assert.equal(queue.find((c) => c.number === 443).priority, null);

    assert.equal(lastJson(sandbox.run("gh-project.mjs", ["get", "440"]).stdout).priority, P1);
    assert.equal(lastJson(sandbox.run("gh-project.mjs", ["get", "443"]).stdout).priority, null);
  } finally {
    sandbox.cleanup();
  }
});

test("create places by priority", () => {
  const sandbox = createSandbox(priorityState());
  try {
    const urgent = sandbox.run("gh-project.mjs", createArgs(sandbox, "urgent", ["--area", "Backend", "--priority", "p1"]));
    assert.equal(urgent.code, 0, urgent.out);
    const urgentNumber = lastJson(urgent.stdout).number;
    assert.equal(sandbox.state().issues[urgentNumber].priority, P1);

    const plain = sandbox.run("gh-project.mjs", createArgs(sandbox, "plain", ["--area", "Backend"]));
    assert.equal(plain.code, 0, plain.out);
    const plainNumber = lastJson(plain.stdout).number;
    assert.equal(sandbox.state().issues[plainNumber].priority, P2);

    const sent = positions(sandbox);
    assert.equal(sent[0].itemId, `ITEM_${urgentNumber}`);
    assert.equal(sent[0].afterId, "ITEM_440");
    assert.equal(sent[1].itemId, `ITEM_${plainNumber}`);
    assert.equal(sent[1].afterId, "ITEM_443");
  } finally {
    sandbox.cleanup();
  }
});

test("create refuses a cross-band queue", () => {
  const state = priorityState();
  state.issues[9] = { ...state.issues[442], number: 9, labels: ["epic"], priority: null };
  state.order.push(9);
  const sandbox = createSandbox(state);
  try {
    const cross = sandbox.run("gh-project.mjs", createArgs(sandbox, "cross", ["--area", "Backend", "--priority", "P1", "--queue", "after:441"]));
    assert.notEqual(cross.code, 0, cross.out);
    assert.match(cross.out, /\/prio/);

    const part = sandbox.run("gh-project.mjs", createArgs(sandbox, "part", ["--area", "Backend", "--priority", "P1", "--parent", "9"]));
    assert.notEqual(part.code, 0, part.out);

    assert.deepEqual(issueCreates(sandbox), []);
  } finally {
    sandbox.cleanup();
  }
});

test("create requires priority unless part", () => {
  const state = priorityState();
  state.issues[9] = { ...state.issues[442], number: 9, labels: ["epic"], priority: null };
  state.order.push(9);
  const sandbox = createSandbox(state);
  try {
    const bare = ["create", "--title", "noprio", "--body-file", sandbox.bodyFile, "--slug", "noprio", "--tier", "1", "--kind", "change", "--area", "Backend"];
    const missing = sandbox.run("gh-project.mjs", bare);
    assert.notEqual(missing.code, 0, missing.out);
    assert.match(missing.out, /--priority/);
    assert.deepEqual(issueCreates(sandbox), []);

    const part = sandbox.run("gh-project.mjs", createArgs(sandbox, "partnoprio", ["--parent", "9", "--area", "Backend"]));
    assert.equal(part.code, 0, part.out);
    assert.equal(issueCreates(sandbox).length, 1);
  } finally {
    sandbox.cleanup();
  }
});

test("priority sets and re-places", () => {
  const sandbox = createSandbox(priorityState());
  try {
    const run = sandbox.run("gh-project.mjs", ["priority", "442", "P1"]);

    assert.equal(run.code, 0, run.out);
    assert.equal(sandbox.state().issues[442].priority, P1);
    const sent = positions(sandbox);
    assert.equal(sent.length, 1);
    assert.equal(sent[0].itemId, "ITEM_442");
    assert.equal(sent[0].afterId, "ITEM_440");
    assert.match(run.stdout, /#442 P2 → P1, 3\. → 2\. w kolejce/);
  } finally {
    sandbox.cleanup();
  }

  const running = createSandbox(
    buildState({
      issues: [
        { number: 10, status: "In progress", labels: ["spec"], priority: P2 },
        { number: 11, status: "Todo", labels: ["spec"], priority: P2 },
      ],
    }),
  );
  try {
    const run = running.run("gh-project.mjs", ["priority", "10", "P1"]);

    assert.equal(run.code, 0, run.out);
    assert.equal(running.state().issues[10].priority, P1);
    assert.equal(positions(running).length, 0);
  } finally {
    running.cleanup();
  }
});

test("priority refuses a part", () => {
  const sandbox = createSandbox(
    buildState({
      issues: [
        { number: 9, status: "Todo", labels: ["epic"] },
        { number: 8, status: "Todo", labels: ["spec"], parent: 9 },
      ],
    }),
  );
  try {
    const run = sandbox.run("gh-project.mjs", ["priority", "8", "P1"]);

    assert.notEqual(run.code, 0, run.out);
    assert.match(run.out, /#8 is a part of epic #9/);
    assert.deepEqual(writesOf(sandbox), []);
  } finally {
    sandbox.cleanup();
  }
});

test("priority where previews", () => {
  const sandbox = createSandbox(priorityState());
  try {
    const run = sandbox.run("gh-project.mjs", ["priority", "--where", "P1"]);

    assert.equal(run.code, 0, run.out);
    assert.match(run.stdout, /2\. w kolejce, za #440 \(P1\)/);
    assert.deepEqual(writesOf(sandbox), []);
  } finally {
    sandbox.cleanup();
  }
});

test("move stays in the band", () => {
  const sandbox = createSandbox(priorityState());
  try {
    const top = sandbox.run("gh-project.mjs", ["move", "442", "top"]);
    assert.equal(top.code, 0, top.out);
    assert.equal(positions(sandbox).length, 1);
    assert.equal(positions(sandbox)[0].afterId, "ITEM_440");

    const refused = sandbox.run("gh-project.mjs", ["move", "442", "before:440"]);
    assert.notEqual(refused.code, 0, refused.out);
    assert.match(refused.out, /\/prio/);
    assert.equal(positions(sandbox).length, 1);
  } finally {
    sandbox.cleanup();
  }
});

test("priority backfill batches and keeps order", () => {
  const issues = [
    { number: 1, status: "Done" },
    { number: 2, status: "Todo", labels: ["spec"], priority: P1 },
    { number: 9, status: "Todo", labels: ["epic"], priority: "P3 Niski" },
    { number: 8, status: "Todo", labels: ["spec"], parent: 9 },
  ];
  for (let n = 101; n <= 125; n++) issues.push({ number: n, status: "Todo", labels: ["spec"] });
  const sandbox = createSandbox(buildState({ issues }));
  try {
    const dry = sandbox.run("gh-project.mjs", ["priority", "--backfill"]);
    assert.equal(dry.code, 0, dry.out);
    for (let n = 101; n <= 125; n++) assert.match(dry.stdout, new RegExp(`#${n} → P2`));
    assert.doesNotMatch(dry.stdout, /#(1|2|8|9) →/);
    assert.deepEqual(writesOf(sandbox), []);

    const applied = sandbox.run("gh-project.mjs", ["priority", "--backfill", "--apply"]);
    assert.equal(applied.code, 0, applied.out);
    const batches = sandbox.log().filter((e) => e.kind === "fieldBatch");
    assert.deepEqual(
      batches.map((b) => b.size),
      [20, 5],
    );
    assert.equal(positions(sandbox).length, 0);
    const state = sandbox.state();
    for (let n = 101; n <= 125; n++) assert.equal(state.issues[n].priority, P2);
    assert.equal(state.issues[8].priority, null);
    assert.equal(state.issues[1].priority, null);
    assert.equal(state.issues[2].priority, P1);

    const again = sandbox.run("gh-project.mjs", ["priority", "--backfill", "--apply"]);
    assert.equal(again.code, 0, again.out);
    assert.equal(sandbox.log().filter((e) => e.kind === "fieldBatch").length, 2);
  } finally {
    sandbox.cleanup();
  }
});

const P3 = "P3 Niski";
const dependsBody = (...numbers) => `## Depends on\n\n${numbers.map((n) => `- #${n} — x`).join("\n")}\n`;
const jsonLine = (stdout) => JSON.parse(stdout.split(/\r?\n/).find((l) => l.startsWith("{")));
const orderOfState = (sandbox) => sandbox.state().order;

function dependencyState() {
  return buildState({
    issues: [
      { number: 450, status: "Todo", labels: ["spec"], priority: P1 },
      { number: 440, status: "Todo", labels: ["spec"], priority: P2, body: dependsBody(432) },
      { number: 441, status: "Todo", labels: ["spec"], priority: P2 },
      { number: 432, status: "Todo", labels: ["spec"], priority: P3 },
    ],
  });
}

test("cycle fails without writes", () => {
  const state = dependencyState();
  state.issues[432].body = dependsBody(440);
  const sandbox = createSandbox(state);
  try {
    const run = sandbox.run("gh-project.mjs", ["priority", "440", "P1"]);

    assert.notEqual(run.code, 0, run.out);
    assert.match(run.out, /cykl zależności: #440 → #432 → #440/);
    assert.deepEqual(writesOf(sandbox), []);
  } finally {
    sandbox.cleanup();
  }
});

test("priority raises dependencies", () => {
  const sandbox = createSandbox(dependencyState());
  try {
    const run = sandbox.run("gh-project.mjs", ["priority", "440", "P1"]);

    assert.equal(run.code, 0, run.out);
    assert.match(run.stdout, /podniesiono #432 P3 → P1 \(zależność #440\)/);
    const state = sandbox.state();
    assert.equal(state.issues[440].priority, P1);
    assert.equal(state.issues[432].priority, P1);
    assert.deepEqual(state.order, [450, 432, 440, 441]);
  } finally {
    sandbox.cleanup();
  }
});

test("lowering the dependent keeps raised dependencies", () => {
  const sandbox = createSandbox(dependencyState());
  try {
    assert.equal(sandbox.run("gh-project.mjs", ["priority", "440", "P1"]).code, 0);

    const run = sandbox.run("gh-project.mjs", ["priority", "440", "P3"]);

    assert.equal(run.code, 0, run.out);
    const state = sandbox.state();
    assert.equal(state.issues[440].priority, P3);
    assert.equal(state.issues[432].priority, P1);
  } finally {
    sandbox.cleanup();
  }
});

test("create and edit raise dependencies", () => {
  const created = createSandbox(dependencyState());
  try {
    fs.writeFileSync(created.bodyFile, `${BODY}\n${dependsBody(432)}`);
    const run = created.run("gh-project.mjs", createArgs(created, "dependent", ["--area", "Backend", "--priority", "P1"]));

    assert.equal(run.code, 0, run.out);
    const { number } = jsonLine(run.stdout);
    assert.match(run.stdout, /podniesiono #432 P3 → P1 \(zależność #\d+\)/);
    const state = created.state();
    assert.equal(state.issues[432].priority, P1);
    assert.equal(state.order.indexOf(432) + 1, state.order.indexOf(number));
  } finally {
    created.cleanup();
  }

  const edited = createSandbox(dependencyState());
  try {
    fs.writeFileSync(edited.bodyFile, `${BODY}\n${dependsBody(432)}`);
    assert.equal(edited.run("gh-project.mjs", ["priority", "441", "P1"]).code, 0);

    const run = edited.run("gh-project.mjs", ["edit", "441", "--body-file", edited.bodyFile]);

    assert.equal(run.code, 0, run.out);
    assert.match(run.stdout, /podniesiono #432 P3 → P1 \(zależność #441\)/);
    const state = edited.state();
    assert.equal(state.issues[432].priority, P1);
    assert.equal(state.order.indexOf(432) + 1, state.order.indexOf(441));
  } finally {
    edited.cleanup();
  }
});

test("priority where previews raises", () => {
  const sandbox = createSandbox(dependencyState());
  try {
    fs.writeFileSync(sandbox.bodyFile, `${BODY}\n${dependsBody(432)}`);

    const run = sandbox.run("gh-project.mjs", ["priority", "--where", "P1", "--body-file", sandbox.bodyFile]);

    assert.equal(run.code, 0, run.out);
    assert.match(run.stdout, /podniesiono #432 P3 → P1/);
    assert.deepEqual(writesOf(sandbox), []);
  } finally {
    sandbox.cleanup();
  }
});

test("lowering a blocking dependency fails", () => {
  const sandbox = createSandbox(
    buildState({
      issues: [
        { number: 432, status: "Todo", labels: ["spec"], priority: P1 },
        { number: 440, status: "Todo", labels: ["spec"], priority: P1, body: dependsBody(432) },
      ],
    }),
  );
  try {
    const lowered = sandbox.run("gh-project.mjs", ["priority", "432", "P3"]);
    assert.notEqual(lowered.code, 0, lowered.out);
    assert.match(lowered.out, /#432 blokuje #440 \(P1\)/);

    const moved = sandbox.run("gh-project.mjs", ["move", "432", "after:440"]);
    assert.notEqual(moved.code, 0, moved.out);
    assert.match(moved.out, /#432 blokuje #440 \(P1\)/);

    assert.deepEqual(writesOf(sandbox), []);
    assert.deepEqual(orderOfState(sandbox), [432, 440]);
  } finally {
    sandbox.cleanup();
  }
});

test("lastBuild", () => {
  const lastBuildOf = (sandbox, n) => {
    const got = sandbox.run("gh-project.mjs", ["get", String(n)]);
    assert.equal(got.code, 0, got.out);
    return lastJson(got.stdout).lastBuild;
  };
  const epic = { number: 30, status: "Todo", labels: ["epic"] };
  const sandboxes = [];
  try {
    const withE2e = createSandbox(
      buildState({
        issues: [
          epic,
          { number: 31, status: "Todo", labels: ["spec"], parent: 30, area: "Backend" },
          { number: 32, status: "Todo", labels: ["spec"], parent: 30, area: "Frontend" },
          { number: 33, status: "Todo", labels: ["spec", "e2e"], parent: 30, area: "E2E" },
        ],
      }),
    );
    sandboxes.push(withE2e);
    assert.equal(lastBuildOf(withE2e, 31), false);
    assert.equal(lastBuildOf(withE2e, 32), true);
    assert.equal(lastBuildOf(withE2e, 33), false);

    const firstClosed = createSandbox(
      buildState({
        issues: [
          epic,
          { number: 31, status: "Done", labels: ["spec"], parent: 30, area: "Backend" },
          { number: 32, status: "Todo", labels: ["spec"], parent: 30, area: "Frontend" },
          { number: 33, status: "Todo", labels: ["spec", "e2e"], parent: 30, area: "E2E" },
        ],
      }),
    );
    sandboxes.push(firstClosed);
    assert.equal(lastBuildOf(firstClosed, 32), true);

    const noE2e = createSandbox(
      buildState({
        issues: [
          epic,
          { number: 41, status: "Todo", labels: ["spec"], parent: 30, area: "Backend" },
          { number: 42, status: "Todo", labels: ["spec"], parent: 30, area: "Backend" },
          { number: 34, status: "Todo", labels: ["spec"], area: "Backend" },
        ],
      }),
    );
    sandboxes.push(noE2e);
    assert.equal(lastBuildOf(noE2e, 41), false);
    assert.equal(lastBuildOf(noE2e, 42), true);
    assert.equal(lastBuildOf(noE2e, 34), false);
  } finally {
    for (const sandbox of sandboxes) sandbox.cleanup();
  }
});

test("prepare workflowArgs verifyAll and area", () => {
  const first = prepareState("Todo");
  first.issues[5].area = "Docs";
  const open = createSandbox(first, { branch: "feat/part-one" });
  try {
    const run = open.run("gh-project.mjs", ["prepare", "5"]);

    assert.equal(run.code, 0, run.out);
    const { workflowArgs } = lastJson(run.stdout);
    assert.equal(workflowArgs.verifyAll, false);
    assert.equal(workflowArgs.area, "Docs");
  } finally {
    open.cleanup();
  }

  const last = prepareState("Todo");
  last.issues[5].status = "Done";
  last.issues[5].state = "CLOSED";
  const closed = createSandbox(last, { branch: "feat/part-two" });
  try {
    const run = closed.run("gh-project.mjs", ["prepare", "6"]);

    assert.equal(run.code, 0, run.out);
    const { workflowArgs } = lastJson(run.stdout);
    assert.equal(workflowArgs.verifyAll, true);
    assert.equal(workflowArgs.area, null);
  } finally {
    closed.cleanup();
  }
});

test("prepare workflowArgs profile", async () => {
  const { profileOf } = await import("./areas.mjs");
  const warning = "Brak obszaru (Area) na #5";
  for (const area of ["Scripts", null]) {
    const state = prepareState("Todo");
    if (area) state.issues[5].area = area;
    const sandbox = createSandbox(state, { branch: "feat/part-one" });
    try {
      const run = sandbox.run("gh-project.mjs", ["prepare", "5"]);

      assert.equal(run.code, 0, run.out);
      const out = lastJson(run.stdout);
      const { workflowArgs } = out;
      assert.deepEqual(workflowArgs.profile, profileOf(area, { skip: workflowArgs.skip, e2e: workflowArgs.e2e }), String(area));
      assert.equal(out.notes.some((n) => n.startsWith(warning)), area === null);
    } finally {
      sandbox.cleanup();
    }
  }
});

test("prepare replay profile", () => {
  const config = fs.mkdtempSync(path.join(os.tmpdir(), "profile-config-"));
  const saved = process.env.CLAUDE_CONFIG_DIR;
  process.env.CLAUDE_CONFIG_DIR = config;
  const state = prepareState("Todo");
  state.issues[5].area = "Scripts";
  const sandbox = createSandbox(state, { branch: "feat/part-one" });
  process.env.CLAUDE_CONFIG_DIR = saved ?? "";
  if (saved === undefined) delete process.env.CLAUDE_CONFIG_DIR;
  try {
    fs.mkdirSync(path.join(sandbox.dir, ".claude", "workflows"), { recursive: true });
    fs.copyFileSync(new URL("../.claude/workflows/build-feature.js", import.meta.url), path.join(sandbox.dir, ".claude", "workflows", "build-feature.js"));
    fs.writeFileSync(path.join(sandbox.dir, "scratch.txt"), "work in progress\n");

    const first = sandbox.run("gh-project.mjs", ["prepare", "5"]);
    assert.equal(first.code, 0, first.out);
    const { workflowArgs } = lastJson(first.stdout);
    assert.equal(typeof workflowArgs.profile?.label, "string", String(JSON.stringify(workflowArgs.profile)));

    const history = path.join(config, "projects", fs.realpathSync(sandbox.dir).replace(/[^A-Za-z0-9]/g, "-"));
    const record = (runId, label) => {
      const dir = path.join(history, "s1", "subagents", "workflows", runId);
      fs.mkdirSync(dir, { recursive: true });
      const journal = [
        { type: "started", key: "k0", agentId: "a0", label: "implement the spec", phase: "p" },
        { type: "result", key: "k0", agentId: "a0", result: { n: 0 } },
      ];
      fs.writeFileSync(path.join(dir, "journal.jsonl"), journal.map((e) => JSON.stringify(e)).join("\n") + "\n");
      fs.mkdirSync(path.join(history, "s1", "workflows"), { recursive: true });
      const args = { ...workflowArgs, profile: { ...workflowArgs.profile, label } };
      fs.writeFileSync(path.join(history, "s1", "workflows", `${runId}.json`), JSON.stringify({ runId, workflowName: "build-feature", args, result: { status: "ready" } }));
    };

    record("wf_changed", "Docs (implementer → reviewer)");
    const changed = sandbox.run("gh-project.mjs", ["prepare", "5"]);
    assert.equal(changed.code, 0, changed.out);
    const changedOut = lastJson(changed.stdout);
    assert.equal(changedOut.replay, null);
    assert.ok(changedOut.replaySkipped, JSON.stringify(changedOut));

    fs.rmSync(path.join(history, "s1"), { recursive: true, force: true });
    record("wf_same", workflowArgs.profile.label);
    const same = sandbox.run("gh-project.mjs", ["prepare", "5"]);
    assert.equal(same.code, 0, same.out);
    const sameOut = lastJson(same.stdout);
    assert.deepEqual(sameOut.replay?.steps, ["implement the spec"], JSON.stringify(sameOut));
    assert.equal(sameOut.replaySkipped, undefined);
  } finally {
    sandbox.cleanup();
    fs.rmSync(config, { recursive: true, force: true });
  }
});

test("report Profil", () => {
  const sandbox = createSandbox(
    buildState({ issues: [{ number: 60, status: "In progress", labels: ["spec"], area: "Scripts", branch: "feat/profil" }] }),
  );
  try {
    const post = (extra) => {
      const run = sandbox.run("gh-project.mjs", [
        "report",
        "60",
        "--json",
        JSON.stringify({ status: "shipped", branch: "feat/profil", tests: [], rounds: 0, ...extra }),
      ]);
      assert.equal(run.code, 0, run.out);
      return sandbox.log().filter((e) => e.kind === "comment").at(-1).body;
    };

    assert.ok(post({ profile: "Scripts (implementer → reviewer)" }).includes("**Profil:** Scripts (implementer → reviewer)"));
    assert.ok(!post({}).includes("Profil"));
    assert.ok(!post({ profile: "" }).includes("Profil"));
  } finally {
    sandbox.cleanup();
  }
});

test("report Poza obszarem", () => {
  const sandbox = createSandbox(
    buildState({ issues: [{ number: 60, status: "In progress", labels: ["spec"], area: "Backend", branch: "feat/outside" }] }),
  );
  try {
    const post = (extra) => {
      const run = sandbox.run("gh-project.mjs", [
        "report",
        "60",
        "--json",
        JSON.stringify({ status: "shipped", branch: "feat/outside", tests: [], rounds: 0, ...extra }),
      ]);
      assert.equal(run.code, 0, run.out);
      return sandbox.log().filter((e) => e.kind === "comment").at(-1).body;
    };

    assert.ok(post({ outsideArea: ["a", "b"] }).includes("**Poza obszarem:** a, b"));
    assert.ok(!post({ outsideArea: [] }).includes("Poza obszarem"));
    assert.ok(!post({}).includes("Poza obszarem"));
  } finally {
    sandbox.cleanup();
  }
});
