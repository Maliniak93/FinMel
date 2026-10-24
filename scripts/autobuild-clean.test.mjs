import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import { cleanAtStart } from "./autobuild-clean.mjs";

const root = path.dirname(fileURLToPath(import.meta.url));
const MB = 1024 * 1024;
const report = {
  rows: [
    { category: "sesje Claude", files: 12, bytes: 5 * MB, skipped: 0 },
    { category: "pliki zgłoszeń", files: 0, bytes: 0, skipped: 3 },
  ],
};

function run(options) {
  const lines = [];
  const calls = [];
  cleanAtStart({ flags: {}, dryRun: false, say: (l) => lines.push(l), clean: (o) => (calls.push(o), report), ...options });
  return { lines, calls };
}

test("cleans before preflight, once, and survives errors", () => {
  const { lines, calls } = run({});
  assert.deepEqual(calls, [{ apply: true }]);
  assert.deepEqual(lines, ["czyszczenie: sesje Claude 12 plików, 5.0 MB", "czyszczenie: pliki zgłoszeń 0 plików, 0.0 MB, pominięto 3"]);

  const failed = run({
    clean: () => {
      throw new Error("EBUSY\nsecond line");
    },
  });
  assert.deepEqual(failed.lines, ["czyszczenie: błąd - EBUSY"]);

  const source = readFileSync(path.join(root, "autobuild.mjs"), "utf8");
  const clean = source.indexOf("cleanAtStart({");
  assert.ok(clean > 0);
  assert.equal(source.indexOf("cleanAtStart({", clean + 1), -1, "called once");
  assert.ok(clean < source.indexOf("if (!ctx.dryRun) runPreflight();"), "before the preflight");
  assert.ok(clean > source.indexOf("waitUntil(times.start);"), "after the start wait");
});

test("--no-clean and --dry-run run no cleaning", () => {
  assert.deepEqual(run({ flags: { "no-clean": true } }), { lines: [], calls: [] });
  assert.deepEqual(run({ dryRun: true }), { lines: [], calls: [] });
});

test("the usage header and the resume line carry --no-clean", () => {
  const source = readFileSync(path.join(root, "autobuild.mjs"), "utf8");
  assert.match(source.split("\n")[1], /\[--no-clean\]/);
  assert.match(source, /\/\/ {3}--no-clean /);
  assert.match(source, /flags\["no-clean"\] && "--no-clean"/);
});
