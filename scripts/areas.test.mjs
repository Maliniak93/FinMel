import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import * as areasModule from "./areas.mjs";
import { AREAS, areaOfFiles, areaOfPath, parseArea } from "./areas.mjs";

const SCRIPT = path.join(path.dirname(fileURLToPath(import.meta.url)), "areas.mjs");

test("areaOfPath maps every prefix", () => {
  assert.deepEqual(AREAS, ["Backend", "Frontend", "E2E", "Claude", "Scripts", "CI/Infra", "Docs", "Fullstack"]);
  const expected = {
    "services/Portfolio/x.cs": "Backend",
    "gateway/Program.cs": "Backend",
    "contracts/Skarbiec.Contracts/a.cs": "Backend",
    "Skarbiec.ServiceDefaults/a.cs": "Backend",
    "Skarbiec.Testing/a.cs": "Backend",
    "requests/portfolio.http": "Backend",
    "web/src/app/app.ts": "Frontend",
    "e2e/tests/a.spec.ts": "E2E",
    ".claude/skills/x/SKILL.md": "Claude",
    "CLAUDE.md": "Claude",
    ".mcp.json": "Claude",
    "scripts/gh.mjs": "Scripts",
    ".github/workflows/ci.yml": "CI/Infra",
    "deploy/README.md": "CI/Infra",
    "Skarbiec.AppHost/Program.cs": "CI/Infra",
    "Skarbiec.slnx": "CI/Infra",
    "Directory.Build.props": "CI/Infra",
    ".editorconfig": "CI/Infra",
    "skarbiec-plan/domain.md": "Docs",
    "README.md": "Docs",
    "web/README.md": "Docs",
  };
  for (const [file, area] of Object.entries(expected)) assert.equal(areaOfPath(file), area, file);
  assert.equal(parseArea("fullstack"), "Fullstack");
  assert.equal(parseArea("CI/INFRA"), "CI/Infra");
  assert.equal(parseArea("nope"), null);
});

test("areaOfFiles applies companions, Fullstack and ties", () => {
  assert.equal(areaOfFiles(["web/src/app/app.ts", "services/Portfolio/x.cs"]), "Fullstack");
  assert.equal(areaOfFiles(["web/src/app/api/client.ts", "services/Portfolio/x.cs"]), "Backend");
  assert.equal(areaOfFiles(["scripts/x.mjs", ".claude/skills/x/SKILL.md"]), "Scripts");
  assert.equal(areaOfFiles([".claude/rules/x.md", "skarbiec-plan/decisions.md"]), "Claude");
  assert.equal(areaOfFiles(["skarbiec-plan/a.md", "skarbiec-plan/b.md", ".claude/rules/x.md"]), "Docs");
  assert.equal(areaOfFiles(["requests/x.http"]), "Backend");
  assert.equal(areaOfFiles([]), null);
});

test("outside", () => {
  const run = (args) => spawnSync(process.execPath, [SCRIPT, ...args], { encoding: "utf8", windowsHide: true });
  const outside = (args) => {
    const res = run(["outside", ...args]);
    assert.equal(res.status, 0, res.stderr);
    return res.stdout.trim() === "" ? [] : res.stdout.trim().split(/\r?\n/);
  };

  assert.deepEqual(outside(["Backend", "services/Portfolio/X.cs", "web/src/app/a.ts", "skarbiec-plan/workflow.md"]), [
    "web/src/app/a.ts",
    "skarbiec-plan/workflow.md",
  ]);
  assert.deepEqual(outside(["Fullstack", "services/Portfolio/X.cs", "web/src/app/a.ts"]), []);
  assert.deepEqual(outside(["Fullstack", "services/Portfolio/X.cs", "web/src/app/a.ts", "skarbiec-plan/workflow.md"]), ["skarbiec-plan/workflow.md"]);
  assert.deepEqual(outside(["Backend", "web/src/app/api/client.ts", "requests/x.http", "Skarbiec.AppHost/Program.cs"]), []);
  assert.deepEqual(outside(["Frontend", "requests/x.http", "Skarbiec.AppHost/Program.cs", "web/src/app/api/client.ts"]), []);
});

const playbook = (name) => `.claude/skills/${name}/SKILL.md`;
const sorted = (list) => [...list].sort();

