import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { copyFileSync, mkdirSync, mkdtempSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { after, test } from "node:test";
import { fileURLToPath } from "node:url";

const SCRIPTS = path.dirname(fileURLToPath(import.meta.url));
const SLUG = "area-idea";
const AREA_OPTIONS = ["Backend", "Frontend", "E2E", "Claude", "Scripts", "CI/Infra", "Docs", "Fullstack", "epic"];
const roots = [];

after(() => roots.forEach((root) => rmSync(root, { recursive: true, force: true })));

function sandboxRepo() {
  const root = mkdtempSync(path.join(os.tmpdir(), "idea-check-"));
  roots.push(root);
  mkdirSync(path.join(root, "scripts"));
  for (const file of readdirSync(SCRIPTS)) if (file.endsWith(".mjs")) copyFileSync(path.join(SCRIPTS, file), path.join(root, "scripts", file));
  mkdirSync(path.join(root, "skarbiec-plan", "prompts"), { recursive: true });
  const git = spawnSync("git", ["init", "-q"], { cwd: root, encoding: "utf8" });
  assert.equal(git.status, 0, git.stderr);
  return root;
}

function prompt({ target = "design", area, split, priority = "P2 — do zrobienia" }) {
  const kind = target === "fix" ? "fix" : "change";
  const sections =
    target === "fix"
      ? [
          ["Objaw", "Po kliknięciu znika lista."],
          ["Kroki reprodukcji", "1. Otwórz listę.\n2. Kliknij zapis."],
          ["Oczekiwane vs faktyczne", "Oczekiwane: lista zostaje. Faktyczne: znika."],
          ["Poza zakresem", "Nic więcej."],
          ...(split === undefined ? [] : [["Proponowany podział", split]]),
        ]
      : [
          ["Cel", "Jedna zmiana w jednym miejscu."],
          ["Stan obecny", "Dziś tego nie ma."],
          ["Jak ma działać", "Ma działać tak jak opisano."],
          ["Decyzje już podjęte", "Brak."],
          ["Poza zakresem", "Nic więcej."],
          ...(split === undefined ? [] : [["Proponowany podział", split]]),
          ["Otwarte pytania dla /design", "Brak."],
        ];
  return [
    "---",
    `slug: ${SLUG}`,
    "title: Area check",
    `target: ${target}`,
    `kind: ${kind}`,
    "status: draft",
    "created: 2026-10-19",
    ...(priority === null ? [] : [`priority: ${priority}`]),
    ...(area === undefined ? [] : [`area: ${area}`]),
    "---",
    "",
    "# Area check",
    "",
    ...sections.flatMap(([name, body]) => [`## ${name}`, "", body, ""]),
  ].join("\n");
}

function writePrompt(root, options) {
  writeFileSync(path.join(root, "skarbiec-plan", "prompts", `${SLUG}.md`), prompt(options), "utf8");
}

function check(root) {
  const res = spawnSync(process.execPath, [path.join(root, "scripts", "idea.mjs"), "check", SLUG], { cwd: root, encoding: "utf8" });
  const line = res.stdout.split(/\r?\n/).find((l) => l.startsWith("IDEA_RESULT: "));
  return {
    code: res.status,
    out: `${res.stdout}${res.stderr}`,
    result: line ? JSON.parse(line.slice("IDEA_RESULT: ".length)) : null,
  };
}

test("check requires a known area", () => {
  const root = sandboxRepo();

  writePrompt(root, { area: "fullstack" });
  const ok = check(root);
  assert.equal(ok.code, 0, ok.out);
  assert.equal(ok.result.ok, true);

  for (const area of [undefined, "nope"]) {
    writePrompt(root, { area });
    const bad = check(root);
    assert.equal(bad.code, 1, `area ${area}: ${bad.out}`);
    assert.equal(bad.result.ok, false);
    assert.ok(bad.result.problems.some((p) => /\barea\b/i.test(p)), JSON.stringify(bad.result.problems));
    for (const option of AREA_OPTIONS) assert.ok(bad.out.toLowerCase().includes(option.toLowerCase()), `${option} listed for area ${area}: ${bad.out}`);
  }
});

test("epic needs a proposed split by area", () => {
  const root = sandboxRepo();
  const split = "1. Backend — API dla celu\n2. Frontend — ekran celu";

  for (const target of ["design", "fix"]) {
    writePrompt(root, { target, area: "epic", split });
    const good = check(root);
    assert.equal(good.code, 0, `${target}: ${good.out}`);
    assert.equal(good.result.ok, true);
  }

  writePrompt(root, { area: "epic" });
  const noSection = check(root);
  assert.equal(noSection.code, 1, noSection.out);
  assert.ok(noSection.result.problems.some((p) => p.includes("Proponowany podział")), JSON.stringify(noSection.result.problems));

  writePrompt(root, { area: "epic", split: "1. Backend — API dla celu" });
  const onePart = check(root);
  assert.equal(onePart.code, 1, onePart.out);
  assert.equal(onePart.result.ok, false);

  writePrompt(root, { area: "epic", split: "1. Backend — API dla celu\n2. Nic konkretnego — ekran celu" });
  const noArea = check(root);
  assert.equal(noArea.code, 1, noArea.out);
  assert.equal(noArea.result.ok, false);
});

test("check requires a priority with a reason", () => {
  const root = sandboxRepo();

  writePrompt(root, { area: "Backend", priority: "P1 — blokuje #440" });
  const ok = check(root);
  assert.equal(ok.code, 0, ok.out);
  assert.equal(ok.result.ok, true);

  for (const priority of [null, "P5 — x", "P1"]) {
    writePrompt(root, { area: "Backend", priority });
    const bad = check(root);
    assert.equal(bad.code, 1, `priority ${priority}: ${bad.out}`);
    assert.equal(bad.result.ok, false);
    assert.ok(bad.result.problems.some((p) => /\bpriority\b/.test(p)), `priority ${priority}: ${JSON.stringify(bad.result.problems)}`);
  }
});
