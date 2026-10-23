import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { buildState, createSandbox } from "./fake-gh-sandbox.mjs";

const BRANCH = "feat/outside-area";

test("outsideArea in the report", () => {
  const sandbox = createSandbox(
    buildState({ issues: [{ number: 50, status: "In progress", labels: ["spec"], area: "Backend", branch: BRANCH }] }),
    { branch: BRANCH },
  );
  try {
    fs.mkdirSync(path.join(sandbox.dir, "web", "src", "app"), { recursive: true });
    fs.writeFileSync(path.join(sandbox.dir, "web", "src", "app", "a.ts"), "export {};\n");
    fs.mkdirSync(path.join(sandbox.dir, "skarbiec-plan"), { recursive: true });
    fs.writeFileSync(path.join(sandbox.dir, "skarbiec-plan", "workflow.md"), "# workflow\n");

    const run = sandbox.run("ship.mjs", [
      "50",
      "--branch",
      BRANCH,
      "--title",
      "Outside area",
      "--json",
      JSON.stringify({ rounds: 0, reviewRan: true, minor: [] }),
      "--dry-run",
    ]);

    const line = run.stdout.split(/\r?\n/).find((l) => l.startsWith("would post: gh-project.mjs report 50 --json "));
    assert.ok(line, run.out);
    const posted = JSON.parse(line.slice(line.indexOf("--json ") + "--json ".length));
    assert.deepEqual([...(posted.outsideArea ?? [])].sort(), ["skarbiec-plan/workflow.md", "web/src/app/a.ts"]);
  } finally {
    sandbox.cleanup();
  }
});

test("gate argv", async () => {
  const { gateCommand } = await import("./ship.mjs");

  assert.equal(
    gateCommand({ e2e: true, lastBuild: false }),
    "node scripts/verify.mjs --projects e2e --cache-check && node scripts/e2e.mjs check",
  );
  assert.equal(gateCommand({ e2e: false, lastBuild: true }), "node scripts/verify.mjs --all --cache-check");
  assert.equal(gateCommand({ e2e: false, lastBuild: false }), "node scripts/verify.mjs --cache-check");
});

const FIX_BRANCH = "feat/fix-ci";
const GREEN = 'console.log(\'VERIFY_RESULT: {"ok":true,"failures":[]}\');\n';
const RED = 'console.log(\'VERIFY_RESULT: {"ok":false,"failures":[]}\');\nprocess.exit(2);\n';

function gitIn(dir, argv) {
  const res = spawnSync("git", ["-c", "user.name=t", "-c", "user.email=t@t", ...argv], { cwd: dir, encoding: "utf8", windowsHide: true });
  assert.equal(res.status, 0, res.stderr);
  return res.stdout.trim();
}

function fixSandbox({ verify = GREEN, branch = FIX_BRANCH, prs = [{ number: 77, headRefName: FIX_BRANCH }], body = "" } = {}) {
  const sandbox = createSandbox(
    buildState({ issues: [{ number: 50, status: "In progress", labels: ["spec"], area: "Scripts", branch: FIX_BRANCH, body }], prs }),
    { branch },
  );
  fs.writeFileSync(path.join(sandbox.dir, "scripts", "verify.mjs"), verify);
  gitIn(sandbox.dir, ["add", "-A"]);
  gitIn(sandbox.dir, ["commit", "-m", "stub verify"]);
  return sandbox;
}

const FIX_REPORT = JSON.stringify({ jobs: ["reporting"], cause: "an unused using broke the build", changed: "removed the using" });

function fixShip(sandbox, extra = [], branch = FIX_BRANCH) {
  return sandbox.run("ship.mjs", ["50", "--fix-pr", "--branch", branch, "--title", "Autobuild CI repair", "--json", FIX_REPORT, ...extra]);
}

const resultOf = (run) => JSON.parse(run.stdout.trim().split(/\r?\n/).at(-1).slice("SHIP_RESULT: ".length));
const originOf = (sandbox) => path.join(sandbox.dir, "..", `${path.basename(sandbox.dir)}-origin.git`);

function dirty(sandbox) {
  fs.mkdirSync(path.join(sandbox.dir, "web"), { recursive: true });
  fs.writeFileSync(path.join(sandbox.dir, "web", "a.ts"), "export {};\n");
  fs.writeFileSync(path.join(sandbox.dir, "scripts", "fixed.mjs"), "export {};\n");
}

