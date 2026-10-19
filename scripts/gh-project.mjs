#!/usr/bin/env node
// Usage: node scripts/gh-project.mjs <verb> ...
//   init                                                   create the custom fields and labels when missing
//   create --title <t> --body-file <p> --slug <s> --tier 1|2 --kind new|change|cleanup|fix --area <area> [--skip-tests] [--parent <n> [--before <m>]] [--epic] [--e2e] [--priority P0|P1|P2|P3] [--queue top|bottom|before:N|after:N]   --priority (top-level only, default P2) places the item in its priority band and --queue refines inside it; --e2e needs --parent and excludes --skip-tests and defaults --area to E2E; --area (see areas.mjs) is required except with --epic, which refuses it; --before puts the new part ahead of sibling #m; --queue places a top-level item on the board (default bottom), a part goes right after the previous part
//   check --body-file <p> [--epic] [--e2e]                 dry run of create's cleaning and checking
//   edit <n> [--body-file <p>] [--tier 1|2] [--area <area>] [--parent <n>] replace the body, set Tier or Area, or attach the issue to an epic
//   section <n> --heading <h> --file <f> [--dry-run]       replace one `## <h>` section of the body (inserted before ## Verification when absent)
//   set <n> <field> <value>                                set one project field
//   get <n> [--out <path> [--raw]]                         print the issue as JSON (parent, lastPart), optionally write a local copy
//   prepare <n> [--tier 1|2] [--skip tests,review] [--fresh] [--dry-run]   /build's pre-workflow step: validates, cuts or switches to the branch (an e2e part's idle branch is recut from origin/master), moves the card; a last epic part also gets the epic and sibling bodies; on a resumed dirty branch it replays the interrupted run's finished steps (--fresh starts over)
//   report <n> [--json '<run report>']                     post a run report (JSON on stdin otherwise); a shipped one also replaces ## Test manualny per manualTest
//   list                                                   every issue on the project as JSON, in board order
//   queue                                                  the build queue as JSON: top-level items, In progress then Todo, board order (manual, an epic's next part)
//   move <n> top|bottom|before:N|after:N                   place a queue item on the board; a Todo item stays in its priority band
//   priority <n> <P0|P1|P2|P3>                             set the priority and re-place a Todo item in its band; a part follows its epic
//   priority --where <P> [--queue <spec>] [--body-file <p>] preview the queue spot of a new item and the dependency raises of its body, writes nothing
//   priority --backfill [--apply]                          set P2 on every open top-level item without Priority; no card moves
//   comment <n> --body-file <path>                         post a comment
//   tick <n>                                               tick every acceptance-criterion checkbox
//   exit 1 on an error, 4 when the GitHub limit is exhausted (stderr: reset time and a GITHUB_LIMIT line; never waits)

import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import {
  existsSync,
  mkdirSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { findReplay, renderResumeScript } from "./build-replay.mjs";
import { e2eBlockLines, rebuildInputs } from "./e2e-repair.mjs";
import {
  GhLimitError,
  OWNER,
  PROJECT_NUMBER,
  REPO,
  cardFromIssueNode,
  exitOnGhLimit,
  gh,
  graphql,
  hasRunMarker,
  limitFromGhFailure,
  listPages,
  rateLimit,
  reportUsage,
  runMarker,
  spawnGh,
} from "./gh.mjs";
import { AREAS, parseArea } from "./areas.mjs";
import {
  DEFAULT_PRIORITY,
  PRIORITIES,
  blockingDependent,
  describeSpot,
  inBand,
  parsePriority,
  placeTarget,
  raisePlan,
  shortOf,
} from "./priority.mjs";
import { positionTarget, queueOf } from "./queue.mjs";

const REPO_ROOT = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..",
);
const PROJECT = String(PROJECT_NUMBER);
const REPO_NAME = REPO.split("/")[1];
const USAGE_START_FILE = path.join(REPO_ROOT, ".git", "gh-usage-start.json");
const ISSUES_DIR = "skarbiec-plan/issues";

const FIELDS = [
  { name: "Tier", type: "SINGLE_SELECT", options: ["1", "2"] },
  {
    name: "Kind",
    type: "SINGLE_SELECT",
    options: ["New", "Change", "Cleanup", "Fix"],
  },
  { name: "Area", type: "SINGLE_SELECT", options: AREAS },
  { name: "Priority", type: "SINGLE_SELECT", options: PRIORITIES },
  { name: "Branch", type: "TEXT" },
];
const LABELS = [
  {
    name: "spec",
    color: "5319e7",
    description: "Build-ready spec written by /design",
  },
  {
    name: "epic",
    color: "3e4b9e",
    description:
      "Umbrella of a split spec; its sub-issues are built one by one",
  },
  {
    name: "skip-tests",
    color: "c5def5",
    description:
      "Spec adds or alters no behaviour; /build skips the test phase",
  },
  {
    name: "e2e",
    color: "0e8a16",
    description:
      "Last part of an epic: automates the epic's manual test plan as Playwright tests",
  },
  {
    name: "autobuild",
    color: "fbca04",
    description: "Built unattended by autobuild.mjs; an In progress item without it is a manual build",
  },
];
const KINDS = { new: "New", change: "Change", cleanup: "Cleanup", fix: "Fix" };
const BOOLEAN_FLAGS = new Set([
  "skip-tests",
  "epic",
  "e2e",
  "raw",
  "dry-run",
  "fresh",
  "backfill",
  "apply",
]);
const SKIPPABLE = new Set(["tests", "review"]);

function git(argv) {
  const res = spawnSync("git", argv, {
    cwd: REPO_ROOT,
    encoding: "utf8",
    windowsHide: true,
    shell: false,
  });
  return {
    ok: !res.error && res.status === 0,
    out: (res.stdout ?? "").trimEnd(),
    err: res.error?.message ?? (res.stderr ?? "").trim().split(/\r?\n/)[0],
  };
}

let projectMeta = null;
const itemIds = new Map();
const bodies = new Map();

// One query per process: the project id and every field with its options.
function projectFields() {
  if (projectMeta) return projectMeta;
  const { user } = graphql(
    `query ($owner: String!, $number: Int!) {
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
    }`,
    { owner: OWNER, number: PROJECT_NUMBER },
  );
  const { id, fields } = user.projectV2;
  projectMeta = { id, fields: new Map(fields.nodes.map((f) => [f.name, f])) };
  return projectMeta;
}

function itemIdOf(number) {
  const known = itemIds.get(Number(number));
  if (known) return known;
  const { repository } = graphql(
    `query ($owner: String!, $name: String!, $number: Int!) {
      repository(owner: $owner, name: $name) {
        issue(number: $number) {
          projectItems(first: 10) { nodes { id project { number } } }
        }
      }
    }`,
    { owner: OWNER, name: REPO_NAME, number: Number(number) },
  );
  const item = repository.issue?.projectItems.nodes.find(
    (i) => i.project.number === PROJECT_NUMBER,
  );
  if (!item) throw new Error(`#${number} is not a card on project ${PROJECT}`);
  itemIds.set(Number(number), item.id);
  return item.id;
}

function setField(number, field, value) {
  const meta = projectFields();
  const def = meta.fields.get(field);
  if (!def?.id) throw new Error(`project field ${field} does not exist`);
  let fieldValue = { text: value };
  if (def.options) {
    const option = def.options.find((o) => o.name === value);
    if (!option)
      throw new Error(
        `${field} has no option ${value}: ${def.options.map((o) => o.name).join(", ")}`,
      );
    fieldValue = { singleSelectOptionId: option.id };
  }
  graphql(
    `mutation ($project: ID!, $item: ID!, $field: ID!, $value: ProjectV2FieldValue!) {
      updateProjectV2ItemFieldValue(input: { projectId: $project, itemId: $item, fieldId: $field, value: $value }) {
        projectV2Item { id }
      }
    }`,
    {
      project: meta.id,
      item: itemIdOf(number),
      field: def.id,
      value: fieldValue,
    },
  );
}

