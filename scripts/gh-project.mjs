#!/usr/bin/env node
// Specs live as GitHub issues on the "FinMel" user project. This is the one place that knows the
// project's number, its custom fields and the spec labels, so skills call a verb instead of
// hand-assembling `gh project` invocations — and so every mechanical step (cleaning a draft,
// checking it, deciding whether an issue is buildable, formatting a run report) is code, not tokens.
//
// Usage:
//   node scripts/gh-project.mjs init
//       Idempotent. Creates the custom fields (Tier, Kind, Branch) and the labels (spec, epic,
//       skip-tests) when missing. Built-in Status (Todo / In progress / Done) is left as it is.
//
//   node scripts/gh-project.mjs create --title <t> --body-file <path> --slug <slug>
//                                      --tier 1|2 --kind new|change|cleanup|fix
//                                      [--skip-tests] [--parent <issue number>] [--epic]
//       Cleans the draft (strips HTML comments, drops empty sections and empty table rows), checks it
//       (Goal, Out of scope, and at least one `- [ ] **AC-n**` checkbox, each naming a `proof:`; an
//       epic needs only Goal) and refuses with the list of problems if it fails. Then creates the
//       issue (label `spec`, plus `skip-tests`), adds it to the project and sets Status=Todo, Tier,
//       Kind, Branch=feat/<slug> (fix/<slug> for kind fix). --parent makes it a sub-issue. --epic
//       creates the umbrella of a split spec instead: label `epic`, no Branch — it is never built
//       itself, its sub-issues are. Prints one JSON line: {"number","url","branch"}.
//
//   node scripts/gh-project.mjs check --body-file <path> [--epic]
//       Dry run of the cleaning and checking `create` does: prints "publishable" or the problems.
//
//   node scripts/gh-project.mjs edit <issue number> --body-file <path> [--tier 1|2]
//       Replaces the body of an existing spec issue, cleaned and checked exactly like `create`.
//
//   node scripts/gh-project.mjs set <issue number> <field> <value>
//       Sets one project field on an issue already in the project, e.g. `set 123 Status "In progress"`.
//
//   node scripts/gh-project.mjs get <issue number> [--out <path> [--raw]]
//       Prints one JSON line: {number,title,url,state,labels,inProject,status,tier,kind,branch,epic,
//       skipTests,subIssues:[{number,title,state}]}. --out also writes the issue to a local file: a
//       title heading, one metadata line, then the body — or with --raw the body alone, as a draft
//       to amend and publish back with `edit`.
//
//   node scripts/gh-project.mjs prepare <issue number> [--tier 1|2] [--skip tests,review]
//       Everything /build does before the workflow: fetches the issue, decides whether it is
//       buildable, writes the local copy to skarbiec-plan/issues/<n>.md, resolves tier and skipped
//       phases, and moves the card to In progress. Prints one JSON line — either
//       {"ok":true,"resumed":bool,"workflowArgs":{...}} to pass to Workflow verbatim, or
//       {"ok":false,"reason":"...","next":"..."} with what to run instead.
//
//   node scripts/gh-project.mjs report <issue number> [--json '<run report>']   (else JSON on stdin)
//       Formats a build run's result as an issue comment and posts it. On a staged run whose review
//       ran, also ticks every acceptance criterion. The build-feature workflow calls this through
//       its ops agent, so nobody hand-writes the comment.
//
//   node scripts/gh-project.mjs list
//       Prints a JSON array of every issue on the project with its fields; an epic also carries its
//       sub-issue numbers in build order.
//
//   node scripts/gh-project.mjs comment <issue number> --body-file <path>
//       Posts a comment on the issue.
//
//   node scripts/gh-project.mjs tick <issue number>
//       Ticks every unticked acceptance-criterion checkbox (`- [ ] **AC-`) in the issue body.
//
// Node >= 22, ESM, zero npm dependencies, requires `gh` >= 2.97 authenticated with the `project`
// scope (`gh auth refresh -s project`). Exit 1 on any failure, with the step that failed on stderr;
// if the issue was already created, its URL is printed too so the remaining steps can be re-run
// with `set`.

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

