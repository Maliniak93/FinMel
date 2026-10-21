import assert from "node:assert/strict";
import { test } from "node:test";
import { activityLabel, createActivityTracker } from "./autobuild-activity.mjs";

const MIN = 60e3;

function fakeTime(start = new Date(2026, 9, 20, 1, 0, 0).getTime()) {
  let t = start;
  return { now: () => t, advance: (ms) => void (t += ms) };
}

const toolUse = (id, name, input, parent = "agent-1") => ({
  type: "assistant",
  parent_tool_use_id: parent,
  message: { content: [{ type: "tool_use", id, name, input }] },
});
const toolResult = (id, parent = "agent-1") => ({
  type: "user",
  parent_tool_use_id: parent,
  message: { content: [{ type: "tool_result", tool_use_id: id, content: "ok" }] },
});
const bgStart = (taskId, toolUseId, description = "raw command") => ({
  type: "system",
  subtype: "task_started",
  task_type: "local_bash",
  is_backgrounded: true,
  task_id: taskId,
  tool_use_id: toolUseId,
  description,
});
const bgEnd = (taskId, status, summary = 'Background command "raw command" completed') => ({ type: "system", subtype: "task_notification", task_id: taskId, status, summary });

const bash = (command, description) => activityLabel("Bash", description === undefined ? { command } : { command, description });

function setup() {
  const time = fakeTime();
  return { time, tracker: createActivityTracker({ now: time.now }) };
}

test("activityLabel table", () => {
  assert.equal(activityLabel("Edit", { file_path: "a.cs" }), "edycja kodu");
  assert.equal(activityLabel("Write", { file_path: "a.cs" }), "edycja kodu");
  assert.equal(activityLabel("NotebookEdit", {}), "edycja kodu");
  assert.equal(activityLabel("Read", { file_path: "a.cs" }), "czytanie kodu");
  assert.equal(activityLabel("Grep", { pattern: "x" }), "czytanie kodu");
  assert.equal(activityLabel("Glob", { pattern: "*.cs" }), "czytanie kodu");
  assert.equal(bash("node scripts/verify.mjs --fix --cache"), "weryfikacja (verify.mjs)");
  assert.equal(bash("cd /x && node scripts/verify.mjs --await .git/verify-result.json"), "czekanie na wynik weryfikacji");
  assert.equal(bash('dotnet test services/Portfolio/Skarbiec.Portfolio.Tests --filter "FullyQualifiedName~Foo"'), "dotnet test");
  assert.equal(bash("dotnet build Skarbiec.slnx"), "dotnet build");
  assert.equal(bash("dotnet format --verify-no-changes"), "dotnet format");
  assert.equal(bash("dotnet ef migrations add Init --project services/X"), "dotnet ef");
  assert.equal(bash("cd web && npm test"), "npm (web)");
  assert.equal(bash("npx vitest"), "npm (web)");
  assert.equal(bash("node scripts/ship.mjs 12 --branch b"), "ship");
  assert.equal(bash("node scripts/gh-project.mjs prepare 12"), "gh-project.mjs");
  assert.equal(bash("node scripts/preflight.mjs"), "preflight");
  assert.equal(bash("node scripts/stop-stack.mjs"), "stop-stack");
  assert.equal(activityLabel("mcp__github__get_issue", { issue_number: 3 }), "narzędzie mcp__github__get_issue");
  assert.equal(bash('node -e "console.log(1)"'), "skrypt node (bez opisu)");
  assert.equal(bash("ls -la /tmp"), "polecenie powłoki (bez opisu)");
});

test("e2e labels by flag", () => {
  assert.equal(bash("node scripts/e2e.mjs run"), "pełny zestaw e2e");
  assert.equal(bash("node scripts/e2e.mjs run --detach"), "pełny zestaw e2e");
  assert.equal(bash('node scripts/e2e.mjs run --grep "portfolio"'), "e2e (wybrane testy)");
  assert.equal(bash("node scripts/e2e.mjs run --await"), "czekanie na wynik e2e");
  assert.equal(bash("node scripts/e2e.mjs --await"), "czekanie na wynik e2e");
  for (const sub of ["up", "down", "status", "report"]) assert.equal(bash(`node scripts/e2e.mjs ${sub}`), `e2e (${sub})`);
});

test("labels never contain the raw command", () => {
  const commands = [
    "node scripts/verify.mjs --fix --cache --out .git/secret.json",
    'node scripts/e2e.mjs run --grep "hunter2"',
    "dotnet test services/Secret/Skarbiec.Secret.Tests --filter Name~Token",
    "npm run build-secret",
    "node scripts/ship.mjs 12 --title hunter2",
    "node -e \"process.exit(hunter2)\"",
    "echo hunter2",
  ];
  for (const command of commands) {
    const label = bash(command);
    for (const word of ["hunter2", "secret", "Secret", "--out", "--filter", "--fix", "--title"]) assert.ok(!label.includes(word), `${label} leaks ${word}`);
  }
});

