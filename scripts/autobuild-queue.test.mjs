import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import path from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import { areaWarning, ensureBranch, mayStart, nextWork, pickNext, planFrom, planLines, postSkipOnce, runQueue, skipMarker, skippedLine, startAt, startWork, windowEnd } from "./autobuild-queue.mjs";
import { queueOf } from "./queue.mjs";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");

const cards = [
  { number: 1, title: "running", status: "In progress", epic: false, autobuild: true },
  { number: 2, title: "manual", status: "In progress", epic: false, autobuild: false },
  { number: 3, title: "todo a", status: "Todo", epic: false },
  { number: 4, title: "todo b", status: "Todo", epic: true, subIssues: [5, 6] },
  { number: 5, title: "part 1", status: "Done", parent: 4 },
  { number: 6, title: "part 2", status: "Todo", parent: 4 },
  { number: 7, title: "finished", status: "Done", epic: false },
];
const queue = [
  { ...cards[0], manual: false },
  { ...cards[1], manual: true },
  { ...cards[2], manual: false },
  { ...cards[3], manual: false, next: 6 },
];
const numbers = (plan) => plan.map((p) => p.number);

test("plan skips manual by default", () => {
  assert.deepEqual(numbers(planFrom(queue, {}, cards)), [1, 3, 4]);
});

test("plan includes manual and limits items", () => {
  assert.deepEqual(numbers(planFrom(queue, { includeManual: true, items: 2 }, cards)), [1, 2]);
});

