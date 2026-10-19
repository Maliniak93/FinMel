import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { PassThrough, Writable } from "node:stream";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import { ask, equivalentCommand, parseFlags, questions, runWizard, summary } from "./autobuild-wizard.mjs";
import { queueOf } from "./queue.mjs";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");

const manualItem = { number: 391, title: "Month dashboard", status: "In progress", manual: true };
const plainItem = { number: 400, title: "Build queue", status: "Todo", manual: false };
const working = { works: true, hibernateAvailable: true, hint: null };
const broken = { works: false, hibernateAvailable: false, hint: "enable wake timers with powercfg" };
const now = new Date(2026, 9, 18, 20, 0, 0);
const tty = (over = {}) => ({ tty: true, plan: [plainItem], power: working, now, ...over });
const ids = (list) => list.map((q) => q.id);

// Every output chunk without a trailing newline is a prompt; the next scripted line is sent after it
function scripted(lines) {
  const queue = [...lines];
  const input = new PassThrough();
  let text = "";
  const output = new Writable({
    write(chunk, _enc, done) {
      const s = chunk.toString();
      text += s;
      if (!s.endsWith("\n")) setImmediate(() => input.write(`${queue.shift() ?? ""}\n`));
      done();
    },
  });
  return { io: { input, output }, written: () => text };
}

test("each flag removes its question", () => {
  const all = ids(questions({}, tty({ plan: [manualItem, plainItem] })));
  for (const id of ["items", "manual", "on-limit", "shutdown", "start", "until"]) assert.ok(all.includes(id), id);

  const some = ids(questions({ items: "2", "on-limit": "wait", "no-shutdown": true }, tty({ plan: [manualItem, plainItem] })));
  for (const id of ["items", "on-limit", "shutdown"]) assert.ok(!some.includes(id), `${id} should be skipped`);
  for (const id of ["start", "until"]) assert.ok(some.includes(id), `${id} should remain`);

  const positional = ids(questions({ only: 400 }, tty({ plan: [plainItem] })));
  assert.ok(!positional.includes("items"));
});

test("no tty asks nothing", async () => {
  assert.deepEqual(questions({}, tty({ tty: false })), []);
  const { io, written } = scripted([]);
  const result = await runWizard({ items: "2" }, tty({ tty: false }), io);
  assert.equal(result.run, true);
  assert.equal(result.flags.items, "2");
  assert.equal(written(), "");
});

test("invalid answer asked again", async () => {
  const picked = questions({}, tty()).filter((q) => q.id === "start" || q.id === "until");
  assert.deepEqual(ids(picked), ["start", "until"]);
  const { io, written } = scripted(["25:99", "23:00", ""]);
  const answers = await ask(picked, io);
  assert.equal(answers.start, "23:00");
  assert.ok(!("until" in answers));
  assert.ok(written().includes("25:99"));
});

test("limit question shows timer state", () => {
  const limit = questions({}, tty({ power: broken })).find((q) => q.id === "on-limit");
  assert.ok(limit);
  assert.match(limit.prompt, /unavailable/i);
  assert.ok(limit.prompt.includes(broken.hint));
  assert.equal(limit.default, "b");
  assert.ok(limit.parse("a").error);
  assert.equal(limit.parse("b").flags["on-limit"], "wait");
  assert.equal(questions({}, tty({ power: working })).find((q) => q.id === "on-limit").default, "a");
});

test("manual question only with manual items", () => {
  assert.ok(!ids(questions({}, tty({ plan: [plainItem] }))).includes("manual"));
  assert.ok(ids(questions({}, tty({ plan: [manualItem, plainItem] }))).includes("manual"));
});

test("summary and command round trip", () => {
  const answers = { items: "2", "skip-manual": true, "on-limit": "sleep", "max-wait": "12", shutdown: true, start: "23:00", until: "07:00" };
  const text = summary(answers, tty());
  const windowLine = text.split(/\r?\n/).find((l) => l.includes("2026-10-19") && l.includes("07:00"));
  assert.ok(windowLine, "window end shown as the next day's 07:00");

  const command = equivalentCommand(answers);
  assert.ok(command.startsWith("node scripts/autobuild.mjs "));
  assert.deepEqual(parseFlags(command.split(/\s+/).slice(2)), answers);
});

test("summary shows area", () => {
  const plan = queueOf([
    { number: 1, title: "Helper item", status: "Todo", area: "Scripts", epic: false, parent: null, autobuild: false },
    { number: 2, title: "Area epic", status: "Todo", area: null, epic: true, parent: null, autobuild: false, subIssues: [3, 4] },
    { number: 3, title: "Epic backend", status: "Todo", area: "Backend", epic: false, parent: 2, autobuild: false },
    { number: 4, title: "Epic frontend", status: "Todo", area: "Frontend", epic: false, parent: 2, autobuild: false },
  ]);
  const ctx = { tty: true, plan, power: working, now };
  const lineOf = (text, n) => text.split(/\r?\n/).find((l) => l.includes(`#${n} `));

  const items = questions({}, ctx).find((q) => q.id === "items");
  assert.match(lineOf(items.prompt, 1), /Helper item.*Scripts/);
  assert.match(lineOf(items.prompt, 2), /Area epic.*Backend → Frontend/);

  const text = summary({ items: "all", "on-limit": "wait", "max-wait": "12", shutdown: true, start: "now" }, ctx);
  assert.match(lineOf(text, 1), /Helper item.*Scripts/);
  assert.match(lineOf(text, 2), /Area epic.*Backend → Frontend/);
});

test("summary shows priority", () => {
  const plan = queueOf([{ number: 1, title: "Helper item", status: "Todo", area: "Scripts", priority: "P1", epic: false, parent: null, autobuild: false }]);
  const ctx = { tty: true, plan, power: working, now };
  const lineOf = (text, n) => text.split(/\r?\n/).find((l) => l.includes(`#${n} `));

  const items = questions({}, ctx).find((q) => q.id === "items");
  assert.match(lineOf(items.prompt, 1), /Helper item.*\[P1 · Scripts\]/);

  const text = summary({ items: "all", "on-limit": "wait", "max-wait": "12", shutdown: true, start: "now" }, ctx);
  assert.match(lineOf(text, 1), /Helper item.*\[P1 · Scripts\]/);
});

test("declined wizard starts nothing", async () => {
  const flags = { items: "all", "on-limit": "wait", "max-wait": "12", shutdown: true, start: "now" };
  const declined = scripted(["", "n"]);
  const result = await runWizard(flags, tty(), declined.io);
  assert.equal(result.run, false);
  assert.ok(declined.written().includes("Start?"));

  const accepted = scripted(["", ""]);
  const go = await runWizard(flags, tty(), accepted.io);
  assert.equal(go.run, true);
  assert.equal(go.flags.items, "all");
});

test("docs describe the wizard", () => {
  for (const file of ["skarbiec-plan/workflow.md", "CLAUDE.md"]) {
    assert.match(readFileSync(path.join(root, file), "utf8"), /wizard|kreator/i, file);
  }
});
