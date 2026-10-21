import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import { MEASURE_ARGS, collectEpicFiles, measureAfterEpic, touchesTests } from "./autobuild-timings.mjs";

const root = fileURLToPath(new URL("..", import.meta.url));

test("touchesTests", () => {
  const table = [
    [["services/Portfolio/Skarbiec.Portfolio/Features/Assets/AddAsset.cs"], true],
    [["contracts/Skarbiec.Contracts/Money.cs"], true],
    [["gateway/Skarbiec.Gateway/Program.cs"], true],
    [["web/src/app/app.ts"], true],
    [["Skarbiec.ServiceDefaults/Tenancy/Tenant.cs"], true],
    [["Skarbiec.Testing/ApiFactory.cs"], true],
    [["Skarbiec.Testing.Tests/FixtureTests.cs"], true],
    [["Elsewhere/Skarbiec.Other.Tests/Some.cs"], true],
    [["tools/thing/foo.spec.ts"], true],
    [["scripts\\a.mjs", "docs\\guide.md", "web\\src\\x.ts"], true],
    [["e2e/tests/accounts.spec.ts"], false],
    [["e2e/stub/server.mjs", "e2e/package.json"], false],
    [["scripts/autobuild.mjs", "scripts/autobuild.test.mjs"], false],
    [[".claude/rules/testing.md", ".claude/agents/implementer.md"], false],
    [["skarbiec-plan/decisions.md"], false],
    [["README.md", "docs/guide.md", "CLAUDE.md", ".github/workflows/ci.yml"], false],
    [["e2e/tests/a.spec.ts", "scripts/x.mjs", "skarbiec-plan/x.md", ".claude/x.md"], false],
    [["scripts/x.mjs", "services/Identity/Skarbiec.Identity/Program.cs"], true],
    [[], false],
  ];
  for (const [paths, expected] of table) assert.equal(touchesTests(paths), expected, paths.join(", "));
});

function ghFake({ pulls, files }) {
  const calls = [];
  const gh = (argv) => {
    calls.push(argv);
    const url = argv[1];
    const list = /pulls\?state=closed&head=owner%3A(.+)$/.exec(url);
    if (list) return { ok: true, out: (pulls[decodeURIComponent(list[1])] ?? []).join("\n"), firstErr: "" };
    const filed = /pulls\/(\d+)\/files$/.exec(url);
    if (filed) {
      assert.ok(argv.includes("--paginate"), "files are paginated");
      return { ok: true, out: (files[filed[1]] ?? []).join("\n"), firstErr: "" };
    }
    return { ok: false, out: "", firstErr: `unexpected ${url}` };
  };
  return { gh, calls };
}

test("an epic's files are the union of its parts' merged PRs", () => {
  const { gh, calls } = ghFake({
    pulls: { "feat/part-1": ["101"], "feat/part-2": ["102"], "feat/part-3": ["103", "104"] },
    files: {
      101: ["scripts/test-timings.mjs", "scripts/test-timings.test.mjs"],
      102: ["scripts/test-timings.mjs", "skarbiec-plan/notes.md"],
      103: ["scripts/autobuild.mjs"],
      104: ["services/Portfolio/Skarbiec.Portfolio/Program.cs"],
    },
  });
  const branches = { 1: "feat/part-1", 2: "feat/part-2", 3: "feat/part-3" };
  const files = collectEpicFiles({ parts: [1, 2, 3], branchOf: (n) => branches[n], gh, repo: "owner/skarbiec" });
  assert.deepEqual(files, [
    "scripts/autobuild.mjs",
    "scripts/test-timings.mjs",
    "scripts/test-timings.test.mjs",
    "services/Portfolio/Skarbiec.Portfolio/Program.cs",
    "skarbiec-plan/notes.md",
  ]);
  assert.equal(calls.filter((c) => c[1].endsWith("/files")).length, 4);
  assert.ok(calls.every((c) => c[0] === "api"));
});

