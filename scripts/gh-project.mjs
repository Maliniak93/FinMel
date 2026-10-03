#!/usr/bin/env node
// Usage: node scripts/gh-project.mjs <verb> ...
//   init                                                   create the custom fields and labels when missing
//   create --title <t> --body-file <p> --slug <s> --tier 1|2 --kind new|change|cleanup|fix [--skip-tests] [--parent <n>] [--epic]
//   check --body-file <p> [--epic]                         dry run of create's cleaning and checking
//   edit <n> [--body-file <p>] [--tier 1|2] [--parent <n>] replace the body, or attach the issue to an epic
//   set <n> <field> <value>                                set one project field
//   get <n> [--out <path> [--raw]]                         print the issue as JSON, optionally write a local copy
//   prepare <n> [--tier 1|2] [--skip tests,review]         /build's pre-workflow step
//   report <n> [--json '<run report>']                     post a run report (JSON on stdin otherwise)
//   list                                                   every issue on the project as JSON
//   comment <n> --body-file <path>                         post a comment
//   tick <n>                                               tick every acceptance-criterion checkbox

import { spawnSync } from "node:child_process";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const OWNER = "Maliniak93";
const REPO = `${OWNER}/FinMel`;
const PROJECT = "1";
const ISSUES_DIR = "skarbiec-plan/issues";

const FIELDS = [
  { name: "Tier", type: "SINGLE_SELECT", options: ["1", "2"] },
  { name: "Kind", type: "SINGLE_SELECT", options: ["New", "Change", "Cleanup", "Fix"] },
  { name: "Branch", type: "TEXT" },
];
const LABELS = [
  { name: "spec", color: "5319e7", description: "Build-ready spec written by /design" },
  { name: "epic", color: "3e4b9e", description: "Umbrella of a split spec; its sub-issues are built one by one" },
  { name: "skip-tests", color: "c5def5", description: "Spec adds or alters no behaviour; /build skips the test phase" },
];
const KINDS = { new: "New", change: "Change", cleanup: "Cleanup", fix: "Fix" };
const BOOLEAN_FLAGS = new Set(["skip-tests", "epic", "raw"]);
const SKIPPABLE = new Set(["tests", "review"]);

function gh(argv, input) {
  const res = spawnSync("gh", argv, { cwd: REPO_ROOT, encoding: "utf8", windowsHide: true, shell: false, input });
  if (res.error) throw new Error(`gh ${argv[0]} ${argv[1] ?? ""}: ${res.error.message}`);
  if (res.status !== 0) throw new Error(`gh ${argv.slice(0, 2).join(" ")}: ${res.stderr.trim()}`);
  return res.stdout.trim();
}

function issueUrl(number) {
  return `https://github.com/${REPO}/issues/${number}`;
}

