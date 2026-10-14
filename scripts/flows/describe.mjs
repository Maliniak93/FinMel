import { existsSync, readdirSync, readFileSync, rmSync } from "node:fs";
import path from "node:path";
import { measurableKeys } from "./measure.mjs";
import { PIPELINE } from "./pipeline.mjs";

export const DESC_SCHEMA_VERSION = 3;

export const descName = (id) => `${id.replace(":", "-")}.json`;

export const descDir = (out) => path.join(out, ".work", "desc");

export function readDescription(out, id) {
  const file = path.join(descDir(out), descName(id));
  if (!existsSync(file)) return { exists: false };
  const text = readFileSync(file, "utf8");
  try {
    return { exists: true, file, value: JSON.parse(text) };
  } catch (error) {
    return { exists: true, file, error: `is not valid JSON (${error.message})` };
  }
}

const isText = (v) => typeof v === "string" && v.trim() !== "";

export function validateLimit(limit, nodes) {
  if (!limit || typeof limit !== "object" || Array.isArray(limit)) return "not an object { ref?, text? }";
  if (limit.ref === undefined && limit.text === undefined) return "needs ref or text";
  if (limit.text !== undefined && !isText(limit.text)) return "text: not a string";
  if (limit.ref !== undefined) {
    const m = isText(limit.ref) ? limit.ref.match(/^(.+)#([A-Za-z0-9_]+)$/) : null;
    if (!m) return `ref: "${limit.ref}" is not <node id>#<constant>`;
    if (!(m[1] in nodes)) return `ref: "${limit.ref}" names no existing node`;
    if (!(nodes[m[1]].facts.limits ?? []).some((l) => l.name === m[2])) return `ref: "${limit.ref}" is not a limit constant in the facts of ${m[1]}`;
  }
  return null;
}

function validateGraph(steps) {
  const ids = new Set(steps.map((s) => s.id));
  if (ids.size !== steps.length) return "field steps: duplicate step id";
  for (const [i, step] of steps.entries()) {
    if (step.next === undefined) return `field steps[${i}] (step "${step.id}").next: missing (use [] for a terminal step)`;
    for (const edge of step.next) {
      if (!ids.has(edge.to)) return `field steps[${i}] (step "${step.id}").next: "${edge.to}" is not a step of this node`;
    }
  }
  if (!steps.length) return null;
  const seen = new Set([steps[0].id]);
  const queue = [steps[0]];
  while (queue.length) {
    for (const edge of queue.shift().next) {
      if (seen.has(edge.to)) continue;
      seen.add(edge.to);
      queue.push(steps.find((s) => s.id === edge.to));
    }
  }
  const lost = steps.find((s) => !seen.has(s.id));
  return lost ? `field steps (step "${lost.id}"): unreachable from the first step "${steps[0].id}"` : null;
}

export function validateDescription(value, nodes, nodeId, measurements = {}) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return "field json: the description must be a JSON object";
  if (value.schema !== DESC_SCHEMA_VERSION) return `field schema: expected ${DESC_SCHEMA_VERSION}, found ${JSON.stringify(value.schema)}`;
  if (!isText(value.sources)) return "field sources: missing or not a string";
  if (!isText(value.summary)) return "field summary: missing or not a string";
  if (!Array.isArray(value.steps)) return "field steps: missing or not an array";
  for (const [i, step] of value.steps.entries()) {
    const where = `steps[${i}]`;
    if (!step || typeof step !== "object") return `field ${where}: not an object`;
    for (const key of ["id", "title", "detail"]) if (!isText(step[key])) return `field ${where}.${key}: missing or not a string`;
    const named = `${where} (step "${step.id}")`;
    if (step.condition !== undefined && !isText(step.condition)) return `field ${named}.condition: not a string`;
    if (step.limit !== undefined) {
      const problem = validateLimit(step.limit, nodes);
      if (problem) return `field ${named}.limit: ${problem}`;
    }
    if (step.retries !== undefined && !Number.isInteger(step.retries)) return `field ${named}.retries: not an integer`;
    if (step.measure !== undefined && !(isText(step.measure) && measurableKeys(nodeId, measurements).includes(step.measure))) {
      return `field ${named}.measure: "${step.measure}" is not a measurable key of ${nodeId} (keys: ${measurableKeys(nodeId, measurements).join(", ") || "none"})`;
    }
    if (step.agent !== undefined && !(isText(step.agent) && `agent:${step.agent}` in nodes)) {
      return `field ${named}.agent: "${step.agent}" is not an existing agent`;
    }
    if (step.calls !== undefined && !(isText(step.calls) && step.calls in nodes)) {
      return `field ${named}.calls: "${step.calls}" is not an existing node`;
    }
    if (step.next !== undefined) {
      if (!Array.isArray(step.next)) return `field ${named}.next: not an array`;
      for (const edge of step.next) {
        if (!edge || typeof edge !== "object" || !isText(edge.to)) return `field ${named}.next: every entry needs a "to" step id`;
        if (edge.when !== undefined && !isText(edge.when)) return `field ${named}.next: "when" is not a string`;
      }
    }
  }
  return PIPELINE.includes(nodeId) ? validateGraph(value.steps) : null;
}

export function isStale(out, id, node) {
  const description = readDescription(out, id);
  if (!description.exists || description.error) return true;
  return description.value?.schema !== DESC_SCHEMA_VERSION || description.value?.sources !== node.hash;
}

export function removeOrphans(out, nodes) {
  const dir = descDir(out);
  if (!existsSync(dir)) return [];
  const known = new Set(Object.keys(nodes).map(descName));
  const removed = [];
  for (const file of readdirSync(dir)) {
    if (file.endsWith(".json") && !known.has(file)) {
      rmSync(path.join(dir, file), { force: true });
      removed.push(file);
    }
  }
  return removed;
}
