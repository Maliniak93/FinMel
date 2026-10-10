import assert from "node:assert/strict";
import { existsSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { test } from "node:test";
import { makeSandbox, prosePath, readFacts, runBuild, runFacts, sequenceProse, snapshot, writeProse } from "./helpers.mjs";

const SLUGS = ["architektura", "bazy-danych", "eventy", "index", "joby-integracje-frontend", "slabe-punkty"];
const LIVE = [...SLUGS.map((slug) => `${slug}.html`), "mermaid.min.js", "manifest.json"];

function readyToBuild() {
  const sb = makeSandbox();
  const facts = runFacts(sb);
  assert.equal(facts.status, 0, facts.output);
  writeProse(sb);
  return sb;
}

test("build writes the six pages, mermaid and manifest, each nav listing all six, and only inside --out", () => {
  const sb = readyToBuild();
  try {
    const rootBefore = snapshot(sb.root, { skip: [".git"] });
    const siblingsBefore = snapshot(sb.dir, { skip: ["out", "repo"] });

    const run = runBuild(sb);
    assert.equal(run.status, 0, run.output);

    for (const file of LIVE) assert.ok(existsSync(path.join(sb.out, file)), `${file} missing`);
    const manifest = JSON.parse(readFileSync(path.join(sb.out, "manifest.json"), "utf8"));
    assert.deepEqual(Object.keys(manifest.pages).sort(), SLUGS);
    assert.equal(manifest.commit, sb.commit);

    for (const slug of SLUGS) {
      const page = `${slug}.html`;
      const html = readFileSync(path.join(sb.out, page), "utf8");
      for (const target of SLUGS) assert.match(html, new RegExp(`href="${target}\\.html"`), `${page} nav to ${target}`);
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

test("build writes one flowchart on Eventy and one erDiagram per service on Bazy danych, MassTransit tables collapsed", () => {
  const sb = readyToBuild();
  try {
    const run = runBuild(sb);
    assert.equal(run.status, 0, run.output);

    const eventy = readFileSync(path.join(sb.out, "eventy.html"), "utf8");
    assert.equal(eventy.match(/flowchart/g)?.length, 1, "one flowchart on Eventy");

    const bazy = readFileSync(path.join(sb.out, "bazy-danych.html"), "utf8");
    assert.equal(bazy.match(/erDiagram/g)?.length, 4, "one erDiagram per service");
    assert.ok(bazy.includes("outbox/inbox"), "collapsed MassTransit line");
    assert.ok(!bazy.includes("PortfolioOutboxMessage"), "MassTransit tables not listed one by one");
  } finally {
    sb.cleanup();
  }
});

function assertBuildFailsAndKeepsLive(mutate, page = "architektura") {
  const sb = readyToBuild();
  try {
    const first = runBuild(sb);
    assert.equal(first.status, 0, first.output);
    const before = snapshot(sb.out, { skip: [".work"] });

    mutate(sb);
    const run = runBuild(sb);

    assert.notEqual(run.status, 0);
    assert.match(run.output, new RegExp(page));
    assert.deepEqual(snapshot(sb.out, { skip: [".work"] }), before);
  } finally {
    sb.cleanup();
  }
}

test("build fails naming Słabe punkty when its prose lacks the ocena-claude section and leaves the guide untouched", () => {
  assertBuildFailsAndKeepsLive((sb) => {
    const hash = readFacts(sb).pages["slabe-punkty"].hash;
    writeFileSync(prosePath(sb, "slabe-punkty"), `<!-- facts:${hash} -->\n<p>Bez sekcji oceny.</p>\n`);
  }, "slabe-punkty");
});

test("build with a missing prose fragment fails naming the page and leaves the previous guide untouched", () => {
  assertBuildFailsAndKeepsLive((sb) => rmSync(prosePath(sb, "architektura")));
});

test("build with a stale-stamped prose fragment fails naming the page and leaves the previous guide untouched", () => {
  assertBuildFailsAndKeepsLive((sb) => {
    writeFileSync(prosePath(sb, "architektura"), "<!-- facts:deadbeef -->\n<p>Stare.</p>\n");
    assert.ok(readFacts(sb).pages.architektura.hash !== "deadbeef");
  });
});

test("build fails naming Eventy when its prose holds fewer than four sequenceDiagram blocks and leaves the guide untouched", () => {
  assertBuildFailsAndKeepsLive((sb) => {
    const hash = readFacts(sb).pages.eventy.hash;
    writeFileSync(prosePath(sb, "eventy"), `<!-- facts:${hash} -->\n${sequenceProse(3)}\n`);
  }, "eventy");
});
