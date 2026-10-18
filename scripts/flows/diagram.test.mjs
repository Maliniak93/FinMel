import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { test } from "node:test";
import { GRAPH, autobuildSource, makeSandbox, pipelineReady, put, readFacts, runBuild, snapshot, step, writeDescription } from "./helpers.mjs";

const html = (sb) => readFileSync(path.join(sb.out, "index.html"), "utf8");
const decode = (text) => text.replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&quot;/g, '"').replace(/&amp;/g, "&");

function card(sb, slug) {
  const page = html(sb);
  const start = page.indexOf(`id="card-${slug}"`);
  assert.ok(start >= 0, `card ${slug} present`);
  return decode(page.slice(start, page.indexOf("</section>", start)));
}

function panelOf(sb, slug) {
  const page = decode(html(sb));
  const start = page.indexOf(`id="step-${slug}"`);
  assert.ok(start >= 0, `step panel ${slug} present`);
  return page.slice(start, page.indexOf("</div>", start));
}

function diagramOf(cardHtml) {
  const m = cardHtml.match(/<pre class="mermaid">([\s\S]*?)<\/pre>/);
  assert.ok(m, "card holds a mermaid block");
  return m[1];
}

test("build rejects a pipeline description with a step lacking next, a missing target or an unreachable step, naming node and step, and renders a non-pipeline node without next as a list", () => {
  const sb = makeBase();
  try {
    assert.equal(runBuild(sb).status, 0);
    assert.ok(html(sb).includes("Unikat-cmd:alpha-1"), "non-pipeline node keeps its step list");
    const before = snapshot(sb.out, { skip: [".work"] });

    const cases = [
      { what: "no next", steps: [step("p1", { next: [{ to: "p2" }] }), step("p2"), step("p3", { next: [] })], named: "p2" },
      { what: "missing target", steps: [step("p1", { next: [{ to: "ghost" }] }), step("p2", { next: [] })], named: "p1" },
      { what: "unreachable", steps: [step("p1", { next: [] }), step("p2", { next: [] })], named: "p2" },
    ];
    for (const c of cases) {
      writeDescription(sb, "script:autobuild", c.steps);
      const run = runBuild(sb);
      assert.notEqual(run.status, 0, `${c.what}: ${run.output}`);
      assert.ok(run.output.includes("autobuild"),`${c.what} names the node: ${run.output}`);
      assert.ok(run.output.includes(c.named), `${c.what} names step ${c.named}: ${run.output}`);
      assert.deepEqual(snapshot(sb.out, { skip: [".work"] }), before, `${c.what}: page untouched`);
    }
  } finally {
    sb.cleanup();
  }
});

test("build renders a pipeline node as a mermaid flowchart with a box per step, a labelled edge per next and a click binding per step", () => {
  const sb = makeBase();
  try {
    const run = runBuild(sb);
    assert.equal(run.status, 0, run.output);
    const source = diagramOf(card(sb, "cmd-build"));
    assert.match(source, /flowchart/);
    for (const s of GRAPH) assert.ok(source.includes(`Unikat-${s.id}`), `box for ${s.id}`);
    const edges = GRAPH.flatMap((s) => s.next);
    assert.equal(source.match(/-->/g)?.length, edges.length, "one edge per next");
    for (const when of edges.map((e) => e.when).filter(Boolean)) assert.ok(source.includes(when), `edge label ${when}`);
    assert.equal(source.match(/^\s*click\s/gm)?.length, GRAPH.length, "one click binding per step");
  } finally {
    sb.cleanup();
  }
});

