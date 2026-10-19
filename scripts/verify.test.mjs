import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { cpSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath, pathToFileURL } from "node:url";
import { computeAffected, computeScope, findCacheHit, stepSet } from "./verify.mjs";

const SCRIPTS_DIR = path.dirname(fileURLToPath(import.meta.url));

const SERVICES = ["Identity", "MarketData", "Portfolio", "Reporting"];
const REL = {
  identity: "services/Identity/Skarbiec.Identity.Tests/Skarbiec.Identity.Tests.csproj",
  marketdata: "services/MarketData/Skarbiec.MarketData.Tests/Skarbiec.MarketData.Tests.csproj",
  portfolio: "services/Portfolio/Skarbiec.Portfolio.Tests/Skarbiec.Portfolio.Tests.csproj",
  reporting: "services/Reporting/Skarbiec.Reporting.Tests/Skarbiec.Reporting.Tests.csproj",
  gateway: "gateway/Skarbiec.Gateway.Tests/Skarbiec.Gateway.Tests.csproj",
  contracts: "contracts/Skarbiec.Contracts.Tests/Skarbiec.Contracts.Tests.csproj",
  servicedefaults: "Skarbiec.ServiceDefaults.Tests/Skarbiec.ServiceDefaults.Tests.csproj",
  testing: "Skarbiec.Testing.Tests/Skarbiec.Testing.Tests.csproj",
};
const PROJECT_MAP = new Map(Object.entries(REL).map(([k, rel]) => [k, { shortName: k, rel }]));

const baseArgs = (over = {}) => ({ quick: false, all: false, web: false, api: false, projects: null, ...over });
const scopeOfFiles = (files, over = {}) =>
  computeScope(baseArgs(over), SERVICES, PROJECT_MAP, { files, treatAsAll: false, base: "master...HEAD" });
const stepsOfFiles = (files) => [...stepSet(scopeOfFiles(files))].sort();
const test_ = (key) => `test:${REL[key]}`;

const PORTFOLIO_PLAIN = "services/Portfolio/Skarbiec.Portfolio/Domain/Money.cs";
const PORTFOLIO_SLICE = "services/Portfolio/Skarbiec.Portfolio/Features/AddAsset/AddAssetHandler.cs";

test("scripts and docs only -> scripts-test", () => {
  for (const files of [
    ["scripts/gh-project.mjs"],
    [".claude/skills/build/SKILL.md"],
    ["skarbiec-plan/workflow.md"],
    ["requests/portfolio.http"],
    [],
  ]) {
    assert.deepEqual(stepsOfFiles(files), ["scripts-test"], files.join(","));
  }
});

test("web only", () => {
  assert.deepEqual(stepsOfFiles(["web/src/app/app.ts"]), ["scripts-test", "web"]);
});

test("service slice", () => {
  assert.deepEqual(stepsOfFiles([PORTFOLIO_PLAIN]), ["build", "format", "scripts-test", test_("portfolio")].sort());
  assert.deepEqual(
    stepsOfFiles([PORTFOLIO_SLICE]),
    ["api", "build", "format", "scripts-test", test_("portfolio")].sort(),
  );
});

test("root test projects run build, format and their own tests", () => {
  assert.deepEqual(
    stepsOfFiles(["Skarbiec.ServiceDefaults.Tests/AuthenticationTests.cs"]),
    ["build", "format", "scripts-test", test_("servicedefaults")].sort(),
  );
  assert.deepEqual(
    stepsOfFiles(["Skarbiec.Testing.Tests/X.cs"]),
    ["build", "format", "scripts-test", test_("testing")].sort(),
  );
});

test("e2e and infra paths", () => {
  assert.deepEqual(stepsOfFiles(["e2e/tests/a.spec.ts"]), ["e2e", "scripts-test"]);
  for (const f of ["deploy/Dockerfile.portfolio", "compose.yaml", ".github/workflows/other.yml"]) {
    assert.deepEqual(stepsOfFiles([f]), ["e2e", "scripts-test"], f);
  }
  assert.deepEqual(stepsOfFiles(["Skarbiec.AppHost/Program.cs"]), ["build", "format", "scripts-test"]);
  assert.deepEqual(
    stepsOfFiles(["gateway/Skarbiec.Gateway/Program.cs"]),
    ["build", "format", "scripts-test", test_("gateway")].sort(),
  );
});

test("full-scope anchors win", () => {
  const all = computeScope(baseArgs({ all: true }), SERVICES, PROJECT_MAP);
  const allSteps = [...stepSet(all)].sort();
  for (const files of [
    ["contracts/CONTRACTS.md"],
    ["Skarbiec.ServiceDefaults/Messaging/README.md"],
    ["Skarbiec.Testing/README.md"],
    ["Directory.Build.props"],
    ["Skarbiec.slnx", "skarbiec-plan/workflow.md"],
    [".editorconfig"],
    [".github/workflows/ci.yml", "README.md"],
  ]) {
    const scope = scopeOfFiles(files);
    assert.deepEqual([...stepSet(scope)].sort(), allSteps, files.join(","));
    assert.deepEqual(scope.selectedRels, all.selectedRels, files.join(","));
    assert.equal(scope.runWeb, true, files.join(","));
    assert.equal(scope.runE2e, true, files.join(","));
  }
});

