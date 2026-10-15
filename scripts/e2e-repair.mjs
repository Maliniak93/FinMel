// Usage: imported by gh-project.mjs report; e2eBlockLines(run) formats the e2e lines of a blocked run report

export function e2eBlockLines(run) {
  const lines = [];
  for (const f of run.failed ?? []) lines.push(`- test \`${f.test}\`${f.step ? ` — ${f.step}` : ""}${f.trace ? ` — trace: \`${f.trace}\`` : ""}`);
  if (run.reportPath) lines.push(`- report: \`${run.reportPath}\``);
  if (run.patch) lines.push(`- patch: \`${run.patch}\``);
  if (run.selfCheck) lines.push(`- self-check: ${run.selfCheck}`);
  return lines;
}
