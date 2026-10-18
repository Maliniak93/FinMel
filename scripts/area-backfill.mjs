#!/usr/bin/env node
// Usage: node scripts/area-backfill.mjs [--apply]   fill the Area field on every non-epic ticket that has none; a dry run unless --apply
//   Done tickets: the merged PR whose head branch is the Branch field; any other status: the paths in the body's `## Code map`
//   exit 1 on an error, 4 when the GitHub limit is exhausted

import { areaOfFiles, areaOfPath } from "./areas.mjs";
import { GhLimitError, OWNER, PROJECT_NUMBER, exitOnGhLimit, gh, graphql, listPages, rateLimit, usageLine } from "./gh.mjs";

const BATCH = 20;

const FIELDS_QUERY = `query ($owner: String!, $number: Int!) {
  user(login: $owner) {
    projectV2(number: $number) {
      id
      fields(first: 50) {
        nodes {
          ... on ProjectV2FieldCommon { id name }
          ... on ProjectV2SingleSelectField { options { id name } }
        }
      }
    }
  }
}`;

const ITEMS_QUERY = `query ($owner: String!, $number: Int!, $cursor: String) {
  user(login: $owner) {
    projectV2(number: $number) {
      items(first: 100, after: $cursor) {
        pageInfo { hasNextPage endCursor }
        nodes {
          id
          status: fieldValueByName(name: "Status") { ... on ProjectV2ItemFieldSingleSelectValue { name } }
          area: fieldValueByName(name: "Area") { ... on ProjectV2ItemFieldSingleSelectValue { name } }
          branch: fieldValueByName(name: "Branch") { ... on ProjectV2ItemFieldTextValue { text } }
          content {
            __typename
            ... on Issue { number title body labels(first: 10) { nodes { name } } }
          }
        }
      }
    }
  }
}`;

function codeMapPaths(body) {
  const section = (body ?? "").replace(/\r\n/g, "\n").split(/^## /m).find((s) => /^Code map\s*\n/.test(s));
  if (!section) return [];
  const paths = [];
  for (const line of section.split("\n").slice(1)) {
    const ticked = [...line.matchAll(/`([^`]+)`/g)].map((m) => m[1]);
    const tokens = ticked.length ? ticked : [line.replace(/^\s*[-*]\s*/, "").split(/\s+/)[0]];
    for (const token of tokens) {
      const file = token.replace(/:\d[\d,\s-]*$/, "");
      if (areaOfPath(file)) paths.push(file);
    }
  }
  return paths;
}

function mergedPrs() {
  const prs = JSON.parse(gh(["pr", "list", "--state", "merged", "--limit", "500", "--json", "number,headRefName,files"]));
  return new Map(prs.map((pr) => [pr.headRefName, pr]));
}

async function projectItems() {
  const nodes = await listPages(
    async (cursor) => graphql(ITEMS_QUERY, { owner: OWNER, number: PROJECT_NUMBER, cursor: cursor ?? null }).user.projectV2.items,
  );
  return nodes.filter((item) => item.content?.__typename === "Issue");
}

function resolve(item, prs) {
  const { number, body } = item.content;
  if (item.status?.name === "Done" && item.branch?.text) {
    const pr = prs.get(item.branch.text);
    const area = pr ? areaOfFiles(pr.files.map((f) => f.path)) : null;
    if (area) return { number, area, source: `PR #${pr.number}` };
  }
  const area = areaOfFiles(codeMapPaths(body));
  return { number, area, source: "Code map" };
}

function applyBatch(batch, projectId, fieldId, optionIds) {
  const mutations = batch.map(
    (r, k) =>
      `m${k}: updateProjectV2ItemFieldValue(input: {projectId: ${JSON.stringify(projectId)}, itemId: ${JSON.stringify(r.itemId)}, fieldId: ${JSON.stringify(fieldId)}, value: {singleSelectOptionId: ${JSON.stringify(optionIds.get(r.area))}}}) { projectV2Item { id } }`,
  );
  graphql(`mutation { ${mutations.join(" ")} }`);
}

async function main() {
  const apply = process.argv.slice(2).includes("--apply");
  const { projectV2 } = graphql(FIELDS_QUERY, { owner: OWNER, number: PROJECT_NUMBER }).user;
  const areaField = projectV2.fields.nodes.find((f) => f.name === "Area");
  if (!areaField?.options) throw new Error("the project has no Area field — run node scripts/gh-project.mjs init");

  const items = (await projectItems()).filter(
    (i) => !i.content.labels.nodes.some((l) => l.name === "epic") && !i.area,
  );
  const prs = mergedPrs();

  const resolved = [];
  const unresolved = [];
  for (const item of items) {
    const r = resolve(item, prs);
    if (r.area) resolved.push({ ...r, itemId: item.id });
    else unresolved.push(item.content);
  }

  for (const r of resolved) console.log(`#${r.number} → ${r.area} (${r.source})`);
  console.log(`${resolved.length} resolved, ${unresolved.length} unresolved, ${apply ? "applying" : "dry run (use --apply to write)"}`);
  if (unresolved.length) {
    console.log("Unresolved (no merged PR and no Code map path) — set by hand with: node scripts/gh-project.mjs set <n> Area <area>");
    for (const u of unresolved) console.log(`#${u.number} ${u.title}`);
  }

  if (!apply) return;
  const optionIds = new Map(areaField.options.map((o) => [o.name, o.id]));
  for (let at = 0; at < resolved.length; at += BATCH)
    applyBatch(resolved.slice(at, at + BATCH), projectV2.id, areaField.id, optionIds);
  console.log(usageLine(rateLimit()));
}

try {
  await main();
} catch (err) {
  if (err instanceof GhLimitError) exitOnGhLimit(err);
  console.error(err.message);
  process.exit(1);
}
