import assert from "node:assert/strict";
import { writeFileSync } from "node:fs";
import { test } from "node:test";
import { finishShutdown } from "./autobuild-limit.mjs";
import { postRunVerify } from "./autobuild-verify.mjs";

test("not on master or dirty tree is not verified", async () => {
  for (const extra of [{ branch: "feat/part-2" }, { branch: "master", dirty: 2 }]) {
    const verify = verifyFakes({ ok: true, failures: [] });
    const line = await postRunVerify({ merged: 1, dryRun: false, run: verify.run, log: verify.log, ...extra });
    assert.equal(verify.calls.length, 0);
    assert.match(line, /^verify --all na master: nie uruchomiono — /);
    assert.ok(verify.logs.includes(line));
  }
});

function verifyFakes(result) {
  const calls = [];
  const logs = [];
  return {
    calls,
    logs,
    log: (line) => logs.push(line),
    run: (cmd, argv) => {
      calls.push([cmd, ...argv]);
      writeFileSync(argv[argv.indexOf("--out") + 1], JSON.stringify(result));
      return { ok: result.ok, code: result.ok ? 0 : 1 };
    },
  };
}

test("runs once after the merges, before the shutdown", async () => {
  const order = [];
  const verify = verifyFakes({ ok: true, failures: [] });
  const line = await postRunVerify({
    merged: 2,
    dryRun: false,
    run: (cmd, argv) => (order.push("verify"), verify.run(cmd, argv)),
    log: verify.log,
  });
  await finishShutdown({ confirm: async () => (order.push("shutdown"), "no"), run: () => {}, log: () => {} });
  assert.equal(verify.calls.length, 1);
  assert.ok(verify.calls[0].includes("--all"));
  assert.ok(verify.calls[0].includes("--out"));
  assert.equal(line, "verify --all na master: zielone");
  assert.ok(verify.logs.includes(line));
  assert.deepEqual(order, ["verify", "shutdown"]);
});

test("skipped without a merge", async () => {
  for (const input of [{ merged: 0, dryRun: false }, { merged: 3, dryRun: true }]) {
    const verify = verifyFakes({ ok: true, failures: [] });
    const line = await postRunVerify({ ...input, run: verify.run, log: verify.log });
    assert.equal(verify.calls.length, 0);
    assert.equal(verify.logs.some((l) => l.includes("verify --all")), false);
    assert.ok(!line);
  }
});

test("red result is reported only", async () => {
  const verify = verifyFakes({
    ok: false,
    failures: [
      { step: "test", summary: "2 test(s) failed", file: "services/Portfolio/Skarbiec.Portfolio.Tests/AddAssetEndpointTests.cs" },
      { step: "test", summary: "1 test(s) failed", file: "services/Reporting/Skarbiec.Reporting.Tests/ReportTests.cs" },
    ],
  });
  const exitCodeBefore = process.exitCode;
  const line = await postRunVerify({ merged: 1, dryRun: false, run: verify.run, log: verify.log });
  assert.equal(line, "verify --all na master: czerwone — Portfolio, Reporting");
  assert.ok(verify.logs.includes(line));
  assert.equal(verify.calls.length, 1);
  assert.equal(process.exitCode, exitCodeBefore);
  let asked = 0;
  await finishShutdown({ confirm: async () => (asked++, "no"), run: () => {}, log: () => {} });
  assert.equal(asked, 1);
});

test("every end reason", async () => {
  for (const reason of ["window", "limit", "stopped", "done"]) {
    const verify = verifyFakes({ ok: true, failures: [] });
    const line = await postRunVerify({ merged: 1, dryRun: false, reason, run: verify.run, log: verify.log });
    assert.equal(verify.calls.length, 1, reason);
    assert.equal(line, "verify --all na master: zielone", reason);
  }
});

test("verify cannot start", async () => {
  const logs = [];
  const line = await postRunVerify({
    merged: 1,
    dryRun: false,
    run: () => {
      throw new Error("spawn node ENOENT");
    },
    log: (l) => logs.push(l),
  });
  assert.match(line, /^verify --all na master: nie uruchomiono — .+/);
  assert.ok(logs.includes(line));
});