function setField(url, field, value) {
  gh(["project", "item-edit", PROJECT, "--owner", OWNER, "--url", url, "--field", field, "--value", value]);
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
  const meaningful = lines.filter((l) => l.trim() && !/^#{2,6} /.test(l) && !isTableRule(l));
  const bareTableHeader = meaningful.length === 1 && meaningful[0].trim().startsWith("|");
  return meaningful.length > 0 && !bareTableHeader;
}

function dropEmpty(lines, level) {
  const marker = new RegExp(`^#{${level}} `);
  const out = [];
  let section = null;
  const flush = () => {
    if (!section) return;
    const body = level < 3 ? dropEmpty(section.slice(1), level + 1) : section.slice(1);
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

function dependsOn(body) {
  return [...new Set([...section(body, "Depends on").matchAll(/#(\d+)/g)].map((m) => Number(m[1])))];
}

function lintWarnings(body, { epic }) {
  if (epic) return [];
  return section(body, "Code map") ? [] : ["no `## Code map` section — the test-writer, implementer and reviewer will each rediscover the code"];
}

function lintBody(body, { epic }) {
  const problems = [];
  if (!/^## Goal\s*$/m.test(body)) problems.push("no `## Goal` section (or it is empty)");
  if (epic) return problems;
  if (!/^## Out of scope\s*$/m.test(body)) problems.push("no `## Out of scope` section (or it is empty) — it is not optional");
  if (section(body, "Depends on") && !dependsOn(body).length) problems.push("`## Depends on` names no issue as `#<number>`");
  const acs = [];
  for (const line of body.split("\n")) {
    const head = line.match(/^[-*] \[[ x]\] \*\*(AC-\d+)/);
    if (head) acs.push({ id: head[1], text: line });
    else if (acs.length && /^\s+\S/.test(line) && !acs.at(-1).closed) acs.at(-1).text += `\n${line}`;
    else if (acs.length && line.trim()) acs.at(-1).closed = true;
  }
  if (!acs.length) problems.push("no acceptance criterion as a `- [ ] **AC-1** …` checkbox");
  for (const ac of acs) {
    if (!/proof:\s*\S/i.test(ac.text)) problems.push(`${ac.id} names no \`proof:\``);
  }
  return problems;
}

function preparedBody(file, epic) {
  const body = cleanBody(readFileSync(path.resolve(REPO_ROOT, file), "utf8"));
  const problems = lintBody(body, { epic });
  if (problems.length) throw new Error(`the draft is not publishable:\n- ${problems.join("\n- ")}`);
  for (const w of lintWarnings(body, { epic })) process.stderr.write(`warning: ${w}\n`);
  return body;
}

function projectItems() {
  const { items } = JSON.parse(
    gh(["project", "item-list", PROJECT, "--owner", OWNER, "--format", "json", "--limit", "500"]),
  );
  return items.filter((item) => item.content?.type === "Issue");
}

function subIssues(number) {
  const subs = JSON.parse(gh(["api", `repos/${REPO}/issues/${number}/sub_issues`]));
  return subs.map((i) => ({ number: i.number, title: i.title, state: i.state.toUpperCase() }));
}

function fieldsOf(item, labels) {
  return {
    status: item?.status ?? null,
    tier: item?.tier ?? null,
    kind: item?.kind ?? null,
    branch: item?.branch ?? null,
    epic: labels.includes("epic"),
    skipTests: labels.includes("skip-tests"),
  };
}

function fetchIssue(number) {
  const issue = JSON.parse(
    gh(["issue", "view", String(number), "--repo", REPO, "--json", "number,title,body,state,labels,url,parent"]),
  );
  const labels = issue.labels.map((l) => l.name);
  const item = projectItems().find((i) => i.content.number === issue.number);
  const result = {
    number: issue.number,
    title: issue.title,
    url: issue.url,
    state: issue.state,
    labels,
    inProject: Boolean(item),
    ...fieldsOf(item, labels),
    parent: issue.parent?.number ?? null,
  };
  result.subIssues = result.epic ? subIssues(issue.number) : [];
  return { result, body: issue.body.replace(/\r\n/g, "\n").trim() };
}

function writeCopy(result, body, out, raw) {
  const file = path.resolve(REPO_ROOT, out);
  const meta = [
    `Issue #${result.number}`,
    result.kind && `Kind: ${result.kind}`,
    result.tier && `Tier: ${result.tier}`,
    result.branch && `Branch: \`${result.branch}\``,
    result.skipTests && "skip-tests",
  ].filter(Boolean);
  mkdirSync(path.dirname(file), { recursive: true });
  writeFileSync(file, raw ? `${body}\n` : `# ${result.title}\n\n${meta.join(" · ")}\n\n${body}\n`);
  return path.relative(REPO_ROOT, file).replaceAll(path.sep, "/");
}

function init() {
  const existing = JSON.parse(gh(["project", "field-list", PROJECT, "--owner", OWNER, "--format", "json"]));
  const names = new Set(existing.fields.map((f) => f.name));
  for (const field of FIELDS) {
    if (names.has(field.name)) {
      console.log(`field ${field.name}: exists`);
      continue;
    }
    const argv = ["project", "field-create", PROJECT, "--owner", OWNER, "--name", field.name, "--data-type", field.type];
    if (field.options) argv.push("--single-select-options", field.options.join(","));
    gh(argv);
    console.log(`field ${field.name}: created`);
  }
  for (const label of LABELS) {
    gh(["label", "create", label.name, "--repo", REPO, "--color", label.color, "--description", label.description, "--force"]);
    console.log(`label ${label.name}: ok`);
  }
}

function create(flags) {
  for (const required of ["title", "body-file", "slug", "tier", "kind"]) {
    if (!flags[required]) throw new Error(`--${required} is required`);
  }
  const kind = KINDS[flags.kind];
  if (!kind) throw new Error(`--kind must be one of: ${Object.keys(KINDS).join(", ")}`);
  if (!["1", "2"].includes(flags.tier)) throw new Error("--tier must be 1 or 2");
  const branch = flags.epic ? null : `${flags.kind === "fix" ? "fix" : "feat"}/${flags.slug}`;
  const body = preparedBody(flags["body-file"], flags.epic);

  const argv = ["issue", "create", "--repo", REPO, "--title", flags.title, "--body-file", "-"];
  argv.push("--label", flags.epic ? "epic" : "spec");
  if (flags["skip-tests"]) argv.push("--label", "skip-tests");
  if (flags.parent) argv.push("--parent", flags.parent);
  const url = gh(argv, body).split(/\r?\n/).pop();

  try {
    gh(["project", "item-add", PROJECT, "--owner", OWNER, "--url", url]);
    setField(url, "Status", "Todo");
    setField(url, "Tier", flags.tier);
    setField(url, "Kind", kind);
    if (branch) setField(url, "Branch", branch);
  } catch (err) {
    throw new Error(`${err.message}\nissue already created: ${url}`);
  }
  console.log(JSON.stringify({ number: Number(url.split("/").pop()), url, branch }));
}

function edit([number, ...rest]) {
  const flags = parseFlags(rest);
  const usage = "usage: edit <issue number> [--body-file <path>] [--tier 1|2] [--parent <epic number>]";
  if (!number || !(flags["body-file"] || flags.tier || flags.parent)) throw new Error(usage);
  if (flags.tier && !["1", "2"].includes(flags.tier)) throw new Error("--tier must be 1 or 2");
  const done = [];
  if (flags["body-file"]) {
    const labels = JSON.parse(gh(["issue", "view", number, "--repo", REPO, "--json", "labels"])).labels.map((l) => l.name);
    const body = preparedBody(flags["body-file"], labels.includes("epic"));
    gh(["issue", "edit", number, "--repo", REPO, "--body-file", "-"], body);
    done.push("body replaced");
  }
  if (flags.tier) {
    setField(issueUrl(number), "Tier", flags.tier);
    done.push(`Tier = ${flags.tier}`);
  }
  if (flags.parent) {
    const parent = JSON.parse(gh(["issue", "view", flags.parent, "--repo", REPO, "--json", "labels"])).labels.map((l) => l.name);
    if (!parent.includes("epic")) throw new Error(`#${flags.parent} is not an epic — create one with \`create --epic\` first`);
    gh(["issue", "edit", number, "--repo", REPO, "--parent", flags.parent]);
    done.push(`sub-issue of #${flags.parent}`);
  }
  console.log(`#${number}: ${done.join(", ")}`);
}

function set([number, field, value]) {
  if (!number || !field || value === undefined) throw new Error("usage: set <issue number> <field> <value>");
  setField(issueUrl(number), field, value);
  console.log(`#${number} ${field} = ${value}`);
}

function get([number, ...rest]) {
  if (!number) throw new Error("usage: get <issue number> [--out <path> [--raw]]");
  const flags = parseFlags(rest);
  const { result, body } = fetchIssue(number);
  if (flags.out) result.file = writeCopy(result, body, flags.out, flags.raw);
  console.log(JSON.stringify(result));
}

function prepare([number, ...rest]) {
  const n = String(number ?? "").replace(/^#/, "");
  if (!/^\d+$/.test(n)) throw new Error("usage: prepare <issue number> [--tier 1|2] [--skip tests,review]");
  const flags = parseFlags(rest);
  const { result: issue, body } = fetchIssue(n);
  const refuse = (reason, next) => console.log(JSON.stringify({ ok: false, number: issue.number, title: issue.title, reason, next }));

  if (issue.state === "CLOSED" || issue.status === "Done") {
    return refuse("already shipped (closed or Done)", "/design for a follow-up spec");
  }
  if (issue.epic) {
    const open = issue.subIssues.filter((s) => s.state === "OPEN");
    const list = issue.subIssues.map((s) => `#${s.number} ${s.title} (${s.state.toLowerCase()})`).join("; ");
    return refuse(`an epic is never built itself — sub-issues: ${list || "none"}`, open.length ? `/build #${open[0].number}` : "/design to add its parts");
  }
  if (issue.parent) {
    const siblings = subIssues(issue.parent);
    const earlier = siblings.slice(0, siblings.findIndex((s) => s.number === issue.number)).filter((s) => s.state === "OPEN");
    if (earlier.length) {
      const list = earlier.map((s) => `#${s.number} ${s.title}`).join("; ");
      return refuse(`part of epic #${issue.parent}, whose earlier part(s) are still open: ${list} — parts build in order, each from master after the previous one merged`, `/build #${earlier[0].number}`);
    }
  }
  const prerequisites = dependsOn(body)
    .map((d) => JSON.parse(gh(["issue", "view", String(d), "--repo", REPO, "--json", "number,title,state"])))
    .filter((d) => d.state === "OPEN");
  if (prerequisites.length) {
    const list = prerequisites.map((d) => `#${d.number} ${d.title}`).join("; ");
    return refuse(
      `depends on issue(s) still open: ${list} — build and merge them first, every branch is cut from master`,
      `/build #${prerequisites[0].number}`,
    );
  }
  const missing = [!issue.labels.includes("spec") && "the `spec` label", !issue.inProject && "a card on the project", !issue.branch && "a Branch field"].filter(Boolean);
  if (missing.length) return refuse(`not a /design or /fix spec: missing ${missing.join(", ")}`, "/design to amend or republish it");

  const tier = Number(flags.tier ?? issue.tier);
  if (![1, 2].includes(tier)) return refuse(`tier is ${flags.tier ?? issue.tier ?? "unset"}, not 1 or 2`, `node scripts/gh-project.mjs set ${n} Tier <1|2>`);
  const skip = flags.skip
    ? flags.skip.split(",").map((s) => s.trim().toLowerCase()).filter(Boolean)
    : issue.skipTests ? ["tests"] : [];
  const bad = skip.filter((s) => !SKIPPABLE.has(s));
  if (bad.length) return refuse(`cannot skip ${bad.join(", ")} — only tests and review are skippable; verify always runs`, `/build #${n}`);

  const spec = writeCopy(issue, body, `${ISSUES_DIR}/${n}.md`, false);
  const resumed = issue.status === "In progress";
  if (!resumed) setField(issue.url, "Status", "In progress");
  console.log(
    JSON.stringify({
      ok: true,
      resumed,
      url: issue.url,
      workflowArgs: { spec, issue: issue.number, branch: issue.branch, title: issue.title, tier, maxRounds: 2, skip },
    }),
  );
}

function report([number, ...rest]) {
  if (!number) throw new Error("usage: report <issue number> [--json '<report>']   (else JSON on stdin)");
  const flags = parseFlags(rest);
  const run = JSON.parse(flags.json ?? readStdin());
  const where = (f) => [f.file, f.line].filter(Boolean).join(":") || "—";
  const lines = [];

  if (run.status === "shipped") {
    const [pr] = JSON.parse(gh(["pr", "list", "--repo", REPO, "--head", run.branch, "--state", "open", "--json", "url", "--limit", "1"]));
    lines.push(`**Built** on \`${run.branch}\` — ${pr ? `PR ${pr.url} is open, awaiting your merge.` : "pushed, but no open PR was found for it."}`, "");
    lines.push(`- Tests: ${run.tests?.length ? run.tests.map((t) => `\`${t}\``).join(", ") : "none — skip-tests"}`);
    lines.push(`- Fix rounds: ${run.rounds ?? 0}`);
    lines.push(`- Review: ${run.reviewRan ? `clean, ${run.minor?.length ?? 0} minor finding(s)` : "skipped"}`);
    for (const f of run.minor ?? []) lines.push(`  - \`${where(f)}\` — ${f.claim}`);
  } else {
    lines.push(`**Blocked** at \`${run.stage ?? "?"}\`${run.reason ? ` — ${run.reason}` : ""}`, "");
    for (const f of run.failures ?? []) lines.push(`- ${f.step}: ${f.summary}${f.file ? ` (\`${f.file}\`)` : ""}`);
    for (const f of run.blocking ?? []) lines.push(`- \`${where(f)}\` — ${f.claim}`);
    lines.push(
      "",
      run.stage === "ship"
        ? "Verified and reviewed, but not shipped: finish the commit, push and PR by hand — a re-run of `/build` would repeat the whole pipeline."
        : `Fix the spec or the named problem, then re-run \`/build #${number}\` — the work stays on its branch.`,
    );
  }

  if (run.deviations?.length) {
    lines.push("", "**Deviations** from the spec or the tests, made by the implementer:");
    for (const d of run.deviations) lines.push(`- ${d.kind}${d.file ? ` \`${d.file}\`` : ""} — ${d.what} (why: ${d.why})`);
  }

  gh(["issue", "comment", String(number), "--repo", REPO, "--body-file", "-"], lines.join("\n"));
  console.log(`#${number}: report posted`);
  if (run.status === "shipped" && run.reviewRan) tick([number]);
}

function list() {
  const result = projectItems().map((item) => {
    const entry = { number: item.content.number, title: item.content.title, ...fieldsOf(item, item.labels ?? []) };
    if (entry.epic) entry.subIssues = subIssues(entry.number).map((s) => s.number);
    return entry;
  });
  console.log(JSON.stringify(result));
}

function comment([number, ...rest]) {
  const flags = parseFlags(rest);
  if (!number || !flags["body-file"]) throw new Error("usage: comment <issue number> --body-file <path>");
  console.log(gh(["issue", "comment", number, "--repo", REPO, "--body-file", flags["body-file"]]));
}

function tick([number]) {
  if (!number) throw new Error("usage: tick <issue number>");
  const { body } = JSON.parse(gh(["issue", "view", String(number), "--repo", REPO, "--json", "body"]));
  let count = 0;
  const ticked = body.replace(/^(\s*[-*] )\[ \]( \*\*AC-)/gm, (_, bullet, rest) => {
    count++;
    return `${bullet}[x]${rest}`;
  });
  if (count) gh(["issue", "edit", String(number), "--repo", REPO, "--body-file", "-"], ticked);
  console.log(`#${number}: ticked ${count} acceptance criteria`);
}

function check(rest) {
  const flags = parseFlags(rest);
  if (!flags["body-file"]) throw new Error("usage: check --body-file <path> [--epic]");
  const body = preparedBody(flags["body-file"], flags.epic);
  console.log(`publishable — ${body.split("\n").length} lines after cleaning`);
}

const VERBS = { init, check, create: (rest) => create(parseFlags(rest)), edit, set, get, prepare, report, list, comment, tick };

const [command, ...rest] = process.argv.slice(2);
try {
  const verb = VERBS[command];
  if (!verb) throw new Error(`usage: gh-project.mjs ${Object.keys(VERBS).join(" | ")}`);
  verb(rest);
} catch (err) {
  console.error(err.message);
  process.exit(1);
}
