import { existsSync, readdirSync, readFileSync, rmSync } from "node:fs";
import path from "node:path";

export const DESC_SCHEMA_VERSION = 1;

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

export function validateDescription(value, nodes) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return "field json: the description must be a JSON object";
  if (value.schema !== DESC_SCHEMA_VERSION) return `field schema: expected ${DESC_SCHEMA_VERSION}, found ${JSON.stringify(value.schema)}`;
  if (!isText(value.sources)) return "field sources: missing or not a string";
  if (!isText(value.summary)) return "field summary: missing or not a string";
  if (!Array.isArray(value.steps)) return "field steps: missing or not an array";
  for (const [i, step] of value.steps.entries()) {
    const where = `steps[${i}]`;
    if (!step || typeof step !== "object") return `field ${where}: not an object`;
    for (const key of ["id", "title", "detail"]) if (!isText(step[key])) return `field ${where}.${key}: missing or not a string`;
    for (const key of ["condition", "limit"]) {
      if (step[key] !== undefined && !isText(step[key])) return `field ${where}.${key}: not a string`;
    }
    if (step.retries !== undefined && !Number.isInteger(step.retries)) return `field ${where}.retries: not an integer`;
    if (step.agent !== undefined && !(isText(step.agent) && `agent:${step.agent}` in nodes)) {
      return `field ${where}.agent: "${step.agent}" is not an existing agent`;
    }
  }
  return null;
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
