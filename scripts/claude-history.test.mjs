import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { HISTORY, REPO_ROOT, historyDir, historyDirs, slugOf } from "./claude-history.mjs";

const SCRIPTS = path.dirname(fileURLToPath(import.meta.url));

function withConfigDir(value, body) {
  const before = process.env.CLAUDE_CONFIG_DIR;
  if (value === undefined) delete process.env.CLAUDE_CONFIG_DIR;
  else process.env.CLAUDE_CONFIG_DIR = value;
  try {
    return body();
  } finally {
    if (before === undefined) delete process.env.CLAUDE_CONFIG_DIR;
    else process.env.CLAUDE_CONFIG_DIR = before;
  }
}

test("slug replaces every non-alphanumeric character", () => {
  assert.equal(slugOf("C:\\Users\\domin\\Desktop\\FinMel"), "C--Users-domin-Desktop-FinMel");
  assert.equal(slugOf("/home/me/fin.mel"), "-home-me-fin-mel");
});

test("roots are the repo folder and its dash-prefixed siblings", () => {
  const config = mkdtempSync(path.join(os.tmpdir(), "history-"));
  try {
    const projects = path.join(config, "projects");
    for (const name of ["C--a-b", "C--a-b-wt1", "C--a-b-wt2", "C--a-bc", "C--a", "other"]) mkdirSync(path.join(projects, name), { recursive: true });
    writeFileSync(path.join(projects, "C--a-b-file"), "not a folder");

    const dirs = historyDirs({ repoRoot: "C:\\a\\b", configDir: config });
    assert.deepEqual(
      dirs.map((d) => path.basename(d)),
      ["C--a-b", "C--a-b-wt1", "C--a-b-wt2"],
    );
    assert.equal(historyDir({ repoRoot: "C:\\a\\b", configDir: config }), dirs[0]);
  } finally {
    rmSync(config, { recursive: true, force: true });
  }
});

test("a missing projects folder still names the repo folder", () => {
  const dirs = historyDirs({ repoRoot: "C:\\a\\b", configDir: path.join(os.tmpdir(), "no-such-claude-config") });
  assert.equal(dirs.length, 1);
  assert.equal(path.basename(dirs[0]), "C--a-b");
});

test("CLAUDE_CONFIG_DIR set or unset decides the projects folder", () => {
  const set = path.join(os.tmpdir(), "custom-claude");
  assert.equal(
    withConfigDir(set, () => historyDir({ repoRoot: "C:\\a\\b" })),
    path.join(set, "projects", "C--a-b"),
  );
  assert.equal(
    withConfigDir(undefined, () => historyDir({ repoRoot: "C:\\a\\b" })),
    path.join(os.homedir(), ".claude", "projects", "C--a-b"),
  );
  assert.equal(HISTORY, historyDir({ repoRoot: REPO_ROOT, configDir: undefined }));
});

test("build-replay and run-cost take the folder from the helper", () => {
  for (const file of ["build-replay.mjs", "run-cost.mjs"]) {
    const source = readFileSync(path.join(SCRIPTS, file), "utf8");
    assert.match(source, /import \{[^}]*\bHISTORY\b[^}]*\} from "\.\/claude-history\.mjs"/, file);
    assert.ok(!source.includes("[^A-Za-z0-9]"), `${file} must not compute the slug itself`);
    assert.ok(!source.includes("CLAUDE_CONFIG_DIR"), `${file} must not read CLAUDE_CONFIG_DIR itself`);
  }
});
