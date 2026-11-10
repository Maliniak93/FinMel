export const PIPELINE = ["cmd:idea", "cmd:design", "cmd:build", "cmd:fix", "wf:build-feature", "script:ship", "script:autobuild"];

export const LEVEL_WORKFLOW = "wf:build-feature";

export const LEVEL_TABLE = {
  "test-writer": { start: ["LEVELS.testWriter"] },
  implementer: { start: ["LEVELS.implementer", "LEVELS.skipTestsImplementer"], escalation: ["LEVELS.escalation"] },
  e2e: { start: ["LEVELS.e2e"], escalation: ["LEVELS.escalation"] },
  reviewer: { start: ["LEVELS.review", "LEVELS.reReview"] },
};

export const REQUIRED_LEVELS = [...new Set(Object.values(LEVEL_TABLE).flatMap((e) => [...e.start, ...(e.escalation ?? [])]))];

export const limitKeys = (id, node) => (node.facts.limits ?? []).map((l) => `${id}#${l.name}`);

export function levelRows(facts, agent) {
  const entry = LEVEL_TABLE[agent];
  if (!entry) return null;
  const rowsOf = (name) => facts.levels?.find((l) => l.name === name)?.rows ?? [];
  const rowsOfAll = (names) => names.flatMap((name, i) => rowsOf(name).map((r) => (i === 0 ? r : `${name.replace(/^LEVELS\./, "")}, ${r}`)));
  return { start: rowsOfAll(entry.start), escalation: entry.escalation ? rowsOfAll(entry.escalation) : [] };
}