function addToProject(number) {
  const meta = projectFields();
  const { repository } = graphql(
    `query ($owner: String!, $name: String!, $number: Int!) {
      repository(owner: $owner, name: $name) { issue(number: $number) { id } }
    }`,
    { owner: OWNER, name: REPO_NAME, number: Number(number) },
  );
  const { addProjectV2ItemById } = graphql(
    `mutation ($project: ID!, $content: ID!) {
      addProjectV2ItemById(input: { projectId: $project, contentId: $content }) {
        item { id }
      }
    }`,
    { project: meta.id, content: repository.issue.id },
  );
  itemIds.set(Number(number), addProjectV2ItemById.item.id);
}

// A null afterId puts the item at the top of the board.
function setPosition(number, afterId) {
  const meta = projectFields();
  graphql(
    `mutation ($project: ID!, $item: ID!, $after: ID) {
      updateProjectV2ItemPosition(input: { projectId: $project, itemId: $item, afterId: $after }) {
        items(first: 1) { nodes { id } }
      }
    }`,
    { project: meta.id, item: itemIdOf(number), after: afterId },
  );
}

function parseFlags(argv) {
  const flags = {};
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (!arg.startsWith("--")) throw new Error(`unexpected argument: ${arg}`);
    const key = arg.slice(2);
    if (BOOLEAN_FLAGS.has(key)) flags[key] = true;
    else flags[key] = argv[++i];
  }
  return flags;
}

function readStdin() {
  try {
    return readFileSync(0, "utf8");
  } catch {
    return "";
  }
}

const isTableRule = (line) => /^\|[\s|:-]*\|$/.test(line.trim());
const isEmptyRow = (line) => /^\|(\s*\|)+\s*$/.test(line.trim());

function hasContent(lines) {
  const meaningful = lines.filter(
    (l) => l.trim() && !/^#{2,6} /.test(l) && !isTableRule(l),
  );
  const bareTableHeader =
    meaningful.length === 1 && meaningful[0].trim().startsWith("|");
  return meaningful.length > 0 && !bareTableHeader;
}

function dropEmpty(lines, level) {
  const marker = new RegExp(`^#{${level}} `);
  const out = [];
  let section = null;
  const flush = () => {
    if (!section) return;
    const body =
      level < 3 ? dropEmpty(section.slice(1), level + 1) : section.slice(1);
    if (hasContent(body)) out.push(section[0], ...body);
  };
  for (const line of lines) {
    if (marker.test(line)) {
      flush();
      section = [line];
    } else if (section) section.push(line);
    else out.push(line);
  }
  flush();
  return out;
}

function cleanBody(text) {
  const lines = text
    .replace(/\r\n/g, "\n")
    .replace(/<!--[\s\S]*?-->/g, "")
    .split("\n")
    .filter((l) => !isEmptyRow(l));
  return dropEmpty(lines, 2)
    .join("\n")
    .replace(/\n{3,}/g, "\n\n")
    .trim();
}

