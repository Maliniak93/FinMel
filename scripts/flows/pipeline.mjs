export const PIPELINE = ["cmd:idea", "cmd:design", "cmd:build", "cmd:fix", "wf:build-feature", "script:ship", "script:epic"];

export const LEVEL_WORKFLOW = "wf:build-feature";

export const LEVEL_TABLE = {
  "test-writer": { start: "testWriter" },
  implementer: { start: "startLevel", escalation: "escalatedLevel" },
  e2e: { start: "e2eStartLevel", escalation: "escalatedLevel" },
  reviewer: { model: "OPUS", effort: "reviewEffort" },
};

export const REQUIRED_LEVELS = ["testWriter", "startLevel", "escalatedLevel", "e2eStartLevel", "reviewEffort"];

export const limitKeys = (id, node) => (node.facts.limits ?? []).map((l) => `${id}#${l.name}`);

export function levelRows(facts, agent) {
  const entry = LEVEL_TABLE[agent];
  if (!entry) return null;
  const rowsOf = (name) => facts.levels?.find((l) => l.name === name)?.rows ?? [];
  if (entry.model) {
    const model = facts.constants?.[entry.model] ?? entry.model;
    return { start: rowsOf(entry.effort).map((r) => r.replace(/ → (.*)$/, (_, effort) => ` → ${model}/${effort}`)), escalation: [] };
  }
  return { start: rowsOf(entry.start), escalation: entry.escalation ? rowsOf(entry.escalation) : [] };
}
