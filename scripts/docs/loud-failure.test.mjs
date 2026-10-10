import assert from "node:assert/strict";
import { existsSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { test } from "node:test";
import { makeSandbox, runFacts } from "./helpers.mjs";

function assertNothingWritten(out) {
  assert.ok(!existsSync(out) || readdirSync(out).length === 0, `${out} must stay empty`);
}

test("facts fails naming the service when its Features folder holds no endpoint", () => {
  const sb = makeSandbox();
  try {
    rmSync(path.join(sb.root, "services", "Reporting", "Skarbiec.Reporting", "Features"), { recursive: true });
    const run = runFacts(sb);
    assert.notEqual(run.status, 0);
    assert.match(run.output, /Reporting/);
    assert.match(run.output, /endpoint/i);
    assertNothingWritten(sb.out);
  } finally {
    sb.cleanup();
  }
});

test("facts fails naming ReverseProxy when the gateway config has none", () => {
  const sb = makeSandbox();
  try {
    writeFileSync(path.join(sb.root, "gateway", "Skarbiec.Gateway", "appsettings.json"), "{}");
    const run = runFacts(sb);
    assert.notEqual(run.status, 0);
    assert.match(run.output, /ReverseProxy/);
    assertNothingWritten(sb.out);
  } finally {
    sb.cleanup();
  }
});