// ---------------------------------------------------------------------------------------------
// draft cleaning and checking
// ---------------------------------------------------------------------------------------------

const isTableRule = (line) => /^\|[\s|:-]*\|$/.test(line.trim());
const isEmptyRow = (line) => /^\|(\s*\|)+\s*$/.test(line.trim());

// A section is empty when nothing but blank lines, sub-headings or a bare table header is left.
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

function lintBody(body, { epic }) {
  const problems = [];
  if (!/^## Goal\s*$/m.test(body)) problems.push("no `## Goal` section (or it is empty)");
  if (epic) return problems;
  if (!/^## Out of scope\s*$/m.test(body)) problems.push("no `## Out of scope` section (or it is empty) — it is not optional");
  // An AC is its checkbox line plus any indented lines under it (a multi-line AC keeps its proof in
  // a sub-bullet); the block ends at the next checkbox, heading or unindented line.
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
  return body;
}

// ---------------------------------------------------------------------------------------------
// project reads
// ---------------------------------------------------------------------------------------------

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
    gh(["issue", "view", String(number), "--repo", REPO, "--json", "number,title,body,state,labels,url"]),
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

// ---------------------------------------------------------------------------------------------
// verbs
// ---------------------------------------------------------------------------------------------

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
  if (!number || !flags["body-file"]) throw new Error("usage: edit <issue number> --body-file <path> [--tier 1|2]");
  if (flags.tier && !["1", "2"].includes(flags.tier)) throw new Error("--tier must be 1 or 2");
  const labels = JSON.parse(gh(["issue", "view", number, "--repo", REPO, "--json", "labels"])).labels.map((l) => l.name);
  const body = preparedBody(flags["body-file"], labels.includes("epic"));
  gh(["issue", "edit", number, "--repo", REPO, "--body-file", "-"], body);
  if (flags.tier) setField(issueUrl(number), "Tier", flags.tier);
  console.log(`#${number}: body replaced${flags.tier ? `, Tier = ${flags.tier}` : ""}`);
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

// The run report the build-feature workflow sends: {status: "staged"|"blocked", stage?, reason?,
// branch?, tests?: string[], rounds?, reviewRan?, minor?: [{file,line,claim}],
// failures?: [{step,summary,file}], blocking?: [{file,line,claim}]}.
function report([number, ...rest]) {
  if (!number) throw new Error("usage: report <issue number> [--json '<report>']   (else JSON on stdin)");
  const flags = parseFlags(rest);
  const run = JSON.parse(flags.json ?? readStdin());
  const where = (f) => [f.file, f.line].filter(Boolean).join(":") || "—";
  const lines = [];

  if (run.status === "staged") {
    lines.push(`**Built** on \`${run.branch}\` — staged, awaiting commit and PR.`, "");
    lines.push(`- Tests: ${run.tests?.length ? run.tests.map((t) => `\`${t}\``).join(", ") : "none — skip-tests"}`);
    lines.push(`- Fix rounds: ${run.rounds ?? 0}`);
    lines.push(`- Review: ${run.reviewRan ? `clean, ${run.minor?.length ?? 0} minor finding(s)` : "skipped"}`);
    for (const f of run.minor ?? []) lines.push(`  - \`${where(f)}\` — ${f.claim}`);
  } else {
    lines.push(`**Blocked** at \`${run.stage ?? "?"}\`${run.reason ? ` — ${run.reason}` : ""}`, "");
    for (const f of run.failures ?? []) lines.push(`- ${f.step}: ${f.summary}${f.file ? ` (\`${f.file}\`)` : ""}`);
    for (const f of run.blocking ?? []) lines.push(`- \`${where(f)}\` — ${f.claim}`);
    lines.push("", `Fix the spec or the named problem, then re-run \`/build #${number}\` — the work stays on its branch.`);
  }

  gh(["issue", "comment", String(number), "--repo", REPO, "--body-file", "-"], lines.join("\n"));
  console.log(`#${number}: report posted`);
  if (run.status === "staged" && run.reviewRan) tick([number]);
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
