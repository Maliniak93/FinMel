import assert from "node:assert/strict";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { test } from "node:test";
import { makeSandbox, runBuild, runFacts, writeProse } from "./helpers.mjs";

test("facts reports no stale page on unchanged code, only architektura after a slice change, every page with --all", () => {
  const sb = makeSandbox();
  try {
    const first = runFacts(sb);
    assert.equal(first.status, 0, first.output);
    writeProse(sb);
    const build = runBuild(sb);
    assert.equal(build.status, 0, build.output);

    const unchanged = runFacts(sb);
    assert.equal(unchanged.status, 0, unchanged.output);
    assert.deepEqual(unchanged.result.stale, []);

    const slice = path.join(sb.root, "services", "Reporting", "Skarbiec.Reporting", "Features", "GetOther");
    mkdirSync(slice, { recursive: true });
    writeFileSync(
      path.join(slice, "GetOtherEndpoint.cs"),
      'public static class GetOtherEndpoint\n{\n    public static void Map(IEndpointRouteBuilder app)\n    {\n        app.MapGroup("/api/reporting/other").MapGet("/x", Handle);\n    }\n}\n',
    );

    const changed = runFacts(sb);
    assert.equal(changed.status, 0, changed.output);
    assert.deepEqual(changed.result.stale, ["architektura"]);

    const all = runFacts(sb, ["--all"]);
    assert.equal(all.status, 0, all.output);
    assert.deepEqual([...all.result.stale].sort(), ["architektura", "bazy-danych", "eventy", "index"]);
  } finally {
    sb.cleanup();
  }
});
