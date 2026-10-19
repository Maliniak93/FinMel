import assert from "node:assert/strict";
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
