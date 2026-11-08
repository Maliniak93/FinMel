import assert from "node:assert/strict";
import { createRequire } from "node:module";
import path from "node:path";
import test from "node:test";
import { fileURLToPath, pathToFileURL } from "node:url";

const webDir = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "web");
const requireFromWeb = createRequire(path.join(webDir, "package.json"));

async function lint(code) {
  const stylelint = requireFromWeb("stylelint");
  const { default: config } = await import(pathToFileURL(path.join(webDir, "stylelint.config.mjs")).href);
  const result = await stylelint.lint({ code, codeFilename: path.join(webDir, "src", "sample.scss"), config });
  return result.results[0].warnings;
}

test("rejects literal font-size", async () => {
  assert.equal((await lint("a { font-size: 15px; }")).length, 1);
  assert.equal((await lint("a { font-size: 1.1rem; }")).length, 1);
  assert.equal((await lint("a { font-size: var(--sk-text-base); }")).length, 0);
});

test("rejects off-grid spacing and allows exceptions", async () => {
  assert.equal((await lint("a { padding: 10px; }")).length, 1);
  assert.equal((await lint("a { gap: 0.5rem; }")).length, 1);
  assert.equal((await lint("a { padding: 1px; }")).length, 0);
  assert.equal((await lint("a { margin: -8px; }")).length, 0);
  assert.equal((await lint("a { padding: var(--sk-space-2); }")).length, 0);
});

test("rejects width literals in media and container queries", async () => {
  assert.equal((await lint("@media (min-width: 720px) { a { color: red; } }")).length, 1);
  assert.equal((await lint("@container (width < 700px) { a { color: red; } }")).length, 1);
  assert.equal((await lint("a { @include sk-from(600) { color: red; } }")).length, 0);
});
