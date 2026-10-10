import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { after, before, test } from "node:test";
import { makeSandbox, mentions, pageOf, readFacts, runBuild, runFacts, section, strings, writeProse } from "./helpers.mjs";

let sb;
let items;

before(() => {
  sb = makeSandbox();
  const run = runFacts(sb);
  assert.equal(run.status, 0, run.output);
  writeProse(sb);
  const build = runBuild(sb);
  assert.equal(build.status, 0, build.output);
  items = section(pageOf(readFacts(sb), "slabe-punkty"), "items");
});

after(() => sb.cleanup());

const EXPECTED = [
  "ADR-002",
  "ADR-003",
  "ADR-004",
  "/internal/prices/ghost-batch",
  "UserRegistered",
  "RefreshToken",
  "quotes.fixture.test",
];

test("Słabe punkty lists the 🕐, exception, fail-soft, /internal, event, external URL and tenancy items once each, each linking to an anchor that exists on its page", () => {
  for (const needle of EXPECTED) {
    const matches = items.filter((i) => mentions(i, needle));
    assert.equal(matches.length, 1, `${needle} listed ${matches.length} times`);

    const links = strings(matches[0])
      .map((s) => /^([\w-]+\.html)#(.+)$/.exec(s))
      .filter(Boolean);
    assert.ok(links.length > 0, `${needle} has no anchor link`);
    for (const [, page, anchor] of links) {
      const html = readFileSync(path.join(sb.out, page), "utf8");
      assert.ok(html.includes(`id="${anchor}"`), `${needle}: ${page} has no #${anchor}`);
    }
  }
});