test("an epic part without a merged PR or Branch is an error", () => {
  const { gh } = ghFake({ pulls: {}, files: {} });
  assert.throws(() => collectEpicFiles({ parts: [1], branchOf: () => null, gh, repo: "owner/skarbiec" }), /#1 has no Branch/);
  assert.throws(() => collectEpicFiles({ parts: [1], branchOf: () => "feat/x", gh, repo: "owner/skarbiec" }), /#1 has no merged PR/);
  const failing = () => ({ ok: false, out: "", firstErr: "HTTP 502" });
  assert.throws(() => collectEpicFiles({ parts: [1], branchOf: () => "feat/x", gh: failing, repo: "owner/skarbiec" }), /HTTP 502/);
});

function hookFakes({ files = ["services/Portfolio/X.cs"], result = { ok: true, code: 0, out: "Razem 10:00 (brak wiarygodnej historii do porównania), testów 100, 0 czerwonych." } } = {}) {
  const runs = [];
  const logs = [];
  const reads = [];
  return {
    runs,
    logs,
    reads,
    files: () => (reads.push(1), files),
    log: (line) => logs.push(line),
    run: (cmd, argv, options) => {
      runs.push([cmd, ...argv]);
      assert.ok(options.timeout > 60 * 60e3, "the child is bounded");
      if (result instanceof Error) throw result;
      return result;
    },
  };
}

const epicInput = { number: 542, epic: true, dryRun: false };

test("measures after an epic that touched tests", async () => {
  const fakes = hookFakes();
  const line = await measureAfterEpic({ ...epicInput, ...fakes });
  assert.deepEqual(fakes.runs, [["node", "scripts/test-timings.mjs", "--from-autobuild", "--wait", "15", "--timeout", "45"]]);
  assert.deepEqual(MEASURE_ARGS.slice(1), ["--from-autobuild", "--wait", "15", "--timeout", "45"]);
  assert.match(line, /^pomiar czasów testów: #542 zakończony — Razem 10:00/);
  assert.deepEqual(fakes.logs, [line]);
});

test("skipped for an epic without test changes", async () => {
  const fakes = hookFakes({ files: ["scripts/autobuild.mjs", "skarbiec-plan/x.md", ".claude/rules/testing.md", "e2e/tests/a.spec.ts", "README.md"] });
  const line = await measureAfterEpic({ ...epicInput, ...fakes });
  assert.equal(fakes.runs.length, 0);
  assert.match(line, /^pomiar czasów testów: #542 pominięty — epik nie zmienił testów ani logiki/);
  assert.deepEqual(fakes.logs, [line]);
});

test("skipped for a standalone issue", async () => {
  const fakes = hookFakes();
  const line = await measureAfterEpic({ ...epicInput, epic: false, ...fakes });
  assert.equal(fakes.runs.length, 0);
  assert.equal(fakes.reads.length, 0);
  assert.match(line, /pominięty — to nie epik/);
  assert.deepEqual(fakes.logs, [line]);
});

test("skipped in a dry run", async () => {
  const fakes = hookFakes();
  const line = await measureAfterEpic({ ...epicInput, dryRun: true, ...fakes });
  assert.equal(fakes.runs.length, 0);
  assert.equal(fakes.reads.length, 0);
  assert.match(line, /pominięty — dry-run/);
  assert.deepEqual(fakes.logs, [line]);
});

test("skipped on a dirty tree or off master", async () => {
  for (const [extra, reason] of [
    [{ branch: "feat/part-3" }, /nie na masterze \(feat\/part-3\)/],
    [{ branch: "master", dirty: 2 }, /master ma 2 niezatwierdzonych plików/],
  ]) {
    const fakes = hookFakes();
    const line = await measureAfterEpic({ ...epicInput, ...extra, ...fakes });
    assert.equal(fakes.runs.length, 0);
    assert.equal(fakes.reads.length, 0);
    assert.match(line, reason);
    assert.deepEqual(fakes.logs, [line]);
  }
});

test("a failed measurement is only logged", async () => {
  const exitCodeBefore = process.exitCode;
  const cases = [
    [{ ok: false, code: 3, out: "działa: runner wsl-1 (zajęty)\nMaszyna nadal zajęta" }, /niepełny \(kod 3.*— działa: runner wsl-1/],
    [{ ok: false, code: 1, out: "Razem 5:00, testów 10, 2 czerwonych." }, /są czerwone testy \(kod 1\)/],
    [{ ok: false, code: 2, error: null, firstErr: "unknown argument" }, /nie powiódł się — unknown argument/],
    [{ ok: false, code: null, error: new Error("spawnSync node ETIMEDOUT") }, /nie powiódł się — spawnSync node ETIMEDOUT/],
    [new Error("spawn node ENOENT"), /nie powiódł się — spawn node ENOENT/],
  ];
  for (const [result, expected] of cases) {
    const fakes = hookFakes({ result });
    const line = await measureAfterEpic({ ...epicInput, ...fakes });
    assert.equal(fakes.logs.length, 1);
    assert.match(line, expected);
    assert.equal(fakes.logs[0], line);
  }
  const unreadable = hookFakes();
  const line = await measureAfterEpic({
    ...epicInput,
    ...unreadable,
    files: () => {
      throw new Error("#9 has no merged PR on feat/x");
    },
  });
  assert.equal(unreadable.runs.length, 0);
  assert.match(line, /nie powiódł się — #9 has no merged PR/);
  assert.equal(process.exitCode, exitCodeBefore);
});

test("measures even when the window is closed", async () => {
  const fakes = hookFakes();
  const line = await measureAfterEpic({ ...epicInput, until: new Date(Date.now() - 3600e3), ...fakes });
  assert.equal(fakes.runs.length, 1);
  assert.match(line, /zakończony/);

  const source = readFileSync(path.join(root, "scripts", "autobuild.mjs"), "utf8");
  const hook = /async function finishEpic[\s\S]*?\n}\n/.exec(source)[0];
  assert.match(hook, /epicMeasurement\(/);
  assert.doesNotMatch(hook, /assertWindowOpen|ctx\.until|mayStart/);
  const measurement = /function epicMeasurement[\s\S]*?\n}\n/.exec(source)[0];
  assert.doesNotMatch(measurement, /assertWindowOpen|ctx\.until|mayStart|buildThroughHangs|awake/i);
});

test("the post-epic measurement is documented once in the header and CLAUDE.md", () => {
  const read = (f) => readFileSync(path.join(root, f), "utf8");
  const header = read("scripts/autobuild.mjs").split("\n").slice(0, 40).filter((l) => l.startsWith("//"));
  assert.equal(header.filter((l) => l.includes("--from-autobuild --wait 15 --timeout 45")).length, 1);
  const claude = read("CLAUDE.md").split("\n");
  const autobuildRow = claude.filter((l) => l.includes("`node scripts/autobuild.mjs"));
  assert.equal(autobuildRow.length, 1);
  assert.match(autobuildRow[0], /--from-autobuild --wait 15 --timeout 45/);
  const timingsRows = claude.filter((l) => l.includes("`node scripts/test-timings.mjs"));
  assert.equal(timingsRows.length, 1);
  for (const flag of ["--wait <min>", "--timeout <min>", "--from-autobuild"]) assert.ok(timingsRows[0].includes(flag), flag);
});