test("profileOf table", () => {
  const { profileOf } = areasModule;
  const table = {
    Backend: { tests: true, implementer: ["backend-playbook"], testWriter: ["testing-playbook"], checklist: ["Common", "Backend"], phases: "test-writer → implementer → reviewer" },
    Frontend: { tests: true, implementer: ["frontend-playbook"], testWriter: ["frontend-playbook"], checklist: ["Common", "Frontend"], phases: "test-writer → implementer → reviewer" },
    Fullstack: {
      tests: true,
      implementer: ["backend-playbook", "frontend-playbook"],
      testWriter: ["testing-playbook", "frontend-playbook"],
      checklist: ["Common", "Backend", "Frontend"],
      phases: "test-writer → implementer → reviewer",
    },
    Scripts: { tests: false, implementer: ["scripts-playbook"], testWriter: [], checklist: ["Common", "Scripts"], phases: "implementer → reviewer" },
    Claude: { tests: false, implementer: ["claude-playbook"], testWriter: [], checklist: ["Common", "Claude"], phases: "implementer → reviewer" },
    "CI/Infra": { tests: false, implementer: [], testWriter: [], checklist: ["Common", "CI/Infra"], phases: "implementer → reviewer" },
    Docs: { tests: false, implementer: [], testWriter: [], checklist: ["Common", "Docs"], phases: "implementer → reviewer" },
    E2E: { tests: false, implementer: [], testWriter: [], checklist: ["Common", "E2E"], phases: "implementer → reviewer" },
  };
  for (const [area, want] of Object.entries(table)) {
    const profile = profileOf(area);
    assert.equal(profile.area, area, area);
    assert.equal(profile.known, true, area);
    assert.equal(profile.tests, want.tests, area);
    assert.equal(profile.e2e, false, area);
    assert.equal(profile.review, true, area);
    assert.deepEqual(sorted(profile.implementerPlaybooks), sorted(want.implementer.map(playbook)), area);
    assert.deepEqual(sorted(profile.testWriterPlaybooks), sorted(want.testWriter.map(playbook)), area);
    assert.deepEqual(sorted(profile.checklist), sorted(want.checklist), area);
    assert.ok(profile.label.startsWith(area), profile.label);
    assert.ok(profile.label.includes(want.phases), profile.label);
  }
  assert.equal(profileOf("Scripts").label, "Scripts (implementer → reviewer)");
});

test("profileOf without area", () => {
  const { profileOf } = areasModule;
  for (const area of [null, undefined, "", "epic", "Nope"]) {
    const profile = profileOf(area);
    assert.equal(profile.known, false, String(area));
    assert.equal(profile.tests, true, String(area));
    assert.equal(profile.review, true, String(area));
    assert.equal(profile.e2e, false, String(area));
    assert.deepEqual(sorted(profile.implementerPlaybooks), sorted([playbook("backend-playbook"), playbook("frontend-playbook")]), String(area));
    assert.deepEqual(profile.testWriterPlaybooks, [playbook("testing-playbook")], String(area));
    assert.deepEqual(sorted(profile.checklist), sorted(["Common", "Backend", "Frontend", "Scripts", "Claude", "CI/Infra", "Docs", "E2E"]), String(area));
    assert.equal(profile.label, "Brak obszaru — pełna ścieżka", String(area));
  }
});

test("profile verb prints the profile as JSON", () => {
  const profile = (args) => {
    const res = spawnSync(process.execPath, [SCRIPT, "profile", ...args], { encoding: "utf8", windowsHide: true });
    assert.equal(res.status, 0, res.stderr);
    return JSON.parse(res.stdout);
  };
  assert.equal(profile(["Backend"]).label, "Backend (test-writer → implementer → reviewer)");
  const fix = profile(["Backend", "--no-tests"]);
  assert.equal(fix.label, "Backend (implementer → reviewer)");
  assert.equal(fix.tests, false);
  assert.deepEqual(fix.implementerPlaybooks, [playbook("backend-playbook")]);
  assert.equal(profile(["--no-tests"]).label, "Brak obszaru — pełna ścieżka");
});

test("profileOf overrides", () => {
  const { profileOf } = areasModule;
  for (const area of ["Backend", "Scripts"]) {
    const noTests = profileOf(area, { skip: ["tests"] });
    assert.equal(noTests.tests, false, area);
    assert.equal(noTests.review, true, area);
    assert.equal(noTests.e2e, false, area);
    assert.ok(!noTests.label.includes("test-writer"), noTests.label);

    const noReview = profileOf(area, { skip: ["review"] });
    assert.equal(noReview.review, false, area);
    assert.equal(noReview.e2e, false, area);
    assert.ok(!noReview.label.includes("reviewer"), noReview.label);

    const e2e = profileOf(area, { e2e: true });
    assert.equal(e2e.e2e, true, area);
    assert.equal(e2e.tests, false, area);
    assert.deepEqual(sorted(e2e.checklist), sorted(["Common", "E2E"]), area);
    assert.ok(e2e.label.includes("e2e → reviewer"), e2e.label);
  }
});

test("cli prints the area", () => {
  const run = (args) => spawnSync(process.execPath, [SCRIPT, ...args], { encoding: "utf8", windowsHide: true });

  const of = run(["of", "services/a.cs"]);
  assert.equal(of.status, 0, of.stderr);
  assert.equal(of.stdout.trim(), "Backend");

  const table = run([]);
  assert.equal(table.status, 0, table.stderr);
  for (const area of AREAS.filter((a) => a !== "Fullstack")) assert.match(table.stdout, new RegExp(area.replace("/", "\\/")));
});