function section(body, name) {
  const lines = body.split("\n");
  const start = lines.findIndex((l) => l.trim() === `## ${name}`);
  if (start === -1) return "";
  const end = lines.findIndex((l, i) => i > start && /^## /.test(l));
  return lines
    .slice(start + 1, end === -1 ? undefined : end)
    .join("\n")
    .trim();
}

function replaceSection(body, name, content) {
  const lines = body.replace(/\r\n/g, "\n").split("\n");
  const text = content
    .replace(/\r\n/g, "\n")
    .replace(new RegExp(`^\\s*## ${name}[ \\t]*\\n`), "")
    .trim();
  const block = [`## ${name}`, "", ...text.split("\n"), ""];
  const start = lines.findIndex((l) => l.trim() === `## ${name}`);
  if (start !== -1) {
    const next = lines.findIndex((l, i) => i > start && /^## /.test(l));
    const end = next === -1 ? lines.length : next;
    return [...lines.slice(0, start), ...block, ...lines.slice(end)]
      .join("\n")
      .trim();
  }
  const verification = lines.findIndex((l) => l.trim() === "## Verification");
  if (verification !== -1)
    return [
      ...lines.slice(0, verification),
      ...block,
      ...lines.slice(verification),
    ]
      .join("\n")
      .trim();
  return [...lines, "", ...block].join("\n").trim();
}

function dependsOn(body) {
  return [
    ...new Set(
      [...section(body, "Depends on").matchAll(/#(\d+)/g)].map((m) =>
        Number(m[1]),
      ),
    ),
  ];
}

const TEST_PLAN = "Test manualny";
const PLAN_MARKER = /weryfikacja w planie epica/i;

function testPlanProblems(body) {
  const plan = section(body, TEST_PLAN);
  if (!plan) return [];
  if (/^\s*[-*] \[[ xX]\]/m.test(plan))
    return [
      `\`## ${TEST_PLAN}\` holds a \`- [ ]\` checkbox line — its steps are numbered (\`1.\`), not checkboxes`,
    ];
  if (!/^\s*\d+\.\s/m.test(plan) && !PLAN_MARKER.test(plan))
    return [
      `\`## ${TEST_PLAN}\` has neither a numbered step (\`1.\`) nor the one-line \`Weryfikacja w planie epica, krok <n>.\``,
    ];
  return [];
}

function lintWarnings(body, { epic, e2e }) {
  const warnings = [];
  if (!epic && !section(body, "Code map"))
    warnings.push(
      "no `## Code map` section — the test-writer, implementer and reviewer will each rediscover the code",
    );
  if (!e2e && !section(body, TEST_PLAN))
    warnings.push(
      `no \`## ${TEST_PLAN}\` section — the manual check after the merge has no steps`,
    );
  return warnings;
}

function lintBody(body, { epic }) {
  const problems = [];
  if (!/^## Goal\s*$/m.test(body))
    problems.push("no `## Goal` section (or it is empty)");
  problems.push(...testPlanProblems(body));
  if (epic) return problems;
  if (!/^## Out of scope\s*$/m.test(body))
    problems.push(
      "no `## Out of scope` section (or it is empty) — it is not optional",
    );
  if (section(body, "Depends on") && !dependsOn(body).length)
    problems.push("`## Depends on` names no issue as `#<number>`");
  const acs = [];
  for (const line of body.split("\n")) {
    const head = line.match(/^[-*] \[[ x]\] \*\*(AC-\d+)/);
    if (head) acs.push({ id: head[1], text: line });
    else if (acs.length && /^\s+\S/.test(line) && !acs.at(-1).closed)
      acs.at(-1).text += `\n${line}`;
    else if (acs.length && line.trim()) acs.at(-1).closed = true;
  }
  if (!acs.length)
    problems.push("no acceptance criterion as a `- [ ] **AC-1** …` checkbox");
  for (const ac of acs) {
    if (!/proof:\s*\S/i.test(ac.text))
      problems.push(`${ac.id} names no \`proof:\``);
  }
  return problems;
}

function preparedBody(file, epic, e2e = false) {
  const body = cleanBody(readFileSync(path.resolve(REPO_ROOT, file), "utf8"));
  const problems = lintBody(body, { epic });
  if (problems.length)
    throw new Error(
      `the draft is not publishable:\n- ${problems.join("\n- ")}`,
    );
  for (const w of lintWarnings(body, { epic, e2e }))
    process.stderr.write(`warning: ${w}\n`);
  return body;
}

const SINGLE_SELECT = "... on ProjectV2ItemFieldSingleSelectValue { name }";
const ITEM_FIELDS = `
  status: fieldValueByName(name: "Status") { ${SINGLE_SELECT} }
  tier: fieldValueByName(name: "Tier") { ${SINGLE_SELECT} }
  kind: fieldValueByName(name: "Kind") { ${SINGLE_SELECT} }
  area: fieldValueByName(name: "Area") { ${SINGLE_SELECT} }
  priority: fieldValueByName(name: "Priority") { ${SINGLE_SELECT} }
  branch: fieldValueByName(name: "Branch") { ... on ProjectV2ItemFieldTextValue { text } }
`;

const ISSUE_QUERY = `query ($owner: String!, $name: String!, $number: Int!) {
  repository(owner: $owner, name: $name) {
    issue(number: $number) {
      number title body state url
      labels(first: 30) { nodes { name } }
      parent {
        number
        subIssues(first: 100) { nodes { number state } }
        projectItems(first: 10) { nodes { id project { number } status: fieldValueByName(name: "Status") { ${SINGLE_SELECT} } } }
      }
      subIssues(first: 100) { nodes { number title state } }
      projectItems(first: 10) { nodes { id project { number } ${ITEM_FIELDS} } }
    }
  }
}`;

const ITEMS_QUERY = `query ($owner: String!, $number: Int!, $cursor: String) {
  user(login: $owner) {
    projectV2(number: $number) {
      items(first: 100, after: $cursor, orderBy: { field: POSITION, direction: ASC }) {
        pageInfo { hasNextPage endCursor }
        nodes {
          id
          ${ITEM_FIELDS}
          content {
            __typename
            ... on Issue { number title body labels(first: 10) { nodes { name } } }
          }
        }
      }
    }
  }
}`;

async function projectItems() {
  const nodes = await listPages(
    async (cursor) =>
      graphql(ITEMS_QUERY, {
        owner: OWNER,
        number: PROJECT_NUMBER,
        cursor: cursor ?? null,
      }).user.projectV2.items,
  );
  return nodes.filter((item) => item.content?.__typename === "Issue");
}

function subIssues(number) {
  const subs = JSON.parse(
    gh(["api", `repos/${REPO}/issues/${number}/sub_issues`]),
  );
  return subs.map((i) => ({
    number: i.number,
    title: i.title,
    state: i.state.toUpperCase(),
  }));
}

function fieldsOf(item, labels) {
  return {
    status: item.status?.name ?? null,
    tier: item.tier?.name ?? null,
    kind: item.kind?.name ?? null,
    area: item.area?.name ?? null,
    priority: item.priority?.name ?? null,
    branch: item.branch?.text ?? null,
    epic: labels.includes("epic"),
    skipTests: labels.includes("skip-tests"),
    e2e: labels.includes("e2e"),
    autobuild: labels.includes("autobuild"),
  };
}

function fetchIssue(number) {
  const { repository } = graphql(ISSUE_QUERY, {
    owner: OWNER,
    name: REPO_NAME,
    number: Number(number),
  });
  const issue = repository.issue;
  if (!issue) throw new Error(`issue #${number} not found in ${REPO}`);
  const result = cardFromIssueNode(issue);
  const item = issue.projectItems.nodes.find(
    (i) => i.project.number === PROJECT_NUMBER,
  );
  if (item) itemIds.set(issue.number, item.id);
  result.lastPart =
    result.parent !== null &&
    issue.parent.subIssues.nodes.every(
      (s) => s.number === issue.number || s.state === "CLOSED",
    );
  const parentItem = issue.parent?.projectItems.nodes.find(
    (i) => i.project.number === PROJECT_NUMBER,
  );
  if (parentItem) itemIds.set(issue.parent.number, parentItem.id);
  return {
    result,
    body: issue.body.replace(/\r\n/g, "\n").trim(),
    parentStatus: parentItem?.status?.name ?? null,
  };
}

function writeCopy(result, body, out, raw) {
  const file = path.resolve(REPO_ROOT, out);
  const meta = [
    `Issue #${result.number}`,
    result.kind && `Kind: ${result.kind}`,
    result.tier && `Tier: ${result.tier}`,
    result.branch && `Branch: \`${result.branch}\``,
    result.skipTests && "skip-tests",
    result.e2e && "e2e",
  ].filter(Boolean);
  mkdirSync(path.dirname(file), { recursive: true });
  writeFileSync(
    file,
    raw ? `${body}\n` : `# ${result.title}\n\n${meta.join(" · ")}\n\n${body}\n`,
  );
  return path.relative(REPO_ROOT, file).replaceAll(path.sep, "/");
}

function init() {
  const existing = JSON.parse(
    gh([
      "project",
      "field-list",
      PROJECT,
      "--owner",
      OWNER,
      "--format",
      "json",
    ]),
  );
  const names = new Set(existing.fields.map((f) => f.name));
  for (const field of FIELDS) {
    if (names.has(field.name)) {
      console.log(`field ${field.name}: exists`);
      continue;
    }
    const argv = [
      "project",
      "field-create",
      PROJECT,
      "--owner",
      OWNER,
      "--name",
      field.name,
      "--data-type",
      field.type,
    ];
    if (field.options)
      argv.push("--single-select-options", field.options.join(","));
    gh(argv);
    console.log(`field ${field.name}: created`);
  }
  for (const label of LABELS) {
    gh([
      "label",
      "create",
      label.name,
      "--repo",
      REPO,
      "--color",
      label.color,
      "--description",
      label.description,
      "--force",
    ]);
    console.log(`label ${label.name}: ok`);
  }
}

async function create(flags) {
  for (const required of ["title", "body-file", "slug", "tier", "kind"]) {
    if (!flags[required]) throw new Error(`--${required} is required`);
  }
  const kind = KINDS[flags.kind];
  if (!kind)
    throw new Error(`--kind must be one of: ${Object.keys(KINDS).join(", ")}`);
  if (!["1", "2"].includes(flags.tier))
    throw new Error("--tier must be 1 or 2");
  if (flags.e2e && flags["skip-tests"])
    throw new Error(
      "--e2e and --skip-tests exclude each other: an e2e part writes tests",
    );
  if (flags.e2e && !flags.parent)
    throw new Error(
      "--e2e needs --parent <epic number>: an e2e part is the last part of an epic",
    );
  if (flags.before && !flags.parent)
    throw new Error("--before needs --parent <epic number>");
  if (flags.queue && flags.parent)
    throw new Error(
      "--queue is for a top-level item: a part goes right after the previous part",
    );
  if (flags.priority && flags.parent)
    throw new Error(
      "--priority is for a top-level item: a part follows its epic's priority",
    );
  if (!flags.parent && !flags.priority)
    throw new Error(
      `--priority is required for a top-level item (one of: ${PRIORITIES.join(", ")})`,
    );
  const priority = flags.priority ? parsePriority(flags.priority) : null;
  if (flags.priority && !priority)
    throw new Error(`--priority must be one of: ${PRIORITIES.join(", ")}`);
  if (flags.area && flags.epic)
    throw new Error("--area is for a spec, not an epic: an epic has no area");
  if (!flags.area && !flags.epic && !flags.e2e)
    throw new Error(
      `--area is required for a spec (one of: ${AREAS.join(", ")})`,
    );
  const area = flags.area ? parseArea(flags.area) : flags.e2e ? "E2E" : null;
  if (flags.area && !area)
    throw new Error(`--area must be one of: ${AREAS.join(", ")}`);
  if (area && !projectFields().fields.has("Area"))
    throw new Error(
      "the project has no Area field — run node scripts/gh-project.mjs init",
    );
  if (priority && !projectFields().fields.has("Priority"))
    throw new Error(
      "the project has no Priority field — run node scripts/gh-project.mjs init",
    );
  const branch = flags.epic
    ? null
    : `${flags.kind === "fix" ? "fix" : "feat"}/${flags.slug}`;
  const body = preparedBody(flags["body-file"], flags.epic, flags.e2e);
  let afterId = null;
  if (!flags.parent) {
    const { board, queue: items } = await projectQueue();
    afterId = placeTarget(items, board, {
      priority,
      spec: flags.queue ?? "bottom",
    });
  }

  const argv = [
    "issue",
    "create",
    "--repo",
    REPO,
    "--title",
    flags.title,
    "--body-file",
    "-",
  ];
  argv.push("--label", flags.epic ? "epic" : "spec");
  if (flags["skip-tests"]) argv.push("--label", "skip-tests");
  if (flags.e2e) argv.push("--label", "e2e");
  if (flags.parent) argv.push("--parent", flags.parent);
  const url = gh(argv, { input: body }).split(/\r?\n/).pop();
  const issueNumber = Number(url.split("/").pop());

  try {
    addToProject(issueNumber);
    setField(issueNumber, "Status", "Todo");
    setField(issueNumber, "Tier", flags.tier);
    setField(issueNumber, "Kind", kind);
    if (area) setField(issueNumber, "Area", area);
    if (priority) setField(issueNumber, "Priority", priority);
    if (branch) setField(issueNumber, "Branch", branch);
    if (flags.before)
      placeBefore(flags.parent, issueNumber, Number(flags.before));
    if (flags.parent) {
      const parts = subIssues(flags.parent).map((s) => s.number);
      const at = parts.indexOf(issueNumber);
      setPosition(issueNumber, itemIdOf(at > 0 ? parts[at - 1] : flags.parent));
    } else setPosition(issueNumber, afterId);
    const { board } = await projectQueue();
    applyRaises(raiseChanges(board, issueNumber).changes);
  } catch (err) {
    if (err instanceof GhLimitError) {
      err.message = `${err.message}\nissue already created: ${url}`;
      throw err;
    }
    throw new Error(`${err.message}\nissue already created: ${url}`);
  }
  console.log(
    JSON.stringify({ number: issueNumber, url, branch }),
  );
}

function placeBefore(parent, number, before) {
  const id = (n) => gh(["api", `repos/${REPO}/issues/${n}`, "--jq", ".id"]);
  gh([
    "api",
    "--method",
    "PATCH",
    `repos/${REPO}/issues/${parent}/sub_issues/priority`,
    "-F",
    `sub_issue_id=${id(number)}`,
    "-F",
    `before_id=${id(before)}`,
  ]);
  const order = subIssues(parent).map((s) => s.number);
  if (
    order.indexOf(number) < 0 ||
    order.indexOf(number) > order.indexOf(before)
  )
    throw new Error(
      `#${number} is not ahead of #${before} in epic #${parent}: ${order.join(", ")}`,
    );
}

async function edit([number, ...rest]) {
  const flags = parseFlags(rest);
  const usage =
    "usage: edit <issue number> [--body-file <path>] [--tier 1|2] [--area <area>] [--parent <epic number>]";
  if (
    !number ||
    !(flags["body-file"] || flags.tier || flags.area || flags.parent)
  )
    throw new Error(usage);
  if (flags.tier && !["1", "2"].includes(flags.tier))
    throw new Error("--tier must be 1 or 2");
  const area = flags.area ? parseArea(flags.area) : null;
  if (flags.area && !area)
    throw new Error(`--area must be one of: ${AREAS.join(", ")}`);
  const done = [];
  let changes = [];
  if (flags["body-file"]) {
    const labels = JSON.parse(
      gh(["issue", "view", number, "--repo", REPO, "--json", "labels"]),
    ).labels.map((l) => l.name);
    const body = preparedBody(
      flags["body-file"],
      labels.includes("epic"),
      labels.includes("e2e"),
    );
    const { board } = await projectQueue();
    changes = raiseChanges(
      board,
      Number(number),
      new Map([[Number(number), body]]),
    ).changes;
    gh(["issue", "edit", number, "--repo", REPO, "--body-file", "-"], {
      input: body,
    });
    done.push("body replaced");
  }
  if (flags.tier) {
    setField(number, "Tier", flags.tier);
    done.push(`Tier = ${flags.tier}`);
  }
  if (area) {
    setField(number, "Area", area);
    done.push(`Area = ${area}`);
  }
  if (flags.parent) {
    const parent = JSON.parse(
      gh(["issue", "view", flags.parent, "--repo", REPO, "--json", "labels"]),
    ).labels.map((l) => l.name);
    if (!parent.includes("epic"))
      throw new Error(
        `#${flags.parent} is not an epic — create one with \`create --epic\` first`,
      );
    gh(["issue", "edit", number, "--repo", REPO, "--parent", flags.parent]);
    done.push(`sub-issue of #${flags.parent}`);
  }
  console.log(`#${number}: ${done.join(", ")}`);
  applyRaises(changes);
}

function sectionEdit(number, heading, file) {
  const content = readFileSync(path.resolve(REPO_ROOT, file), "utf8");
  const { body } = JSON.parse(
    gh(["issue", "view", String(number), "--repo", REPO, "--json", "body"]),
  );
  const next = replaceSection(body, heading, content);
  if (heading === TEST_PLAN) {
    const problems = testPlanProblems(next);
    if (problems.length)
      throw new Error(
        `the new section is not publishable:\n- ${problems.join("\n- ")}`,
      );
  }
  return next;
}

function sectionVerb([number, ...rest]) {
  const flags = parseFlags(rest);
  if (!number || !flags.heading || !flags.file)
    throw new Error(
      "usage: section <issue number> --heading <h> --file <f> [--dry-run]",
    );
  const next = sectionEdit(number, flags.heading, flags.file);
  if (flags["dry-run"]) {
    console.log(next);
    return;
  }
  gh(["issue", "edit", String(number), "--repo", REPO, "--body-file", "-"], {
    input: next,
  });
  console.log(`#${number}: replaced \`## ${flags.heading}\``);
}

function set([number, field, value]) {
  if (!number || !field || value === undefined)
    throw new Error("usage: set <issue number> <field> <value>");
  setField(number, field, value);
  console.log(`#${number} ${field} = ${value}`);
}

function get([number, ...rest]) {
  if (!number)
    throw new Error("usage: get <issue number> [--out <path> [--raw]]");
  const flags = parseFlags(rest);
  const { result, body } = fetchIssue(number);
  if (flags.out) result.file = writeCopy(result, body, flags.out, flags.raw);
  console.log(JSON.stringify(result));
}

function prepare([number, ...rest]) {
  const n = String(number ?? "").replace(/^#/, "");
  if (!/^\d+$/.test(n))
    throw new Error(
      "usage: prepare <issue number> [--tier 1|2] [--skip tests,review] [--fresh] [--dry-run]",
    );
  const flags = parseFlags(rest);
  const { result: issue, body, parentStatus } = fetchIssue(n);
  const refuse = (reason, next) =>
    console.log(
      JSON.stringify({
        ok: false,
        number: issue.number,
        title: issue.title,
        reason,
        next,
      }),
    );

  if (issue.state === "CLOSED" || issue.status === "Done") {
    return refuse(
      "already shipped (closed or Done)",
      "/design for a follow-up spec",
    );
  }
  if (issue.epic) {
    const open = issue.subIssues.filter((s) => s.state === "OPEN");
    const list = issue.subIssues
      .map((s) => `#${s.number} ${s.title} (${s.state.toLowerCase()})`)
      .join("; ");
    return refuse(
      `an epic is never built itself — sub-issues: ${list || "none"}`,
      open.length ? `/build #${open[0].number}` : "/design to add its parts",
    );
  }
  if (issue.parent) {
    const siblings = subIssues(issue.parent);
    const earlier = siblings
      .slice(
        0,
        siblings.findIndex((s) => s.number === issue.number),
      )
      .filter((s) => s.state === "OPEN");
    if (earlier.length) {
      const list = earlier.map((s) => `#${s.number} ${s.title}`).join("; ");
      return refuse(
        `part of epic #${issue.parent}, whose earlier part(s) are still open: ${list} — parts build in order, each from master after the previous one merged`,
        `/build #${earlier[0].number}`,
      );
    }
  }
  const prerequisites = dependsOn(body)
    .map((d) =>
      JSON.parse(
        gh([
          "issue",
          "view",
          String(d),
          "--repo",
          REPO,
          "--json",
          "number,title,state",
        ]),
      ),
    )
    .filter((d) => d.state === "OPEN");
  if (prerequisites.length) {
    const list = prerequisites.map((d) => `#${d.number} ${d.title}`).join("; ");
    return refuse(
      `depends on issue(s) still open: ${list} — build and merge them first, every branch is cut from master`,
      `/build #${prerequisites[0].number}`,
    );
  }
  const missing = [
    !issue.labels.includes("spec") && "the `spec` label",
    !issue.inProject && "a card on the project",
    !issue.branch && "a Branch field",
  ].filter(Boolean);
  if (missing.length)
    return refuse(
      `not a /design or /fix spec: missing ${missing.join(", ")}`,
      "/design to amend or republish it",
    );

  const tier = Number(flags.tier ?? issue.tier);
  if (![1, 2].includes(tier))
    return refuse(
      `tier is ${flags.tier ?? issue.tier ?? "unset"}, not 1 or 2`,
      `node scripts/gh-project.mjs set ${n} Tier <1|2>`,
    );
  const skip = flags.skip
    ? flags.skip
        .split(",")
        .map((s) => s.trim().toLowerCase())
        .filter(Boolean)
    : issue.skipTests
      ? ["tests"]
      : [];
  const bad = skip.filter((s) => !SKIPPABLE.has(s));
  if (bad.length)
    return refuse(
      `cannot skip ${bad.join(", ")} — only tests and review are skippable; verify always runs`,
      `/build #${n}`,
    );

  const dryRun = Boolean(flags["dry-run"]);
  const target = issue.branch;
  const refuseBranch = (reason, extra = {}) =>
    console.log(
      JSON.stringify({
        ok: false,
        stage: "branch",
        number: issue.number,
        title: issue.title,
        reason,
        ...extra,
        next: extra.next ?? `fix the cause, then re-run /build #${n}`,
      }),
    );

  const head = git(["rev-parse", "--abbrev-ref", "HEAD"]);
  if (!head.ok)
    return refuseBranch(`git rev-parse --abbrev-ref HEAD: ${head.err}`);
  const status = git(["status", "--porcelain"]);
  if (!status.ok) return refuseBranch(`git status --porcelain: ${status.err}`);
  const dirty = status.out ? status.out.split(/\r?\n/) : [];
  const branchCommands = [];
  let action = "resumed";
  if (head.out !== target && dirty.length) {
    return refuseBranch(
      "the working tree holds changes that are not this spec",
      {
        files: dirty,
        next: `commit, stash or discard them, then re-run /build #${n}`,
      },
    );
  }
  const masterSync =
    head.out === "master"
      ? ["pull", "--ff-only", "origin", "master"]
      : ["fetch", "origin", "master:master"];
  branchCommands.push("git fetch origin", ["git", ...masterSync].join(" "));
  if (!dryRun) {
    const fetched = git(["fetch", "origin"]);
    if (!fetched.ok) return refuseBranch(`git fetch origin: ${fetched.err}`);
    const synced = git(masterSync);
    if (!synced.ok) {
      return refuseBranch(`git ${masterSync.join(" ")}: ${synced.err}`, {
        next: `reset local master to origin/master (git switch master && git reset --hard origin/master) or fix it by hand, then re-run /build #${n}`,
      });
    }
  }
  const local = git([
    "rev-parse",
    "--verify",
    "--quiet",
    `refs/heads/${target}`,
  ]).ok;
  const exists =
    local ||
    git(["rev-parse", "--verify", "--quiet", `refs/remotes/origin/${target}`])
      .ok;
  let recut = false;
  if (issue.e2e && exists && !dirty.length) {
    const ref = local ? target : `origin/${target}`;
    const ahead = git(["rev-list", "--count", `origin/master..${ref}`]);
    const behind = git(["rev-list", "--count", `${ref}..origin/master`]);
    recut =
      ahead.ok && behind.ok && ahead.out === "0" && Number(behind.out) > 0;
  }
  if (head.out !== target || recut) {
    action = recut ? "recut" : exists ? "switched" : "created";
    const cmd = exists ? "git" : "gh";
    const argv = recut
      ? ["switch", "-C", target, "origin/master"]
      : exists
        ? ["switch", target]
        : [
            "issue",
            "develop",
            n,
            "--repo",
            REPO,
            "--name",
            target,
            "--base",
            "master",
            "--checkout",
          ];
    branchCommands.push([cmd, ...argv].join(" "));
    if (!dryRun) {
      const res =
        cmd === "gh"
          ? spawnGh(argv)
          : spawnSync(cmd, argv, {
              cwd: REPO_ROOT,
              encoding: "utf8",
              windowsHide: true,
              shell: false,
            });
      if (res.error || res.status !== 0) {
        const limit = cmd === "gh" && !res.error ? limitFromGhFailure(res.stderr) : null;
        if (limit) throw limit;
        return refuseBranch(
          `${cmd} ${argv.join(" ")}: ${res.error?.message ?? res.stderr.trim().split(/\r?\n/)[0]}`,
        );
      }
    }
  }

  const specPath = `${ISSUES_DIR}/${n}.md`;
  const spec = dryRun ? specPath : writeCopy(issue, body, specPath, false);
  const manualPath = `${ISSUES_DIR}/${n}.manual.md`;
  if (!dryRun) rmSync(path.resolve(REPO_ROOT, manualPath), { force: true });
  const epic = issue.parent
    ? { number: issue.parent, last: issue.lastPart }
    : null;
  const extraCopies = [];
  if (epic?.last) {
    const siblings = subIssues(epic.number).filter(
      (s) => s.number !== issue.number,
    );
    extraCopies.push(epic.number, ...siblings.map((s) => s.number));
    if (!dryRun) {
      rmSync(
        path.resolve(REPO_ROOT, `${ISSUES_DIR}/${epic.number}.manual.md`),
        { force: true },
      );
      for (const m of extraCopies) {
        const copy = fetchIssue(m);
        writeCopy(copy.result, copy.body, `${ISSUES_DIR}/${m}.md`, false);
      }
    }
  }
  const resumed = issue.status === "In progress";
  if (!resumed && !dryRun) setField(issue.number, "Status", "In progress");
  if (issue.parent && parentStatus === "Todo" && !dryRun)
    setField(issue.parent, "Status", "In progress");
  if (!dryRun) saveUsageStart();
  const ownPlan = issue.e2e && /^\s*\d+\.\s/m.test(section(body, TEST_PLAN));
  const rebuild = prepareRebuild({
    n,
    issue,
    freshBranch: dirty.length === 0,
    dryRun,
  });
  const workflowArgs = {
    spec,
    issue: issue.number,
    branch: target,
    title: issue.title,
    tier,
    maxRounds: 2,
    skip,
    epic,
    e2e: issue.e2e,
    ...(ownPlan ? { ownPlan: true } : {}),
    ...(rebuild.diagnosis
      ? { diagnosis: rebuild.diagnosis, patchApplied: rebuild.patchApplied }
      : {}),
  };
  const replay = planReplay({
    n,
    body,
    workflowArgs,
    attempt: !flags.fresh && action === "resumed" && dirty.length > 0,
    dryRun,
  });
  const areaNote = issue.area
    ? []
    : [
        `Brak obszaru (Area) na #${n} — ustaw: node scripts/gh-project.mjs set ${n} Area <obszar>`,
      ];
  for (const note of areaNote) process.stderr.write(`${note}\n`);
  const out = {
    ok: true,
    resumed,
    url: issue.url,
    branch: { name: target, action },
    notes: [
      ...(action === "resumed" ? dirty : []),
      ...rebuild.notes,
      ...areaNote,
    ],
    workflow: replay.workflow,
    replay: replay.summary,
    workflowArgs,
  };
  if (replay.skipped) out.replaySkipped = replay.skipped;
  if (dryRun)
    Object.assign(out, {
      dryRun: true,
      branchCommands,
      cardMove: resumed ? null : "Status -> In progress",
      epicCopies: extraCopies.map((m) => `${ISSUES_DIR}/${m}.md`),
    });
  console.log(JSON.stringify(out));
}

// An e2e part whose newest comment is a /fix e2e test-defect diagnosis gets it as a file, and the previous run's tests back from <n>.e2e.patch
function prepareRebuild({ n, issue, freshBranch, dryRun }) {
  const none = { diagnosis: null, patchApplied: false, notes: [] };
  if (!issue.e2e) return none;
  const { comments } = JSON.parse(
    gh(["issue", "view", String(n), "--repo", REPO, "--json", "comments"]),
  );
  const patchRel = `${ISSUES_DIR}/${n}.e2e.patch`;
  const patchFile = path.resolve(REPO_ROOT, patchRel);
  const { diagnosis, applyPatch } = rebuildInputs({
    e2e: issue.e2e,
    comments,
    freshBranch,
    patchExists: existsSync(patchFile),
  });
  if (!diagnosis) return none;
  const diagnosisRel = `${ISSUES_DIR}/${n}.diagnosis.md`;
  if (!dryRun) {
    mkdirSync(path.dirname(path.resolve(REPO_ROOT, diagnosisRel)), {
      recursive: true,
    });
    writeFileSync(path.resolve(REPO_ROOT, diagnosisRel), `${diagnosis}\n`);
  }
  if (!applyPatch)
    return {
      diagnosis: diagnosisRel,
      patchApplied: false,
      notes: [`no ${patchRel}: the diagnosed tests are rewritten from master`],
    };
  if (dryRun)
    return {
      diagnosis: diagnosisRel,
      patchApplied: false,
      notes: [
        `would run: git apply --check ${patchRel}, then git apply ${patchRel}`,
      ],
    };
  const check = git(["apply", "--check", patchRel]);
  if (!check.ok)
    return {
      diagnosis: diagnosisRel,
      patchApplied: false,
      notes: [
        `${patchRel} does not apply (${check.err}): the diagnosed tests are rewritten from master`,
      ],
    };
  const applied = git(["apply", patchRel]);
  if (!applied.ok)
    return {
      diagnosis: diagnosisRel,
      patchApplied: false,
      notes: [`git apply ${patchRel} failed (${applied.err})`],
    };
  return {
    diagnosis: diagnosisRel,
    patchApplied: true,
    notes: [`applied ${patchRel}: the previous run's tests are on disk`],
  };
}

// Re-running an interrupted build replays the finished agent steps of its last run from the workflow journal (zero tokens).
function planReplay({ n, body, workflowArgs, attempt, dryRun }) {
  const runFile = path.resolve(REPO_ROOT, `${ISSUES_DIR}/${n}.run.json`);
  const resumeRel = `${ISSUES_DIR}/${n}.resume.js`;
  const resumeFile = path.resolve(REPO_ROOT, resumeRel);
  const plain = {
    workflow: { name: "build-feature" },
    summary: null,
    skipped: null,
  };
  const bodyHash = createHash("sha256").update(body).digest("hex");
  let previous = null;
  try {
    previous = JSON.parse(readFileSync(runFile, "utf8"));
  } catch {
    previous = null;
  }
  let found = null;
  let skipped = null;
  if (attempt) {
    if (previous && previous.bodyHash !== bodyHash)
      skipped = "spec changed since the last run";
    else {
      const { tier, skip, e2e } = workflowArgs;
      found = findReplay({
        issue: Number(n),
        args: { tier, skip, e2e },
        previous,
      });
      if (!found.ok) {
        skipped = found.reason;
        found = null;
      }
    }
  }
  let source = null;
  try {
    source = found
      ? renderResumeScript(
          readFileSync(
            path.resolve(REPO_ROOT, ".claude/workflows/build-feature.js"),
            "utf8",
          ),
          found,
        )
      : null;
  } catch {
    source = null;
  }
  if (found && !source) {
    skipped = "build-feature.js has no replay marker";
    found = null;
  }
  if (!dryRun) {
    const { tier, skip, e2e } = workflowArgs;
    mkdirSync(path.dirname(runFile), { recursive: true });
    writeFileSync(
      runFile,
      `${JSON.stringify({ bodyHash, tier, skip, e2e, replayed: found ? found.steps : [], writtenAt: Date.now() })}\n`,
    );
    if (source) writeFileSync(resumeFile, source);
    else rmSync(resumeFile, { force: true });
  }
  if (!found) return { ...plain, skipped };
  return {
    workflow: { scriptPath: resumeRel },
    summary: {
      steps: found.steps.map((s) => s.label),
      liveFrom: found.liveFrom,
      runId: found.runId,
    },
    skipped: null,
  };
}

// The usage line is informative: a failing rate_limit read must never fail a build or a report.
function saveUsageStart() {
  try {
    writeFileSync(USAGE_START_FILE, JSON.stringify(rateLimit()));
  } catch {
    rmSync(USAGE_START_FILE, { force: true });
  }
}

function usageReportLine() {
  try {
    let start = null;
    try {
      start = JSON.parse(readFileSync(USAGE_START_FILE, "utf8"));
    } catch {
      start = null;
    }
    return reportUsage(start, rateLimit());
  } catch {
    return null;
  }
}

function report([number, ...rest]) {
  if (!number)
    throw new Error(
      "usage: report <issue number> [--json '<report>']   (else JSON on stdin)",
    );
  const flags = parseFlags(rest);
  const run = JSON.parse(flags.json ?? readStdin());
  const where = (f) => [f.file, f.line].filter(Boolean).join(":") || "—";
  const lines = [];
  const afterPost = () => {
    if (run.status === "shipped" && (run.reviewRan || run.resumedShip))
      tick([number]);
  };

  const marker = reportMarker(number, run);
  const { comments } = JSON.parse(
    gh(["issue", "view", String(number), "--repo", REPO, "--json", "comments"]),
  );
  if (hasRunMarker(comments, marker)) {
    console.log(`#${number}: report already posted`);
    afterPost();
    return;
  }

  if (run.status === "shipped") {
    const [pr] = JSON.parse(
      gh([
        "pr",
        "list",
        "--repo",
        REPO,
        "--head",
        run.branch,
        "--state",
        "open",
        "--json",
        "url",
        "--limit",
        "1",
      ]),
    );
    lines.push(
      `**Built** on \`${run.branch}\` — ${pr ? `PR ${pr.url} is open, awaiting your merge.` : "pushed, but no open PR was found for it."}`,
      "",
    );
    if (run.resumedShip) {
      lines.push(
        "- Shipped by `scripts/autobuild.mjs` after the earlier ship step failed — tests and review are in the blocked report above.",
      );
    } else {
      if (run.e2e) {
        const flaky = run.e2e.flaky ?? [];
        lines.push(
          `- **e2e:** ${run.e2e.passed ?? 0}/${run.e2e.total ?? 0} green${flaky.length ? `, flaky: ${flaky.map((t) => `\`${t}\``).join(", ")}` : ""}`,
        );
      } else
        lines.push(
          `- Tests: ${run.tests?.length ? run.tests.map((t) => `\`${t}\``).join(", ") : "none — skip-tests"}`,
        );
      lines.push(`- Fix rounds: ${run.rounds ?? 0}`);
      lines.push(
        `- Review: ${run.reviewRan ? `clean, ${run.minor?.length ?? 0} minor finding(s)` : "skipped"}`,
      );
      for (const f of run.minor ?? [])
        lines.push(`  - \`${where(f)}\` — ${f.claim}`);
    }
  } else {
    const e2eBlock = run.stage === "e2e";
    lines.push(
      `**Blocked** at \`${run.stage ?? "?"}\`${e2eBlock && run.class ? ` (${run.class})` : ""}${run.reason ? ` — ${run.reason}` : ""}`,
      "",
    );
    for (const f of run.failures ?? [])
      lines.push(
        `- ${f.step}: ${f.summary}${f.file ? ` (\`${f.file}\`)` : ""}`,
      );
    for (const f of run.blocking ?? [])
      lines.push(`- \`${where(f)}\` — ${f.claim}`);
    if (e2eBlock) lines.push(...e2eBlockLines(run));
    lines.push(
      "",
      run.stage === "ship"
        ? "Verified and reviewed, but not shipped: finish the commit, push and PR by hand — a re-run of `/build` would repeat the whole pipeline."
        : e2eBlock
          ? run.class === "environment"
            ? `An environment problem, not an app defect: fix the machine, then re-run \`/build #${number}\` (or \`node scripts/autobuild.mjs <epic>\`) — the e2e tests are written again from master.`
            : `A stable app failure: fix the app with \`/fix\` and merge it, then re-run \`/build #${number}\` (or \`node scripts/autobuild.mjs <epic>\`) — the branch is recut from master and the e2e tests are written again; the patch is for reference only.`
          : `Fix the spec or the named problem, then re-run \`/build #${number}\` — the work stays on its branch.`,
    );
  }

  if (run.deviations?.length) {
    lines.push(
      "",
      "**Deviations** from the spec or the tests, made by the implementer:",
    );
    for (const d of run.deviations)
      lines.push(
        `- ${d.kind}${d.file ? ` \`${d.file}\`` : ""} — ${d.what} (why: ${d.why})`,
      );
  }

  if (run.status === "shipped" && run.manualTest?.length) {
    lines.push("");
    for (const m of run.manualTest) {
      try {
        gh(
          [
            "issue",
            "edit",
            String(m.issue),
            "--repo",
            REPO,
            "--body-file",
            "-",
          ],
          { input: sectionEdit(m.issue, TEST_PLAN, m.file) },
        );
        lines.push(`Test manualny zaktualizowany w #${m.issue}`);
      } catch (err) {
        if (err instanceof GhLimitError) throw err;
        lines.push(
          `Test manualny NIE zaktualizowany w #${m.issue}: ${err.message.split("\n")[0]}`,
        );
      }
    }
  }

  const usage = usageReportLine();
  if (usage) lines.push("", usage);
  lines.push("", marker);

  gh(["issue", "comment", String(number), "--repo", REPO, "--body-file", "-"], {
    input: lines.join("\n"),
  });
  console.log(`#${number}: report posted`);
  afterPost();
}

function reportMarker(number, run) {
  const git = (...argv) =>
    (
      spawnSync("git", argv, {
        cwd: REPO_ROOT,
        encoding: "utf8",
        windowsHide: true,
        shell: false,
      }).stdout ?? ""
    ).trim();
  const digest = createHash("sha256")
    .update(JSON.stringify(run))
    .digest("hex")
    .slice(0, 8);
  return runMarker({
    issue: number,
    branch: run.branch ?? git("rev-parse", "--abbrev-ref", "HEAD"),
    sha: `${git("rev-parse", "HEAD").slice(0, 12)}-${digest}`,
  });
}

async function listCards() {
  const cards = (await projectItems()).map((item) => {
    const entry = {
      id: item.id,
      number: item.content.number,
      title: item.content.title,
      ...fieldsOf(
        item,
        item.content.labels.nodes.map((l) => l.name),
      ),
    };
    bodies.set(entry.number, item.content.body ?? "");
    entry.dependsOn = dependsOn(item.content.body ?? "");
    if (entry.epic)
      entry.subIssues = subIssues(entry.number).map((s) => s.number);
    return entry;
  });
  for (const c of cards) itemIds.set(c.number, c.id);
  return cards;
}

function dependencyMap(board, queue, overrides = new Map()) {
  const statusOf = new Map(board.map((c) => [c.number, c.status]));
  const openDeps = (n) => {
    const body = overrides.has(n) ? overrides.get(n) : (bodies.get(n) ?? "");
    return dependsOn(body).filter((d) => d !== n && statusOf.get(d) !== "Done");
  };
  return new Map(
    queue.map((q) => [
      q.number,
      [q.number, ...(q.epic ? q.subIssues ?? [] : [])].flatMap(openDeps),
    ]),
  );
}

function raiseChanges(board, root, overrides = new Map()) {
  const queue = queueOf(board);
  const item = queue.find(
    (q) => q.number === root || (q.epic && (q.subIssues ?? []).includes(root)),
  );
  const deps = dependencyMap(board, queue, overrides);
  if (!item) return { queue, deps, item: null, changes: [] };
  const changes = raisePlan(queue, deps, {
    number: item.number,
    priority: item.priority ?? DEFAULT_PRIORITY,
  });
  return { queue, deps, item, changes };
}

function raiseLine(change, dependent) {
  return change.from === change.to
    ? `przesunięto #${change.number} przed ${dependent} (zależność)`
    : `podniesiono #${change.number} ${shortOf(change.from)} → ${shortOf(change.to)} (zależność ${dependent})`;
}

function applyRaises(changes) {
  for (const c of changes) {
    if (c.from !== c.to) setField(c.number, "Priority", c.to);
    setPosition(c.number, c.afterId);
    console.log(raiseLine(c, `#${c.for}`));
  }
}

function placed(board, number, afterId, patch = {}) {
  const moved = { ...board.find((c) => c.number === number), ...patch };
  const others = board.filter((c) => c.number !== number);
  const at = afterId === null ? 0 : others.findIndex((c) => c.id === afterId) + 1;
  return [...others.slice(0, at), moved, ...others.slice(at)];
}

function assertNoBlocker(board, number, deps) {
  const queue = queueOf(board);
  const blocked = blockingDependent(queue, deps, number);
  if (blocked)
    throw new Error(
      `#${number} blokuje #${blocked.number} (${shortOf(blocked.priority)}) — najpierw obniż #${blocked.number}`,
    );
}

async function projectQueue() {
  const board = await listCards();
  return { board, queue: queueOf(board) };
}

async function list() {
  console.log(JSON.stringify(await listCards()));
}

async function queue() {
  console.log(JSON.stringify((await projectQueue()).queue));
}

async function move([number, target]) {
  const n = Number(String(number ?? "").replace(/^#/, ""));
  if (!n || !target)
    throw new Error("usage: move <n> top|bottom|before:N|after:N");
  const { board, queue: items } = await projectQueue();
  const card = items.find((q) => q.number === n);
  if (!card) throw new Error(`#${n} is not in the queue`);
  const afterId =
    card.status === "Todo"
      ? placeTarget(items, board, {
          priority: card.priority ?? DEFAULT_PRIORITY,
          spec: target,
          self: n,
        })
      : positionTarget(
          items.filter((q) => q.number !== n),
          target,
          board,
        );
  const after = placed(board, n, afterId);
  const { deps, changes } = raiseChanges(after, n);
  if (card.status === "Todo") assertNoBlocker(after, n, deps);
  setPosition(n, afterId);
  console.log(`#${n}: moved ${target}`);
  applyRaises(changes);
}

function partsOf(board) {
  const parents = new Map();
  for (const c of board)
    if (c.epic) for (const s of c.subIssues ?? []) parents.set(s, c.number);
  return parents;
}

function backfillPriority(board, apply) {
  const parts = partsOf(board);
  const todo = board.filter(
    (c) => c.status !== "Done" && !c.priority && !parts.has(c.number),
  );
  for (const c of todo) console.log(`#${c.number} → ${DEFAULT_PRIORITY}`);
  console.log(
    `${todo.length} to set, ${apply ? "applying" : "dry run (use --apply to write)"}`,
  );
  if (!apply || !todo.length) return;

  const meta = projectFields();
  const field = meta.fields.get("Priority");
  const option = field?.options?.find((o) => o.name === DEFAULT_PRIORITY);
  if (!option)
    throw new Error(
      "the project has no Priority field — run node scripts/gh-project.mjs init",
    );
  const BATCH = 20;
  for (let at = 0; at < todo.length; at += BATCH) {
    const mutations = todo
      .slice(at, at + BATCH)
      .map(
        (c, k) =>
          `m${k}: updateProjectV2ItemFieldValue(input: {projectId: ${JSON.stringify(meta.id)}, itemId: ${JSON.stringify(c.id)}, fieldId: ${JSON.stringify(field.id)}, value: {singleSelectOptionId: ${JSON.stringify(option.id)}}}) { projectV2Item { id } }`,
      );
    graphql(`mutation { ${mutations.join(" ")} }`);
  }
}

async function priorityVerb(rest) {
  const positional = [];
  while (rest.length && !rest[0].startsWith("--")) positional.push(rest.shift());
  const flags = parseFlags(rest);
  const { board, queue: items } = await projectQueue();

  if (flags.backfill) return backfillPriority(board, Boolean(flags.apply));

  if (flags.where !== undefined) {
    const priority = parsePriority(flags.where);
    if (!priority)
      throw new Error(`priority must be one of: ${PRIORITIES.join(", ")}`);
    const afterId = placeTarget(items, board, {
      priority,
      spec: flags.queue ?? "bottom",
    });
    const preview = { number: 0, id: "NEW", status: "Todo", priority, parent: null, epic: false };
    const at = afterId === null ? 0 : board.findIndex((c) => c.id === afterId) + 1;
    const hypothetical = [...board.slice(0, at), preview, ...board.slice(at)];
    console.log(describeSpot(queueOf(hypothetical), 0));
    if (flags["body-file"]) {
      const body = cleanBody(readFileSync(path.resolve(REPO_ROOT, flags["body-file"]), "utf8"));
      const { changes } = raiseChanges(hypothetical, 0, new Map([[0, body]]));
      for (const c of changes) console.log(raiseLine(c, "#nowy"));
    }
    return;
  }

  const n = Number(String(positional[0] ?? "").replace(/^#/, ""));
  if (!n || !positional[1])
    throw new Error(
      "usage: priority <n> <P0|P1|P2|P3> | priority --where <P> [--queue <spec>] | priority --backfill [--apply]",
    );
  const priority = parsePriority(positional[1]);
  if (!priority)
    throw new Error(`priority must be one of: ${PRIORITIES.join(", ")}`);
  const epic = partsOf(board).get(n);
  if (epic)
    throw new Error(
      `#${n} is a part of epic #${epic} — set the epic's priority`,
    );
  const card = items.find((q) => q.number === n);
  if (!card) throw new Error(`#${n} is not in the queue`);
  if (!projectFields().fields.has("Priority"))
    throw new Error(
      "the project has no Priority field — run node scripts/gh-project.mjs init",
    );

  const was = card.priority ?? DEFAULT_PRIORITY;
  const head = `#${n} ${shortOf(was)} → ${shortOf(priority)}`;
  const replace = card.status === "Todo" && !(was === priority && inBand(board, card));
  const afterId = replace ? placeTarget(items, board, { priority, self: n }) : undefined;
  const after = replace
    ? placed(board, n, afterId, { priority })
    : board.map((c) => (c.number === n ? { ...c, priority } : c));
  const { deps, changes } = raiseChanges(after, n);
  if (replace) assertNoBlocker(after, n, deps);

  setField(n, "Priority", priority);
  if (card.status !== "Todo") console.log(`${head}, ${card.status}: only the field is set`);
  else if (!replace) console.log(`${head}, unchanged`);
  else {
    setPosition(n, afterId);
    const positionOf = (cards) => queueOf(cards).findIndex((q) => q.number === n) + 1;
    console.log(`${head}, ${positionOf(board)}. → ${positionOf(after)}. w kolejce`);
  }
  applyRaises(changes);
}

function comment([number, ...rest]) {
  const flags = parseFlags(rest);
  if (!number || !flags["body-file"])
    throw new Error("usage: comment <issue number> --body-file <path>");
  console.log(
    gh([
      "issue",
      "comment",
      number,
      "--repo",
      REPO,
      "--body-file",
      flags["body-file"],
    ]),
  );
}

function tick([number]) {
  if (!number) throw new Error("usage: tick <issue number>");
  const { body } = JSON.parse(
    gh(["issue", "view", String(number), "--repo", REPO, "--json", "body"]),
  );
  let count = 0;
  const ticked = body.replace(
    /^(\s*[-*] )\[ \]( \*\*AC-)/gm,
    (_, bullet, rest) => {
      count++;
      return `${bullet}[x]${rest}`;
    },
  );
  if (count)
    gh(["issue", "edit", String(number), "--repo", REPO, "--body-file", "-"], {
      input: ticked,
    });
  console.log(`#${number}: ticked ${count} acceptance criteria`);
}

function check(rest) {
  const flags = parseFlags(rest);
  if (!flags["body-file"])
    throw new Error("usage: check --body-file <path> [--epic] [--e2e]");
  const body = preparedBody(flags["body-file"], flags.epic, flags.e2e);
  console.log(`publishable — ${body.split("\n").length} lines after cleaning`);
}

const VERBS = {
  init,
  check,
  create: (rest) => create(parseFlags(rest)),
  edit,
  section: sectionVerb,
  set,
  get,
  prepare,
  report,
  list,
  queue,
  move,
  priority: priorityVerb,
  comment,
  tick,
};

const [command, ...rest] = process.argv.slice(2);
try {
  const verb = VERBS[command];
  if (!verb)
    throw new Error(`usage: gh-project.mjs ${Object.keys(VERBS).join(" | ")}`);
  await verb(rest);
} catch (err) {
  if (err instanceof GhLimitError) exitOnGhLimit(err);
  console.error(err.message);
  process.exit(1);
}