test("union of rules", () => {
  assert.deepEqual(
    stepsOfFiles(["web/README.md", PORTFOLIO_PLAIN]),
    ["build", "format", "scripts-test", test_("portfolio"), "web"].sort(),
  );
});

test("computeAffected keeps its shape", () => {
  const affected = computeAffected([PORTFOLIO_SLICE, "web/src/app/app.ts", "e2e/tests/a.spec.ts"], SERVICES);
  assert.deepEqual([...affected.affectedKeys], ["portfolio"]);
  assert.equal(affected.web, true);
  assert.equal(affected.api, true);
  assert.equal(affected.e2e, true);
});

test("explicit flags unchanged", () => {
  const e2eOnly = computeScope(baseArgs({ projects: ["e2e"] }), SERVICES, PROJECT_MAP);
  assert.deepEqual([...stepSet(e2eOnly)].sort(), ["e2e"]);

  const pair = computeScope(baseArgs({ projects: ["Portfolio", "Reporting"] }), SERVICES, PROJECT_MAP);
  assert.deepEqual(pair.selectedRels, [REL.portfolio, REL.reporting].sort());
  assert.equal(pair.runWeb, false);
  assert.equal(pair.runApi, false);
  assert.equal(pair.runE2e, false);
  const pairSteps = [...stepSet(pair)];
  assert.ok(!pairSteps.includes("scripts-test"));
  assert.ok(pairSteps.includes("build"));

  const all = computeScope(baseArgs({ all: true }), SERVICES, PROJECT_MAP);
  assert.equal(all.selectedRels.length, PROJECT_MAP.size);
  const allSteps = [...stepSet(all)];
  for (const s of ["build", "format", "scripts-test", "web", "api", "e2e"]) assert.ok(allSteps.includes(s), s);
});

const green = (over = {}) => ({
  tree: "T1",
  quick: false,
  steps: ["build", "format", "scripts-test", "web", "test:a", "test:b"],
  result: { ok: true, failures: [] },
  ...over,
});
const need = (over = {}) => ({ tree: "T1", quick: false, steps: ["scripts-test", "test:a"], ...over });

test("cache superset", () => {
  const exact = green({ steps: ["scripts-test", "test:a"] });
  assert.equal(findCacheHit([exact], need()), exact);
  const wider = green();
  assert.equal(findCacheHit([green({ tree: "other" }), wider], need()), wider);
});

test("cache misses", () => {
  assert.equal(findCacheHit([green({ steps: ["scripts-test"] })], need()), null);
  assert.equal(findCacheHit([green({ tree: "T2" })], need()), null);
  assert.equal(findCacheHit([green({ quick: true })], need()), null);
  assert.equal(findCacheHit([{ fingerprint: "abc", scope: "{}", result: { ok: true, failures: [] } }], need()), null);
  assert.equal(findCacheHit([], need()), null);
});

test("cache-check end to end", async () => {
  const dir = mkdtempSync(path.join(os.tmpdir(), "verify-cache-"));
  try {
    const git = (...a) => {
      const r = spawnSync("git", a, { cwd: dir, encoding: "utf8", windowsHide: true });
      assert.equal(r.status, 0, r.stderr);
      return r.stdout;
    };
    mkdirSync(path.join(dir, "scripts"));
    for (const f of ["verify.mjs", "stop-stack.mjs"]) cpSync(path.join(SCRIPTS_DIR, f), path.join(dir, "scripts", f));
    writeFileSync(path.join(dir, "README.md"), "x\n");
    git("init", "-q", "-b", "master");
    git("config", "user.email", "t@t");
    git("config", "user.name", "t");
    git("add", "-A");
    git("commit", "-q", "-m", "init");
    git("checkout", "-q", "-b", "work");
    writeFileSync(path.join(dir, "scripts", "x.mjs"), "export {};\n");

    const copy = await import(pathToFileURL(path.join(dir, "scripts", "verify.mjs")).href);
    const tree = copy.computeTreeHash();
    assert.ok(tree);
    const allSteps = [...copy.stepSet(copy.computeScope(baseArgs({ all: true }), [], new Map()))];
    copy.storeGreen({ tree, quick: false, steps: allSteps }, { ok: true, failures: [] });

    const check = () =>
      spawnSync(process.execPath, [path.join(dir, "scripts", "verify.mjs"), "--cache-check"], {
        cwd: dir,
        encoding: "utf8",
        windowsHide: true,
      });
    const hit = check();
    assert.equal(hit.status, 0, hit.stdout + hit.stderr);
    assert.match(hit.stdout, /"cached":true/);

    writeFileSync(path.join(dir, "notes.md"), "outside the scope\n");
    const miss = check();
    assert.equal(miss.status, 2, miss.stdout + miss.stderr);
    assert.match(miss.stdout, /"step":"cache"/);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