test("description fills the gap", () => {
  const long = `Waiting   for the\ndate change ${"x".repeat(100)}`;
  const label = bash('node -e "wait()"', long);
  assert.equal(label.length, 70);
  assert.ok(label.startsWith("Waiting for the date change xxx"));
  assert.ok(label.endsWith("…"));
  assert.equal(bash("ls", "List files"), "List files");
});

test("known label beats description", () => {
  assert.equal(bash("node scripts/verify.mjs --fix", "Run the verification"), "weryfikacja (verify.mjs)");
  assert.equal(bash("node scripts/e2e.mjs run", "Run e2e"), "pełny zestaw e2e");
});

test("heartbeat names the current activity", () => {
  const { time, tracker } = setup();
  assert.equal(tracker.heartbeatTail({ tokens: 1000, tools: 1 }), "");
  tracker.observe(toolUse("t1", "Bash", { command: "node scripts/e2e.mjs run" }));
  time.advance(14 * MIN);
  assert.equal(tracker.heartbeatTail({ tokens: 2000, tools: 2 }), "teraz: pełny zestaw e2e (od 14 min)");
  tracker.observe(toolUse("t2", "Read", { file_path: "a" }));
  time.advance(30e3);
  assert.equal(tracker.heartbeatTail({ tokens: 3000, tools: 3 }), "teraz: czytanie kodu (od <1 min)");
});

test("first heartbeat before any tool call has no tail", () => {
  const { tracker } = setup();
  assert.equal(tracker.heartbeatTail({ tokens: 10, tools: 0 }), "");
  assert.equal(tracker.heartbeatTail({ tokens: 20, tools: 0 }), "");
});

test("main session tool calls are not subagent activity", () => {
  const { time, tracker } = setup();
  tracker.observe(toolUse("t1", "Bash", { command: "ls" }, null));
  time.advance(MIN);
  assert.equal(tracker.heartbeatTail({ tokens: 1, tools: 1 }), "");
});

test("stale activity says no new tools", () => {
  const { time, tracker } = setup();
  tracker.heartbeatTail({ tokens: 1000, tools: 5 });
  tracker.observe(toolUse("t1", "Edit", { file_path: "a" }));
  tracker.observe(toolResult("t1"));
  time.advance(12 * MIN);
  assert.equal(tracker.heartbeatTail({ tokens: 9000, tools: 5 }), "teraz: brak nowych narzędzi od 12 min");
});

test("a tool call still running is not stale", () => {
  const { time, tracker } = setup();
  tracker.heartbeatTail({ tokens: 1000, tools: 5 });
  tracker.observe(toolUse("t1", "Bash", { command: "node scripts/e2e.mjs run" }));
  time.advance(25 * MIN);
  assert.equal(tracker.heartbeatTail({ tokens: 1000, tools: 6 }), "teraz: pełny zestaw e2e (od 25 min)");
});

test("waiting on a background process", () => {
  const { time, tracker } = setup();
  tracker.observe(toolUse("t1", "Bash", { command: "node scripts/verify.mjs --fix", run_in_background: true }));
  assert.deepEqual(tracker.observe(bgStart("b1", "t1")), ["bg: weryfikacja (verify.mjs)"]);
  tracker.heartbeatTail({ tokens: 100, tools: 4 });
  time.advance(100 * MIN);
  assert.equal(tracker.heartbeatTail({ tokens: 100, tools: 4 }), "czeka na proces w tle od 1h 40m");
});

test("several background tasks", () => {
  const { time, tracker } = setup();
  tracker.observe(toolUse("t1", "Bash", { command: "a", description: "First" }));
  tracker.observe(toolUse("t2", "Bash", { command: "b", description: "Second" }));
  tracker.observe(bgStart("b1", "t1"));
  time.advance(30 * MIN);
  tracker.observe(bgStart("b2", "t2"));
  tracker.heartbeatTail({ tokens: 1, tools: 1 });
  time.advance(10 * MIN);
  assert.equal(tracker.heartbeatTail({ tokens: 1, tools: 1 }), "czeka na procesy w tle (2) od 40 min");
});

test("growth beats waiting", () => {
  const { time, tracker } = setup();
  tracker.observe(toolUse("t1", "Bash", { command: "a", description: "Long job" }));
  tracker.observe(bgStart("b1", "t1"));
  tracker.heartbeatTail({ tokens: 1, tools: 1 });
  tracker.observe(toolUse("t2", "Read", { file_path: "a" }));
  time.advance(10 * MIN);
  assert.equal(tracker.heartbeatTail({ tokens: 2, tools: 1 }), "teraz: czytanie kodu (od 10 min)");
  time.advance(10 * MIN);
  assert.equal(tracker.heartbeatTail({ tokens: 2, tools: 1 }), "czeka na proces w tle od 20 min");
});