test("plan for one number", () => {
  assert.deepEqual(numbers(planFrom(queue, { only: 4 }, cards)), [4]);
  assert.deepEqual(numbers(planFrom(queue, { only: 2 }, cards)), [2]);
  assert.deepEqual(planFrom(queue, { only: 7 }, cards), []);
  assert.throws(() => planFrom(queue, { only: 6 }, cards), /epic #4/);
});

test("next work for standalone and epic", () => {
  assert.equal(nextWork(cards[2], cards), 3);
  assert.equal(nextWork(cards[3], cards), 6);
});

test("times are the next occurrence", () => {
  const at = (d, h, m) => new Date(2026, 9, d, h, m, 0, 0);
  const start = at(18, 23, 0);
  assert.equal(windowEnd("07:00", start).getTime(), at(19, 7, 0).getTime());
  assert.equal(startAt("02:30", start).getTime(), at(19, 2, 30).getTime());
  assert.equal(startAt("15", start).getTime(), at(18, 23, 15).getTime());
  assert.equal(startAt("now", start).getTime(), start.getTime());
  assert.equal(windowEnd("07:00", at(18, 6, 0)).getTime(), at(18, 7, 0).getTime());
});

test("window blocks new builds", () => {
  const end = new Date(2026, 9, 19, 7, 0, 0, 0);
  assert.equal(mayStart(end, end), "window");
  assert.equal(mayStart(end, new Date(2026, 9, 19, 7, 5, 0, 0)), "window");
  assert.ok(!mayStart(end, new Date(2026, 9, 19, 6, 59, 0, 0)));
  assert.ok(!mayStart(null, new Date()));
});

test("start labels item and part", async () => {
  const calls = [];
  await startWork({
    item: 4,
    part: 6,
    gh: async (argv) => void calls.push(["gh", ...argv].join(" ")),
    build: async () => void calls.push("build"),
  });
  const labels = calls.filter((c) => c.includes("--add-label"));
  assert.ok(labels.some((c) => c.includes(" 4 ") && c.includes("autobuild")));
  assert.ok(labels.some((c) => c.includes(" 6 ") && c.includes("autobuild")));
  assert.equal(calls.at(-1), "build");
  assert.ok(calls.indexOf("build") > calls.findLastIndex((c) => c.includes("--add-label")));
});

const areaCards = [
  { number: 10, title: "Helper item", status: "Todo", area: "Scripts", epic: false, parent: null, autobuild: false },
  { number: 11, title: "Bare item", status: "Todo", area: null, epic: false, parent: null, autobuild: false },
  { number: 12, title: "Area epic", status: "Todo", area: null, epic: true, parent: null, autobuild: false, subIssues: [13, 14] },
  { number: 13, title: "Epic part one", status: "Done", area: "Backend", epic: false, parent: 12, autobuild: false },
  { number: 14, title: "Epic part two", status: "Todo", area: null, epic: false, parent: 12, autobuild: false },
];

test("plan lines show area", () => {
  const [helper, bare, epic] = planLines(queueOf(areaCards), areaCards);

  assert.match(helper, /Helper item.*Scripts/);
  assert.match(bare, /Bare item.*—/);
  assert.match(epic, /Area epic.*Backend → —/);
});

test("missing area warns and continues", () => {
  assert.equal(areaWarning(areaCards[1], areaCards), "⚠ #11 bez obszaru (Area)");
  assert.equal(areaWarning(areaCards[2], areaCards), "⚠ #14 bez obszaru (Area)");
  assert.equal(areaWarning(areaCards[0], areaCards), null);
});

const flowCards = () => [
  { number: 434, title: "Running epic", status: "In progress", epic: true, parent: null, autobuild: true, subIssues: [435, 436, 437], priority: "P2", area: null },
  { number: 435, title: "Part one", status: "Done", parent: 434, area: "Backend" },
  { number: 436, title: "Part two", status: "Todo", parent: 434, area: "Backend" },
  { number: 437, title: "Part three", status: "Todo", parent: 434, area: "Backend" },
  { number: 440, title: "Other", status: "Todo", epic: false, parent: null, priority: "P2", area: "Scripts" },
].map((c) => ({ tier: "1", ...c }));
const urgent = { number: 450, title: "Fire", status: "Todo", epic: false, parent: null, priority: "P0", area: "Scripts", tier: "1" };
const setStatus = (cards, number, status) => cards.map((c) => (c.number === number ? { ...c, status } : c));

test("pickNext takes a new P0 between parts", () => {
  const started = new Set([434]);
  let cards = [...flowCards(), urgent];
  assert.equal(pickNext(queueOf(cards), { started, items: Infinity })?.number, 450);

  cards = setStatus(cards, 450, "Done");
  assert.equal(pickNext(queueOf(cards), { started, items: Infinity })?.number, 434);
});

test("pickNext respects items and manual", () => {
  const cards = [...flowCards(), urgent];
  assert.equal(pickNext(queueOf(cards), { started: new Set([434]), items: 1 })?.number, 434);
  assert.equal(pickNext(queueOf(cards), { started: new Set([434, 450]), items: 1 })?.number, 450);

  const manual = [{ number: 2, title: "manual", status: "In progress", epic: false, parent: null, autobuild: false }, ...flowCards().slice(3)];
  assert.equal(pickNext(queueOf(manual), { started: new Set(), items: Infinity })?.number, 440);
  assert.equal(pickNext(queueOf(manual), { started: new Set(), items: Infinity, includeManual: true })?.number, 2);
});

test("queue re-read before each part", async () => {
  let cards = flowCards();
  let reads = 0;
  const built = [];
  const logs = [];
  await runQueue({
    items: Infinity,
    includeManual: false,
    readCards: async () => {
      reads++;
      return cards;
    },
    buildPart: async (item, part) => {
      built.push(part);
      cards = setStatus(cards, part, "Done");
      if (part === 437) cards = setStatus(cards, 434, "Done");
      if (built.length === 1) cards = [...cards, urgent];
    },
    log: (line) => logs.push(line),
  });

  assert.deepEqual(built, [436, 450, 437, 440]);
  assert.ok(reads >= built.length, "the queue is read before every part");
  assert.ok(logs.some((l) => l.includes("kolejka zmieniona: #450 (P0) przed #434")), logs.join("\n"));
});

test("a merged part is not built again while its card lags behind the closed issue", async () => {
  const cards = flowCards();
  const closed = new Set();
  const built = [];
  const finishedEpics = [];
  await runQueue({
    readCards: async () => cards,
    buildPart: async (item, part) => {
      built.push(part);
      closed.add(part);
    },
    isClosed: async (number) => closed.has(number),
    finishEpic: async (item) => {
      finishedEpics.push(item.number);
    },
  });

  assert.deepEqual(built, [436, 437, 440]);
  assert.deepEqual(finishedEpics, [434]);
});

const standalone = (number, extra = {}) => ({ number, title: `Card ${number}`, status: "Todo", epic: false, parent: null, priority: "P2", area: "Scripts", tier: "1", dependsOn: [], ...extra });

async function drive(cards, hooks = {}) {
  const built = [];
  const logs = [];
  const skips = [];
  const finishedEpics = [];
  const result = await runQueue({
    readCards: async () => cards,
    buildPart: async (item, part) => {
      built.push(part);
      cards = setStatus(cards, part, "Done");
    },
    finishEpic: async (item) => void finishedEpics.push(item.number),
    onSkip: async (skip) => void skips.push(skip),
    log: (line) => logs.push(line),
    ...hooks,
  });
  return { built, logs, skips, finishedEpics, result, cards };
}

test("missing branch is healed", () => {
  const healed = [];
  const heal = (number) => {
    healed.push(number);
    return { branch: "feat/derived", healed: true, source: "derived" };
  };

  assert.equal(ensureBranch({ number: 466, branch: null }, heal).branch, "feat/derived");
  assert.deepEqual(healed, [466]);
  assert.equal(ensureBranch({ number: 5, branch: "feat/set" }, heal).branch, "feat/set");
  assert.deepEqual(healed, [466]);
  assert.throws(() => ensureBranch({ number: 7, branch: null }, () => ({ branch: null })), /#7 has no Branch/);
});

test("unfixable card is skipped", async () => {
  for (const tier of [null, "3"]) {
    const cards = [standalone(440, { tier }), standalone(441), standalone(442)];
    const { built, logs, skips, cards: after } = await drive(cards);

    assert.deepEqual(built, [441, 442]);
    assert.deepEqual(skips, [{ number: 440, reason: "brak Tier" }]);
    assert.ok(logs.includes("pominięto #440: brak Tier"), logs.join("\n"));
    assert.equal(after.find((c) => c.number === 440).status, "Todo");
  }
});

test("skip propagates to epic parts and dependents", async () => {
  const cards = [
    { ...standalone(434), epic: true, subIssues: [435, 436, 437], priority: "P2", area: null, status: "In progress" },
    standalone(435, { status: "Done", parent: 434 }),
    standalone(436, { tier: null, parent: 434 }),
    standalone(437, { parent: 434 }),
    standalone(440, { dependsOn: [436] }),
    standalone(441, { dependsOn: [440] }),
    standalone(442),
  ];
  const { built, logs, skips, finishedEpics } = await drive(cards);

  assert.deepEqual(built, [442]);
  assert.deepEqual(skips, [
    { number: 436, reason: "brak Tier" },
    { number: 437, reason: "zależy od #436" },
    { number: 440, reason: "zależy od #436" },
    { number: 441, reason: "zależy od #440" },
  ]);
  assert.ok(logs.includes("pominięto #437: zależy od #436"), logs.join("\n"));
  assert.deepEqual(finishedEpics, []);
});

test("skip comment posted once", async () => {
  const comments = [];
  const post = async (body) => void comments.push({ body });
  const cards = [standalone(466, { tier: null }), standalone(467)];
  const runs = [];

  for (let i = 0; i < 2; i++) {
    runs.push(
      await drive(cards, {
        onSkip: ({ number, reason }) => postSkipOnce({ number, reason, comments: async () => comments, post }),
      }),
    );
  }

  assert.ok(runs.every((r) => r.logs.includes("pominięto #466: brak Tier")));
  assert.equal(comments.length, 1);
  assert.ok(comments[0].body.includes(skipMarker(466)));
  assert.ok(comments[0].body.includes("brak Tier"));
});

test("run with skips ends done", async () => {
  const cards = [standalone(466, { tier: null }), { ...standalone(468), epic: true, subIssues: [469], dependsOn: [466] }, standalone(469, { parent: 468 }), standalone(470)];
  const { built, result } = await drive(cards);

  assert.deepEqual(built, [470]);
  assert.deepEqual(result.skipped, [
    { number: 466, reason: "brak Tier" },
    { number: 469, reason: "zależy od #466" },
  ]);
  assert.equal(skippedLine(result.skipped), "pominięte: #466 (brak Tier), #469 (zależy od #466)");
  assert.equal(skippedLine([]), null);
});

test("autobuild heals a part through ensureBranch", () => {
  const source = readFileSync(path.join(root, "scripts/autobuild.mjs"), "utf8");
  assert.match(source, /ensureBranch\(getIssue/);
});

test("plan lines show priority", () => {
  const cards = [{ number: 1, title: "Helper item", status: "Todo", area: "Scripts", priority: "P1", epic: false, parent: null, autobuild: false }];
  assert.match(planLines(queueOf(cards), cards)[0], /Helper item.*\[P1 · Scripts\]/);
});

test("delay is an unknown flag", () => {
  const res = spawnSync(process.execPath, ["scripts/autobuild.mjs", "423", "--dry-run", "--delay", "5"], { cwd: root, encoding: "utf8" });
  assert.equal(res.status, 2);
  const out = `${res.stdout}${res.stderr}`;
  assert.match(out, /unexpected argument: --delay/);
  assert.doesNotMatch(out, /runner online/);
});

test("docs no longer mention delay and describe the window", () => {
  const read = (f) => readFileSync(path.join(root, f), "utf8");
  for (const f of ["CLAUDE.md", "skarbiec-plan/workflow.md", "scripts/autobuild.mjs"]) assert.doesNotMatch(read(f), /--delay/, f);
  assert.match(read("skarbiec-plan/workflow.md"), /--until/);
  assert.match(read("CLAUDE.md"), /--until/);
});
