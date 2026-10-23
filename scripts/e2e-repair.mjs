// Usage: imported by autobuild.mjs (repair loop), autobuild-ci.mjs (CI fix), gh-project.mjs (report, prepare) - e2e block lines, FIX_RESULT parsing, repair and CI fix outcome, diagnosis and rebuild inputs

import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";

export const E2E_BLOCKED ="**Blocked** at `e2e`";
export const E2E_APP_BLOCKED = `${E2E_BLOCKED} (app)`;
export const E2E_DIAGNOSIS = "**E2E test defect**";

export function failedTests(blockBody) {
  const names = [...String(blockBody ?? "").matchAll(/^- test `([^`]+)`/gm)].map((m) => m[1]);
  return [...new Set(names)].sort();
}

export function repairBudget(state, { part, tests, sha, max }) {
  const entry = state?.[part];
  if (!entry) return { ok: true, used: 0 };
  if (entry.count >= max) return { ok: false, used: entry.count, reason: `${entry.count} unattended repairs of #${part} did not make it green` };
  const same = entry.sha === sha && JSON.stringify(entry.tests ?? []) === JSON.stringify(tests ?? []);
  if (same) return { ok: false, used: entry.count, reason: `the same failing test(s) ${(tests ?? []).map((t) => `"${t}"`).join(", ") || "(none named)"} at master ${sha} as the previous repair` };
  return { ok: true, used: entry.count };
}

export function recordRepair(state, { part, tests, sha, fixes = [] }) {
  const entry = state?.[part] ?? { count: 0, fixes: [] };
  return { ...state, [part]: { count: entry.count + 1, fixes: [...entry.fixes, ...fixes], tests, sha } };
}

export function loadRepairState(file) {
  try {
    return JSON.parse(readFileSync(file, "utf8"));
  } catch {
    return {};
  }
}

export function saveRepairState(file, state) {
  mkdirSync(path.dirname(file), { recursive: true });
  writeFileSync(file, `${JSON.stringify(state, null, 2)}\n`);
}

export const blockAfterBuild = ({ pr, commented, block }) => (!pr && commented ? block : null);

export const noPrReason = ({ part, commented, code }) =>
  commented ? `#${part}: no PR after the build (exit ${code}) - see the report on the issue` : `#${part}: the build did not start (no comment on the part since its start, exit ${code}) - see the build output`;

export function newestDiagnosis(comments) {
  const body = comments?.at(-1)?.body ?? "";
  return body.startsWith(E2E_DIAGNOSIS) ? body : null;
}

export function rebuildInputs({ e2e, comments, freshBranch, patchExists }) {
  const diagnosis = e2e && freshBranch ? newestDiagnosis(comments) : null;
  return { diagnosis, applyPatch: diagnosis !== null && Boolean(patchExists) };
}

export function parseFixResult(text) {
  const line = String(text ?? "").split(/\r?\n/).reverse().find((l) => l.trim().startsWith("FIX_RESULT: "));
  if (!line) return null;
  try {
    return JSON.parse(line.trim().slice("FIX_RESULT: ".length));
  } catch {
    return null;
  }
}

export function ciFixOutcome(result) {
  if (!result) return { ok: false, reason: "the fix session gave no FIX_RESULT" };
  if (!result.ok) return { ok: false, reason: `the fix session stopped: ${result.summary}` };
  if (result.kind !== "ci") return { ok: false, reason: `the fix session returned kind "${result.kind}", not "ci"` };
  return { ok: true, summary: result.summary };
}

export function repairOutcome(result, { part, nextOpen }) {
  if (!result) return { action: "stop", reason: "the repair gave no FIX_RESULT" };
  if (!result.ok) return { action: "stop", reason: `the repair stopped: ${result.summary}` };
  if (result.kind === "app") {
    const issues = result.issues ?? [];
    if (!issues.length || !nextOpen || !issues.includes(nextOpen)) return { action: "stop", reason: `the repair reported fix part(s) ${issues.join(", ") || "none"}, but the next open part is #${nextOpen}` };
    return { action: "next", reason: `fix part(s) ${issues.map((n) => `#${n}`).join(", ")} ahead of #${part} - ${result.summary}` };
  }
  if (result.kind === "plan") return { action: "rebuild", reason: `the epic's plan corrected - ${result.summary}` };
  if (result.kind === "test") {
    if (nextOpen !== part) return { action: "stop", reason: `the repair diagnosed a test defect in #${part}, but the next open part is #${nextOpen}` };
    return { action: "rebuild", reason: `test defect in #${part} - ${result.summary}` };
  }
  return { action: "stop", reason: `the repair returned an unknown kind "${result.kind}"` };
}

export function e2eBlockLines(run) {
  const lines = [];
  for (const f of run.failed ?? []) lines.push(`- test \`${f.test}\`${f.step ? ` — ${f.step}` : ""}${f.trace ? ` — trace: \`${f.trace}\`` : ""}`);
  if (run.reportPath) lines.push(`- report: \`${run.reportPath}\``);
  if (run.patch) lines.push(`- patch: \`${run.patch}\``);
  if (run.selfCheck) lines.push(`- self-check: ${run.selfCheck}`);
  return lines;
}
