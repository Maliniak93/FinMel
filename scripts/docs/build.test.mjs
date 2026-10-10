import assert from "node:assert/strict";
import { existsSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { test } from "node:test";
import { makeSandbox, prosePath, readFacts, runBuild, runFacts, snapshot, writeProse } from "./helpers.mjs";

const LIVE = ["index.html", "architektura.html", "mermaid.min.js", "manifest.json"];

function readyToBuild() {
  const sb = makeSandbox();
  const facts = runFacts(sb);
  assert.equal(facts.status, 0, facts.output);
  writeProse(sb);
  return sb;
}

test("build writes the pages, mermaid and manifest, and only inside --out", () => {
  const sb = readyToBuild();
  try {
    const rootBefore = snapshot(sb.root, { skip: [".git"] });
    const siblingsBefore = snapshot(sb.dir, { skip: ["out", "repo"] });

    const run = runBuild(sb);
    assert.equal(run.status, 0, run.output);

    for (const file of LIVE) assert.ok(existsSync(path.join(sb.out, file)), `${file} missing`);
    const manifest = JSON.parse(readFileSync(path.join(sb.out, "manifest.json"), "utf8"));
    assert.deepEqual(Object.keys(manifest.pages).sort(), ["architektura", "index"]);
    assert.equal(manifest.commit, sb.commit);

    for (const page of ["index.html", "architektura.html"]) {
      const html = readFileSync(path.join(sb.out, page), "utf8");
      assert.match(html, /href="index\.html"/, `${page} nav to index`);
      assert.match(html, /href="architektura\.html"/, `${page} nav to architektura`);
      assert.ok(html.includes(sb.commit), `${page} footer commit hash`);
      assert.match(html, /<footer[\s\S]*\d{4}-\d{2}-\d{2}[\s\S]*<\/footer>/, `${page} footer timestamp`);
      assert.ok(html.includes('<script src="mermaid.min.js">'), `${page} mermaid script`);
      assert.doesNotMatch(html, /(src|href)\s*=\s*["']https?:\/\//i, `${page} external reference`);
      assert.doesNotMatch(html, /url\(\s*["']?https?:/i, `${page} external css reference`);
    }

    assert.deepEqual(snapshot(sb.root, { skip: [".git"] }), rootBefore);
    assert.deepEqual(snapshot(sb.dir, { skip: ["out", "repo"] }), siblingsBefore);
  } finally {
    sb.cleanup();
  }
});

function assertBuildFailsAndKeepsLive(mutate) {
  const sb = readyToBuild();
  try {
    const first = runBuild(sb);
    assert.equal(first.status, 0, first.output);
    const before = snapshot(sb.out, { skip: [".work"] });

    mutate(sb);
    const run = runBuild(sb);

    assert.notEqual(run.status, 0);
    assert.match(run.output, /architektura/);
    assert.deepEqual(snapshot(sb.out, { skip: [".work"] }), before);
  } finally {
    sb.cleanup();
  }
}

test("build with a missing prose fragment fails naming the page and leaves the previous guide untouched", () => {
  assertBuildFailsAndKeepsLive((sb) => rmSync(prosePath(sb, "architektura")));
});

test("build with a stale-stamped prose fragment fails naming the page and leaves the previous guide untouched", () => {
  assertBuildFailsAndKeepsLive((sb) => {
    writeFileSync(prosePath(sb, "architektura"), "<!-- facts:deadbeef -->\n<p>Stare.</p>\n");
    assert.ok(readFacts(sb).pages.architektura.hash !== "deadbeef");
  });
});
