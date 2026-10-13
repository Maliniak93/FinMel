import assert from "node:assert/strict";
import { readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { test } from "node:test";
import { descPath, descriptionFor, makeSandbox, readFacts, readyToBuild, runBuild, snapshot } from "./helpers.mjs";

const badges = (html) => html.match(/nieaktualny/g)?.length ?? 0;

test("build writes one index.html and manifest.json with map, cards, steps, badge only on the undescribed node and the commit footer, only inside --out", () => {
  const sb = makeSandbox();
  try {
    readyToBuild(sb);
    const rootBefore = snapshot(sb.root, { skip: [".git"] });
    const siblingsBefore = snapshot(sb.dir, { skip: ["out", "repo"] });

    const full = runBuild(sb);
    assert.equal(full.status, 0, full.output);
    const baseline = badges(readFileSync(path.join(sb.out, "index.html"), "utf8"));

    rmSync(descPath(sb, "script:gamma"));
    const run = runBuild(sb);
    assert.equal(run.status, 0, run.output);

    assert.deepEqual(readdirSync(sb.out).filter((f) => f !== ".work").sort(), ["index.html", "manifest.json"]);
    const html = readFileSync(path.join(sb.out, "index.html"), "utf8");
    const manifest = JSON.parse(readFileSync(path.join(sb.out, "manifest.json"), "utf8"));

    assert.doesNotMatch(html, /<script\s+src/i);
    assert.doesNotMatch(html, /<link\s+[^>]*href/i);
    assert.match(html, /flowchart/);
    for (const name of ["alpha", "worker", "flow-one", "beta", "gamma"]) assert.ok(html.includes(name), `page mentions ${name}`);
    for (const text of ["--quick", "skip the slow part", "DEFAULT_TIMEOUT_MS", "zmienia git", "Prepare", "Unikat-script:beta-1", "Unikat-script:beta-2", "gdy trzeba"]) {
      assert.ok(html.includes(text), `page contains "${text}"`);
    }
    assert.equal(badges(html), baseline + 1, "exactly one more nieaktualny badge for the undescribed node");
    assert.ok(html.includes(sb.commit), "footer commit");
    assert.equal(manifest.commit, sb.commit);

    assert.deepEqual(snapshot(sb.root, { skip: [".git"] }), rootBefore);
    assert.deepEqual(snapshot(sb.dir, { skip: ["out", "repo"] }), siblingsBefore);
  } finally {
    sb.cleanup();
  }
});

test("build with an invalid description fails naming the node and the field and leaves index.html and manifest.json byte-identical", () => {
  const sb = makeSandbox();
  try {
    readyToBuild(sb);
    assert.equal(runBuild(sb).status, 0);
    const before = snapshot(sb.out, { skip: [".work"] });
    const facts = readFacts(sb);

    const cases = [
      { id: "script:beta", content: "{ not json", field: /json/i },
      { id: "cmd:alpha", content: JSON.stringify({ schema: 1, sources: facts.nodes["cmd:alpha"].hash, summary: "x" }), field: /steps/ },
      {
        id: "wf:flow-one",
        content: JSON.stringify(
          descriptionFor("wf:flow-one", facts.nodes["wf:flow-one"].hash, {
            steps: [{ id: "s1", title: "t", detail: "d", agent: "ghost" }],
          }),
        ),
        field: /agent/,
      },
    ];
    for (const c of cases) {
      const good = readFileSync(descPath(sb, c.id), "utf8");
      writeFileSync(descPath(sb, c.id), c.content);
      const run = runBuild(sb);
      assert.notEqual(run.status, 0, `${c.id}: ${run.output}`);
      assert.ok(run.output.includes(c.id.split(":")[1]), `${c.id} named: ${run.output}`);
      assert.match(run.output, c.field);
      assert.deepEqual(snapshot(sb.out, { skip: [".work"] }), before, `${c.id} output untouched`);
      writeFileSync(descPath(sb, c.id), good);
    }
  } finally {
    sb.cleanup();
  }
});
