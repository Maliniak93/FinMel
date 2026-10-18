import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
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

test("cli prints the area", () => {
  const run = (args) => spawnSync(process.execPath, [SCRIPT, ...args], { encoding: "utf8", windowsHide: true });

  const of = run(["of", "services/a.cs"]);
  assert.equal(of.status, 0, of.stderr);
  assert.equal(of.stdout.trim(), "Backend");

  const table = run([]);
  assert.equal(table.status, 0, table.stderr);
  for (const area of AREAS.filter((a) => a !== "Fullstack")) assert.match(table.stdout, new RegExp(area.replace("/", "\\/")));
});