test("build gives a step with a tiered agent the start and escalated level rows and any other agent its frontmatter model and effort, both with a link to the agent card", () => {
  const sb = makeBase();
  try {
    const run = runBuild(sb);
    assert.equal(run.status, 0, run.output);
    const levels = readFacts(sb).nodes["wf:build-feature"].facts.levels;
    const rowsOf = (name) => levels.find((l) => l.name === name).rows;
    const text = (row) => row.split(" → ").slice(1).join(" → ");
    const tiered = panelOf(sb, "wf-build-feature-a");
    const plain = panelOf(sb, "wf-build-feature-b");

    const startAt = tiered.indexOf("poziom startowy");
    const escalationAt = tiered.indexOf("eskalacja");
    assert.ok(startAt >= 0 && escalationAt > startAt, "start level then escalation headings in the step panel");
    const sections = { startLevel: tiered.slice(startAt, escalationAt), escalatedLevel: tiered.slice(escalationAt) };
    for (const name of ["startLevel", "escalatedLevel"]) {
      assert.ok(rowsOf(name).length > 0, `${name} has rows`);
      const section = sections[name];
      for (const row of rowsOf(name)) assert.ok(section.includes(text(row)), `${name} row "${row}" shown in the step panel`);
    }
    assert.ok(tiered.includes('href="#card-agent-implementer"'), "step panel links to implementer card");
    assert.ok(plain.includes("opus/xhigh"), "scout frontmatter model and effort in the step panel");
    assert.ok(plain.includes('href="#card-agent-scout"'), "step panel links to scout card");
  } finally {
    sb.cleanup();
  }
});

test("build resolves limit.ref to the current constant and calls to a node link, follows a changed constant without a description change and rejects a ref or call naming nothing", () => {
  const callsBuild = GRAPH.map((s) => (s.id === "krok-b" ? { ...s, calls: "script:autobuild" } : s));
  const autobuildSteps = [
    step("e1", { limit: { ref: "script:autobuild#CI_TIMEOUT_MS", text: "Unikat-tekst-limitu" }, next: [{ to: "e2" }] }),
    step("e2", { next: [] }),
  ];
  const sb = makeBase({ build: callsBuild, autobuild: autobuildSteps });
  try {
    const first = runBuild(sb);
    assert.equal(first.status, 0, first.output);
    const limitPanel = panelOf(sb, "script-autobuild-e1");
    assert.ok(limitPanel.includes("CI_TIMEOUT_MS") && limitPanel.includes("5400000") && limitPanel.includes("Unikat-tekst-limitu"));
    assert.ok(limitPanel.includes('href="#card-script-autobuild"'), "limit links to the constant's node");
    assert.ok(card(sb, "cmd-build").includes('href="#card-script-autobuild"'), "link to the called node");

    put(sb, "scripts/autobuild.mjs", autobuildSource(45));
    const second = runBuild(sb);
    assert.equal(second.status, 0, second.output);
    const changed = panelOf(sb, "script-autobuild-e1");
    assert.ok(changed.includes("2700000"), "new value shown in the step panel");
    assert.ok(!changed.includes("5400000"), "old value gone from the step panel");

    const before = snapshot(sb.out, { skip: [".work"] });
    writeDescription(sb, "script:autobuild", [step("e1", { limit: { ref: "script:autobuild#NOPE" }, next: [] })]);
    const badRef = runBuild(sb);
    assert.notEqual(badRef.status, 0, badRef.output);
    assert.ok(badRef.output.includes("autobuild") && badRef.output.includes("e1"), badRef.output);

    writeDescription(sb, "script:autobuild", autobuildSteps);
    writeDescription(sb, "cmd:build", GRAPH.map((s) => (s.id === "krok-b" ? { ...s, calls: "script:ghost" } : s)));
    const badCall = runBuild(sb);
    assert.notEqual(badCall.status, 0, badCall.output);
    assert.ok(badCall.output.includes("build") && badCall.output.includes("krok-b"), badCall.output);
    assert.deepEqual(snapshot(sb.out, { skip: [".work"] }), before, "page untouched");
  } finally {
    sb.cleanup();
  }
});

function makeBase(opts) {
  const sb = makeSandbox();
  pipelineReady(sb, opts);
  return sb;
}
