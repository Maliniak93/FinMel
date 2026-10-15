// Usage: imported by epic.mjs (repair loop), gh-project.mjs (report, prepare) - e2e block lines, FIX_RESULT parsing, repair outcome, diagnosis and rebuild inputs

export const E2E_BLOCKED = "**Blocked** at `e2e`";
export const E2E_APP_BLOCKED = `${E2E_BLOCKED} (app)`;
export const E2E_DIAGNOSIS = "**E2E test defect**";

export function appBlocks(comments) {
  return (comments ?? []).filter((c) => (c.body ?? "").startsWith(E2E_APP_BLOCKED)).length;
}

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