test("--fix-pr dry run plans no pr create", () => {
  const sandbox = fixSandbox();
  try {
    dirty(sandbox);
    const run = fixShip(sandbox, ["--dry-run"]);

    assert.equal(run.code, 0, run.out);
    const planned = run.stdout.split(/\r?\n/).filter((l) => l.startsWith("would run: "));
    assert.ok(planned.some((l) => l === "would run: git add -A"), run.out);
    assert.ok(planned.some((l) => l.startsWith("would run: git commit") && l.includes("Fix CI: Autobuild CI repair") && l.includes("Spec: #50")), run.out);
    assert.ok(planned.some((l) => l === `would run: git push origin ${FIX_BRANCH}`), run.out);
    assert.ok(planned.some((l) => l.startsWith("would run: gh pr comment 77") && l.includes("skarbiec-ci-fix")), run.out);
    assert.ok(!run.stdout.includes("pr create"));
    assert.ok(!planned.some((l) => /--force|--force-with-lease| -f( |$)/.test(l)));
    assert.ok(run.stdout.includes("verify cache: green"));
    assert.equal(sandbox.log().filter((e) => e.kind === "prComment").length, 0);
  } finally {
    sandbox.cleanup();
  }
});

test("--fix-pr commits, pushes without force and comments with the marker", () => {
  const sandbox = fixSandbox();
  try {
    dirty(sandbox);
    const before = gitIn(sandbox.dir, ["rev-parse", "HEAD"]);
    const run = fixShip(sandbox);

    assert.equal(run.code, 0, run.out);
    const result = resultOf(run);
    assert.equal(result.ok, true);
    assert.equal(result.commentPosted, true);
    assert.equal(result.prUrl, "https://github.com/Maliniak93/FinMel/pull/77");
    const head = gitIn(sandbox.dir, ["rev-parse", "HEAD"]);
    assert.notEqual(head, before);
    assert.equal(gitIn(originOf(sandbox), ["rev-parse", `refs/heads/${FIX_BRANCH}`]), head);
    const message = gitIn(sandbox.dir, ["log", "-1", "--format=%B"]);
    assert.ok(message.startsWith("Fix CI: Autobuild CI repair"), message);
    assert.ok(message.includes("Spec: #50") && message.includes("Co-Authored-By: Claude"), message);

    const log = sandbox.log();
    const comments = log.filter((e) => e.kind === "prComment");
    assert.equal(comments.length, 1);
    assert.equal(comments[0].number, 77);
    for (const part of ["<!-- skarbiec-ci-fix -->", "Failing job(s):** reporting", "Cause:** an unused using broke the build", "Changed:** removed the using", "**Poza obszarem:** web/a.ts", "Verify:** green"]) {
      assert.ok(comments[0].body.includes(part), part);
    }
    assert.ok(!log.some((e) => e.args?.[0] === "pr" && e.args?.[1] === "create"));
    assert.ok(!log.some((e) => e.kind === "comment"), "no run report on the issue");
  } finally {
    sandbox.cleanup();
  }
});

test("--fix-pr refuses master and a wrong lane", () => {
  const onMaster = fixSandbox({ branch: "" });
  try {
    dirty(onMaster);
    const run = fixShip(onMaster, [], "master");
    assert.equal(run.code, 2, run.out);
    assert.equal(resultOf(run).failedCommand, "guard");
    assert.match(resultOf(run).error, /refusing to ship branch master/);
    assert.equal(onMaster.log().filter((e) => e.kind === "prComment").length, 0);
  } finally {
    onMaster.cleanup();
  }

  const sandbox = fixSandbox();
  try {
    dirty(sandbox);
    const wrong = fixShip(sandbox, [], "feat/other");
    assert.equal(wrong.code, 2, wrong.out);
    assert.match(resultOf(wrong).error, /current branch is feat\/fix-ci, expected feat\/other/);
    const chore = fixShip(sandbox, [], "docs/lane");
    assert.equal(chore.code, 2, chore.out);
    assert.equal(sandbox.log().filter((e) => e.kind === "prComment").length, 0);
  } finally {
    sandbox.cleanup();
  }
});

test("--fix-pr refuses a tree verify has not proven green", () => {
  const sandbox = fixSandbox({ verify: RED });
  try {
    dirty(sandbox);
    const before = gitIn(sandbox.dir, ["rev-parse", "HEAD"]);
    const run = fixShip(sandbox);

    assert.equal(run.code, 2, run.out);
    assert.match(resultOf(run).error, /not proven green/);
    assert.equal(gitIn(sandbox.dir, ["rev-parse", "HEAD"]), before);
    assert.equal(sandbox.log().filter((e) => e.kind === "prComment").length, 0);
  } finally {
    sandbox.cleanup();
  }
});

