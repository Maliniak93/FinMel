import assert from "node:assert/strict";
import { test } from "node:test";
import { makeSandbox, put, runFacts, snapshot } from "./helpers.mjs";

const CASES = [
  {
    name: "workflow without a parsable meta",
    file: "flow-one.js",
    path: ".claude/workflows/flow-one.js",
    content: "const nothing = 1\nawait step('worker', 'x', [])\n",
    what: /meta/i,
  },
  {
    name: "skill with broken frontmatter",
    file: "alpha",
    path: ".claude/skills/alpha/SKILL.md",
    content: "---\nname: alpha\ndescription: [unclosed\nmodel: sonnet\n\n# Alpha\n",
    what: /frontmatter/i,
  },
  {
    name: "script without a usage line",
    file: "gamma.mjs",
    path: "scripts/gamma.mjs",
    content: "console.log('no header');\n",
    what: /usage/i,
  },
];

for (const c of CASES) {
  test(`facts fails loudly on a ${c.name}, names the file and writes nothing`, () => {
    const sb = makeSandbox();
    try {
      put(sb, c.path, c.content);
      const run = runFacts(sb);
      assert.notEqual(run.status, 0, run.output);
      assert.ok(run.output.includes(c.file), `output names ${c.file}: ${run.output}`);
      assert.match(run.output, c.what);
      assert.deepEqual(snapshot(sb.out), {});
    } finally {
      sb.cleanup();
    }
  });
}