test("no progress message", () => {
  const { time, tracker } = setup();
  tracker.heartbeatTail({ tokens: 500, tools: 3 });
  time.advance(10 * MIN);
  assert.equal(tracker.heartbeatTail({ tokens: 500, tools: 3 }), "bez postępu od 10 min");
  time.advance(10 * MIN);
  assert.equal(tracker.heartbeatTail({ tokens: 500, tools: 3 }), "bez postępu od 20 min");
  time.advance(10 * MIN);
  assert.equal(tracker.heartbeatTail({ tokens: 600, tools: 3 }), "");
  time.advance(10 * MIN);
  assert.equal(tracker.heartbeatTail({ tokens: 600, tools: 3 }), "bez postępu od 10 min");
});

test("bg start uses the label", () => {
  const { tracker } = setup();
  tracker.observe(toolUse("t1", "Bash", { command: 'node -e "const t=Date.UTC(2026,9,21)"' }));
  const lines = tracker.observe(bgStart("b1", "t1", 'node -e "const t=Date.UTC(2026,9,21)"'));
  assert.deepEqual(lines, ["bg: skrypt node (bez opisu)"]);
});

test("bg start with an unknown tool id", () => {
  const { tracker } = setup();
  assert.deepEqual(tracker.observe(bgStart("b1", "missing", "node -e secret")), ["bg: polecenie w tle (bez opisu)"]);
});

test("bg start ignores foreground tasks", () => {
  const { tracker } = setup();
  assert.deepEqual(tracker.observe({ ...bgStart("b1", "t1"), is_backgrounded: false }), []);
});

test("bg end statuses", () => {
  const { time, tracker } = setup();
  tracker.observe(toolUse("t1", "Bash", { command: "node scripts/e2e.mjs run" }));
  for (const [id, status, verb] of [["b1", "completed", "done"], ["b2", "stopped", "killed"], ["b3", "killed", "killed"], ["b4", "failed", "failed"], ["b5", "error", "failed"]]) {
    tracker.observe(bgStart(id, "t1"));
    time.advance(5 * MIN);
    assert.deepEqual(tracker.observe(bgEnd(id, status)), [`bg ${verb}: pełny zestaw e2e · 5 min`]);
  }
  tracker.observe(bgStart("b6", "t1"));
  time.advance(75 * MIN);
  assert.deepEqual(tracker.observe(bgEnd("b6", "completed")), ["bg done: pełny zestaw e2e · 1h 15m"]);
  tracker.observe(bgStart("b7", "t1"));
  assert.deepEqual(tracker.observe(bgEnd("b7", "completed")), ["bg done: pełny zestaw e2e · <1 min"]);
});

test("bg pairing by task id", () => {
  const { time, tracker } = setup();
  tracker.observe(toolUse("t1", "Bash", { command: "a", description: "First" }));
  tracker.observe(toolUse("t2", "Bash", { command: "b", description: "Second" }));
  tracker.observe(bgStart("b1", "t1"));
  time.advance(2 * MIN);
  tracker.observe(bgStart("b2", "t2"));
  time.advance(3 * MIN);
  assert.deepEqual(tracker.observe(bgEnd("b2", "completed")), ["bg done: Second · 3 min"]);
  assert.deepEqual(tracker.observe(bgEnd("b1", "completed")), ["bg done: First · 5 min"]);
  assert.deepEqual(tracker.observe(bgEnd("b1", "completed")), []);
  assert.deepEqual(tracker.observe(bgEnd("unknown", "completed")), []);
});

test("observe on a recorded stream", () => {
  const { time, tracker } = setup();
  const said = [];
  const feed = (obj) => said.push(...tracker.observe(obj));
  feed({ type: "system", subtype: "init", session_id: "s" });
  feed({ type: "assistant", parent_tool_use_id: null, message: { content: [{ type: "tool_use", id: "agent-1", name: "Agent", input: { description: "Run e2e" } }] } });
  feed(toolUse("sub-1", "Bash", { command: 'node -e "const t=Date.UTC(2026,9,21); wait(t)"', run_in_background: true }));
  feed(bgStart("task-1", "sub-1", 'node -e "const t=Date.UTC(2026,9,21); wait(t)"'));
  feed(toolResult("sub-1"));
  feed({ type: "system", subtype: "task_progress", task_id: "agent-1", description: "Running node -e …", usage: { total_tokens: 5, tool_uses: 1 } });
  time.advance(MIN);
  feed(toolUse("sub-2", "Bash", { command: "node scripts/e2e.mjs run --grep x" }));
  time.advance(2 * MIN);
  feed(bgEnd("task-1", "completed", 'Background command "node -e const t=…" completed'));
  assert.deepEqual(said, ["bg: skrypt node (bez opisu)", "bg done: skrypt node (bez opisu) · 3 min"]);
  assert.ok(said.every((line) => !line.includes("Date.UTC")));
  time.advance(MIN);
  assert.equal(tracker.heartbeatTail({ tokens: 5000, tools: 7 }), "teraz: e2e (wybrane testy) (od 3 min)");
});