test("--fix-pr fails on nothing to commit and on a missing PR", () => {
  const clean = fixSandbox();
  try {
    const run = fixShip(clean);
    assert.equal(run.code, 2, run.out);
    assert.equal(resultOf(run).failedCommand, "git commit");
    assert.match(resultOf(run).error, /nothing to commit/);
    assert.equal(clean.log().filter((e) => e.kind === "prComment").length, 0);
  } finally {
    clean.cleanup();
  }

  const noPr = fixSandbox({ prs: [] });
  try {
    dirty(noPr);
    const before = gitIn(noPr.dir, ["rev-parse", "HEAD"]);
    const run = fixShip(noPr);
    assert.equal(run.code, 2, run.out);
    assert.equal(resultOf(run).failedCommand, "gh pr view");
    assert.equal(gitIn(noPr.dir, ["rev-parse", "HEAD"]), before);
  } finally {
    noPr.cleanup();
  }
});

const DESTRUCTIVE_MIGRATION = `public partial class Drop : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Accounts");
    }
}
`;
const CONSENT_BODY = "## Goal\n\nx\n\n## Utrata danych\n\n- Portfolio.Accounts\n\nZgoda: tak, dane testowe\n";
const SHIP_REPORT = JSON.stringify({ rounds: 0, reviewRan: true, minor: [] });

function addMigration(sandbox) {
  const dir = path.join(sandbox.dir, "services", "Portfolio", "Skarbiec.Portfolio", "Migrations");
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(path.join(dir, "20270101000000_Drop.cs"), DESTRUCTIVE_MIGRATION);
}

const shipNormal = (sandbox, extra = []) => sandbox.run("ship.mjs", ["50", "--branch", FIX_BRANCH, "--title", "Drop accounts", "--json", SHIP_REPORT, ...extra]);
const gitCalls = (sandbox) => sandbox.log().filter((e) => e.args?.[0] === "pr" && e.args?.[1] === "create");

test("destructive migration without consent blocks", () => {
  const sandbox = fixSandbox({ body: "## Goal\n\nx\n" });
  try {
    addMigration(sandbox);
    const before = gitIn(sandbox.dir, ["rev-parse", "HEAD"]);

    const normal = shipNormal(sandbox);
    assert.equal(normal.code, 2, normal.out);
    assert.equal(resultOf(normal).failedCommand, "migration-guard");
    assert.match(resultOf(normal).error, /Portfolio\.Accounts/);
    assert.match(resultOf(normal).error, /Utrata danych/);
    assert.match(resultOf(normal).error, /Przeniesienie danych/);
    const reports = sandbox.log().filter((e) => e.kind === "comment");
    assert.equal(reports.length, 1);
    assert.match(reports[0].body, /Portfolio\.Accounts/);
    assert.equal(gitIn(sandbox.dir, ["rev-parse", "HEAD"]), before);
    assert.equal(gitCalls(sandbox).length, 0);

    const fix = fixShip(sandbox);
    assert.equal(fix.code, 2, fix.out);
    assert.equal(resultOf(fix).failedCommand, "migration-guard");
    assert.match(resultOf(fix).error, /Portfolio\.Accounts/);
    assert.equal(gitIn(sandbox.dir, ["rev-parse", "HEAD"]), before);
    assert.equal(sandbox.log().filter((e) => e.kind === "prComment").length, 0);

    const dry = shipNormal(sandbox, ["--dry-run"]);
    assert.equal(dry.code, 2, dry.out);
    assert.match(dry.stdout, /migration guard: .*Portfolio\.Accounts/);
  } finally {
    sandbox.cleanup();
  }
});

test("destructive migration with consent ships", () => {
  const sandbox = fixSandbox({ body: CONSENT_BODY });
  try {
    addMigration(sandbox);

    const dry = shipNormal(sandbox, ["--dry-run"]);
    assert.equal(dry.code, 0, dry.out);
    assert.match(dry.stdout, /migration guard: ok/);

    const fix = fixShip(sandbox);
    assert.equal(fix.code, 0, fix.out);
    assert.equal(resultOf(fix).ok, true);
  } finally {
    sandbox.cleanup();
  }
});

test("a rewritten migration blocks whatever the body says", () => {
  const sandbox = fixSandbox({ body: CONSENT_BODY });
  try {
    const dir = path.join(sandbox.dir, "services", "Portfolio", "Skarbiec.Portfolio", "Migrations");
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(path.join(dir, "20260101000000_Old.cs"), "// old\n");
    gitIn(sandbox.dir, ["add", "-A"]);
    gitIn(sandbox.dir, ["commit", "-m", "old migration"]);
    gitIn(sandbox.dir, ["branch", "-f", "master", "HEAD"]);
    fs.writeFileSync(path.join(dir, "20260101000000_Old.cs"), "// edited\n");

    const fix = fixShip(sandbox);
    assert.equal(fix.code, 2, fix.out);
    assert.equal(resultOf(fix).failedCommand, "migration-guard");
    assert.match(resultOf(fix).error, /append-only/);
  } finally {
    sandbox.cleanup();
  }
});
