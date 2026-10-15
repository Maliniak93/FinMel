// Usage: imported by every script that runs gh - the one place that spawns it (SKARBIEC_GH_BIN overrides the binary), plus the rate-limit and pure card, paging and check helpers; runs nothing on import

import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

export const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
export const OWNER = "Maliniak93";
export const REPO = `${OWNER}/FinMel`;
export const PROJECT_NUMBER = 1;

const MAX_BUFFER = 64 * 1024 * 1024;

export function spawnGh(argv, { input, cwd = REPO_ROOT, timeout } = {}) {
  return spawnSync(process.env.SKARBIEC_GH_BIN ?? "gh", argv, {
    cwd,
    encoding: "utf8",
    windowsHide: true,
    shell: false,
    input,
    timeout,
    maxBuffer: MAX_BUFFER,
  });
}

export function gh(argv, { input, timeout } = {}) {
  const res = spawnGh(argv, { input, timeout });
  if (res.error) throw new Error(`gh ${argv[0]} ${argv[1] ?? ""}: ${res.error.message}`);
  if (res.status !== 0) throw new Error(`gh ${argv.slice(0, 2).join(" ")}: ${(res.stderr ?? "").trim()}`);
  return res.stdout.trim();
}

export function graphql(query, variables = {}) {
  const out = JSON.parse(gh(["api", "graphql", "--input", "-"], { input: JSON.stringify({ query, variables }) }));
  if (out.errors?.length) throw new Error(`gh api graphql: ${out.errors.map((e) => e.message).join("; ")}`);
  return out.data;
}

const bucketOf = ({ limit, remaining, reset }) => ({ limit, remaining, reset });

export function rateLimit() {
  const { resources } = JSON.parse(gh(["api", "rate_limit"]));
  return { graphql: bucketOf(resources.graphql), core: bucketOf(resources.core) };
}

const clock = (epochSeconds) => {
  const d = new Date(epochSeconds * 1000);
  return `${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`;
};

export function usageLine(snapshot) {
  const { graphql: g, core } = snapshot;
  return `GitHub: GraphQL ${g.remaining}/${g.limit}, REST ${core.remaining}/${core.limit}, reset ${clock(g.reset)}`;
}

export function usageDelta(before, after) {
  const reset = before.graphql.reset !== after.graphql.reset || before.core.reset !== after.core.reset;
  return {
    reset,
    graphql: before.graphql.remaining - after.graphql.remaining,
    rest: before.core.remaining - after.core.remaining,
  };
}

export function reportUsage(start, end) {
  if (!start) return `Limit at report: ${usageLine(end)}`;
  const delta = usageDelta(start, end);
  const used = delta.reset ? "window reset in between" : `used GraphQL ${delta.graphql}, REST ${delta.rest}`;
  return `Limit at prepare: ${usageLine(start)} · at report: ${usageLine(end)} · ${used}`;
}

export function cardFromIssueNode(node, projectNumber = PROJECT_NUMBER) {
  const labels = node.labels.nodes.map((l) => l.name);
  const item = node.projectItems.nodes.find((i) => i.project.number === projectNumber);
  return {
    number: node.number,
    title: node.title,
    url: node.url,
    state: node.state,
    labels,
    inProject: Boolean(item),
    status: item?.status?.name ?? null,
    tier: item?.tier?.name ?? null,
    kind: item?.kind?.name ?? null,
    branch: item?.branch?.text ?? null,
    epic: labels.includes("epic"),
    skipTests: labels.includes("skip-tests"),
    e2e: labels.includes("e2e"),
    parent: node.parent?.number ?? null,
    subIssues: node.subIssues.nodes.map((s) => ({ number: s.number, title: s.title, state: s.state.toUpperCase() })),
  };
}

export async function listPages(fetchPage) {
  const nodes = [];
  let cursor;
  while (true) {
    const page = await fetchPage(cursor);
    nodes.push(...page.nodes);
    if (!page.pageInfo.hasNextPage) return nodes;
    cursor = page.pageInfo.endCursor;
  }
}

const CHECK_BUCKETS = {
  success: "pass",
  skipped: "skipping",
  neutral: "skipping",
  failure: "fail",
  timed_out: "fail",
  cancelled: "cancel",
};

export function checkBuckets(checkRuns) {
  return checkRuns.map((run) => ({
    name: run.name,
    state: run.conclusion ?? run.status,
    bucket: run.status !== "completed" ? "pending" : (CHECK_BUCKETS[run.conclusion] ?? "fail"),
    link: run.html_url,
  }));
}
