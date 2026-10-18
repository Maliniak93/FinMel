// Usage: imported by the script tests - a temp copy of scripts/ wired to fake-gh.cjs through SKARBIEC_GH_BIN, with a recorded call log

import { spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const SCRIPTS = path.dirname(fileURLToPath(import.meta.url));

export const BODY = `## Goal

A goal.

## Out of scope

- nothing

## Acceptance criteria

- [ ] **AC-1** Given a, when b, then c. — proof: a.test.mjs "case"

## Code map

- scripts/x.mjs

## Test manualny

1. Open the board.
`;

const optionsOf = (name, names) => ({
  id: `F_${name}`,
  options: names.map((n) => ({ id: `O_${name}_${n}`, name: n })),
});

export const AREA_OPTIONS = ["Backend", "Frontend", "E2E", "Claude", "Scripts", "CI/Infra", "Docs", "Fullstack"];

export function buildState({ issues, order, nextNumber = 100, withArea = true, prs = [] }) {
  const map = {};
  for (const i of issues) {
    map[i.number] = {
      title: `Issue ${i.number}`,
      labels: [],
      parent: null,
      tier: "1",
      kind: "Change",
      area: null,
      branch: null,
      body: "",
      ...i,
      state: i.status === "Done" ? "CLOSED" : "OPEN",
    };
  }
  return {
    nextNumber,
    order: order ?? issues.map((i) => i.number),
    issues: map,
    prs,
    fields: {
      Status: optionsOf("Status", ["Todo", "In progress", "Done"]),
      Tier: optionsOf("Tier", ["1", "2"]),
      Kind: optionsOf("Kind", ["New", "Change", "Cleanup", "Fix"]),
      ...(withArea ? { Area: optionsOf("Area", AREA_OPTIONS) } : {}),
      Branch: { id: "F_Branch" },
    },
  };
}

function git(cwd, argv) {
  const res = spawnSync("git", ["-c", "user.name=t", "-c", "user.email=t@t", ...argv], { cwd, encoding: "utf8", windowsHide: true });
  if (res.status !== 0) throw new Error(`git ${argv.join(" ")}: ${res.stderr}`);
}

export function createSandbox(state, { branch = null } = {}) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "gh-sandbox-"));
  const scriptsDir = path.join(dir, "scripts");
  fs.mkdirSync(scriptsDir);
  for (const file of fs.readdirSync(SCRIPTS)) {
    if ((file.endsWith(".mjs") && !file.endsWith(".test.mjs")) || file === "fake-gh.cjs") {
      fs.copyFileSync(path.join(SCRIPTS, file), path.join(scriptsDir, file));
    }
  }
  const aux = `${dir}-aux`;
  fs.mkdirSync(aux);
  const statePath = path.join(aux, "fake-state.json");
  const logPath = path.join(aux, "fake-log.jsonl");
  fs.writeFileSync(statePath, JSON.stringify(state));
  fs.writeFileSync(logPath, "");
  const bodyFile = path.join(aux, "body.md");
  fs.writeFileSync(bodyFile, BODY);

  if (branch !== null) {
    const origin = path.join(dir, "..", `${path.basename(dir)}-origin.git`);
    git(dir, ["init", "-b", "master"]);
    git(dir, ["add", "-A"]);
    git(dir, ["commit", "-m", "init"]);
    fs.mkdirSync(origin);
    git(origin, ["init", "--bare", "-b", "master"]);
    git(dir, ["remote", "add", "origin", origin]);
    git(dir, ["push", "origin", "master"]);
    if (branch) git(dir, ["switch", "-c", branch]);
  }

  const preload = path.join(scriptsDir, "fake-gh.cjs").replace(/\\/g, "/");
  const env = {
    ...process.env,
    SKARBIEC_GH_BIN: process.execPath,
    NODE_OPTIONS: `--require ${JSON.stringify(preload)}`,
    FAKE_GH_STATE: statePath,
    FAKE_GH_LOG: logPath,
  };

  return {
    dir,
    bodyFile,
    run(script, args) {
      const res = spawnSync(process.execPath, [path.join(scriptsDir, script), ...args], {
        cwd: dir,
        env,
        encoding: "utf8",
        timeout: 120_000,
        windowsHide: true,
      });
      return { code: res.status, stdout: res.stdout ?? "", stderr: res.stderr ?? "", out: `${res.stdout}\n${res.stderr}` };
    },
    log: () =>
      fs
        .readFileSync(logPath, "utf8")
        .split("\n")
        .filter(Boolean)
        .map((l) => JSON.parse(l)),
    state: () => JSON.parse(fs.readFileSync(statePath, "utf8")),
    cleanup() {
      fs.rmSync(dir, { recursive: true, force: true });
      fs.rmSync(aux, { recursive: true, force: true });
      if (branch !== null) fs.rmSync(path.join(dir, "..", `${path.basename(dir)}-origin.git`), { recursive: true, force: true });
    },
  };
}
