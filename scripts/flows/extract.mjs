import { createHash } from "node:crypto";
import { existsSync, readdirSync, readFileSync } from "node:fs";
import path from "node:path";

export class ExtractError extends Error {}

const fail = (file, what) => {
  throw new ExtractError(`${file}: ${what}`);
};

const read = (file) => readFileSync(file, "utf8").replace(/\r\n/g, "\n");

const LIST_KEYS = new Set(["skills", "tools", "disallowedTools", "allowed-tools"]);
const EFFORTS = new Set(["low", "medium", "high", "xhigh", "max"]);
const LIMIT_NAME = /_MS|_MIN|_SEC|TIMEOUT|DEADLINE|MAX_/;

export const LABELS = [
  "zmienia git",
  "merguje",
  "pisze do GitHuba",
  "dotyka lokalnego stacku",
  "dotyka izolowanego stacku e2e",
  "pełny verify",
];

const GIT_VERBS = "add|commit|push|merge|switch|checkout|reset|restore|stash|branch|pull|rebase";
const callRe = (tool, subcommand) =>
  new RegExp(
    `(?:["']${tool}["']\\s*,\\s*(?:argv:\\s*)?|\\b${tool}\\(\\s*|cmd:\\s*["']${tool}["']\\s*,\\s*argv:\\s*)\\[?\\s*["'](?:${subcommand})["']`,
  );
const GIT_MUTATION = callRe("git", GIT_VERBS);
const GH_MERGE = callRe("gh", "pr[\"']\\s*,\\s*[\"']merge");
const GH_WRITE = callRe(
  "gh",
  "issue[\"']\\s*,\\s*[\"'](?:create|edit|close|comment|develop)|pr[\"']\\s*,\\s*[\"'](?:create|edit|comment|merge)|project[\"']\\s*,\\s*[\"']item-[a-z-]+",
);
const GH_DEVELOP_CHECKOUT = /["']issue["']\s*,\s*["']develop["'][^\]]*--checkout/;
const VERIFY_ALL = /\bnode\s+scripts\/verify\.mjs[^\n]*?(?<=["'\s`])--all\b/;
const VERIFY_ALL_ARGV = /verify\.mjs["']\s*,\s*(?:[^\]\n]*?,\s*)?["']--all["']/;

function unquote(value, file, line) {
  const quote = value[0];
  if (quote !== '"' && quote !== "'") return value;
  if (value.length < 2 || value.at(-1) !== quote) fail(file, `broken frontmatter at line ${line}: unterminated quote`);
  return value.slice(1, -1);
}

export function parseFrontmatter(text, file) {
  const lines = text.split("\n");
  if (lines[0].trim() !== "---") fail(file, "broken frontmatter: the file does not start with ---");
  const end = lines.findIndex((l, i) => i > 0 && l.trim() === "---");
  if (end < 0) fail(file, "broken frontmatter: no closing ---");
  const data = {};
  let key = null;
  for (let i = 1; i < end; i++) {
    const line = lines[i];
    if (!line.trim() || line.trim().startsWith("#")) continue;
    if (/^\s/.test(line)) {
      const item = line.match(/^\s+-\s+(.*)$/);
      if (item && key && (data[key] === "" || Array.isArray(data[key]))) {
        if (!Array.isArray(data[key])) data[key] = [];
        data[key].push(unquote(item[1].trim(), file, i + 1));
      }
      continue;
    }
    const m = line.match(/^([A-Za-z][\w-]*):\s*(.*)$/);
    if (!m) fail(file, `broken frontmatter at line ${i + 1}: "${line}"`);
    key = m[1];
    const value = m[2].trim();
    if (value.startsWith("[")) {
      if (!value.endsWith("]")) fail(file, `broken frontmatter at line ${i + 1}: unterminated [ in "${key}"`);
      data[key] = LIST_KEYS.has(key)
        ? value
            .slice(1, -1)
            .split(",")
            .map((s) => s.trim())
            .filter(Boolean)
        : value;
    } else data[key] = unquote(value, file, i + 1);
  }
  return { data, body: lines.slice(end + 1).join("\n") };
}

const asText = (value) => (Array.isArray(value) ? value.join(", ") : value);
const asBool = (value) => (value === undefined ? undefined : value === "true" || value === true);

function listDir(dir, filter) {
  return existsSync(dir) ? readdirSync(dir, { withFileTypes: true }).filter(filter).map((e) => e.name).sort() : [];
}

function extractCommands(root) {
  const nodes = [];
  const base = path.join(root, ".claude", "skills");
  for (const dir of listDir(base, (e) => e.isDirectory())) {
    const rel = `.claude/skills/${dir}/SKILL.md`;
    if (!existsSync(path.join(root, rel))) continue;
    const text = read(path.join(root, rel));
    const { data } = parseFrontmatter(text, rel);
    if (data["user-invocable"] === "false") continue;
    nodes.push({
      id: `cmd:${dir}`,
      kind: "command",
      name: dir,
      file: rel,
      text,
      frontAgent: data.agent,
      facts: {
        description: data.description,
        argumentHint: data["argument-hint"],
        model: data.model,
        effort: data.effort,
        allowedTools: asText(data["allowed-tools"]),
        context: data.context,
        agent: data.agent,
        disableModelInvocation: asBool(data["disable-model-invocation"]),
      },
    });
  }
  return nodes;
}

function extractAgents(root) {
  const nodes = [];
  const base = path.join(root, ".claude", "agents");
  for (const file of listDir(base, (e) => e.isFile() && e.name.endsWith(".md"))) {
    const rel = `.claude/agents/${file}`;
    const text = read(path.join(root, rel));
    const { data, body } = parseFrontmatter(text, rel);
    const name = data.name || file.replace(/\.md$/, "");
    const limits = [...new Set([...body.matchAll(/\btimeout:\s*(\d+)/g)].map((m) => Number(m[1])))].map((value) => ({
      name: "timeout",
      value,
      unit: "ms",
    }));
    const skills = data.skills === undefined ? [] : Array.isArray(data.skills) ? data.skills : [data.skills];
    nodes.push({
      id: `agent:${name}`,
      kind: "agent",
      name,
      file: rel,
      text,
      facts: {
        description: data.description,
        model: data.model,
        effort: data.effort,
        tools: asText(data.tools),
        disallowedTools: asText(data.disallowedTools),
        skills,
        limits,
      },
    });
  }
  return nodes;
}

function matchBrace(text, open) {
  let depth = 0;
  for (let i = open; i < text.length; i++) {
    const c = text[i];
    if (c === '"' || c === "'" || c === "`") {
      for (i++; i < text.length && text[i] !== c; i++) if (text[i] === "\\") i++;
    } else if (c === "/" && text[i + 1] === "/") {
      while (i < text.length && text[i] !== "\n") i++;
    } else if (c === "/" && text[i + 1] === "*") {
      i = text.indexOf("*/", i + 2);
      if (i < 0) return -1;
      i++;
    } else if (c === "{") depth++;
    else if (c === "}" && --depth === 0) return i;
  }
  return -1;
}

function stripLineComments(text) {
  let out = "";
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (c === '"' || c === "'" || c === "`") {
      const start = i;
      for (i++; i < text.length && text[i] !== c; i++) if (text[i] === "\\") i++;
      out += text.slice(start, i + 1);
    } else if (c === "/" && text[i + 1] === "/") {
      while (i < text.length && text[i] !== "\n") i++;
      out += "\n";
    } else out += c;
  }
  return out;
}

function splitTernary(expr) {
  let depth = 0;
  let quote = null;
  let q = -1;
  let nest = 0;
  for (let i = 0; i < expr.length; i++) {
    const c = expr[i];
    if (quote) {
      if (c === "\\") i++;
      else if (c === quote) quote = null;
      continue;
    }
    if (c === '"' || c === "'" || c === "`") quote = c;
    else if ("([{".includes(c)) depth++;
    else if (")]}".includes(c)) depth--;
    else if (depth === 0 && c === "?" && expr[i + 1] !== "?" && expr[i + 1] !== "." && expr[i - 1] !== "?") {
      if (q < 0) q = i;
      else nest++;
    } else if (depth === 0 && c === ":" && q >= 0) {
      if (nest === 0) {
        return { cond: expr.slice(0, q).trim(), cons: expr.slice(q + 1, i).trim(), alt: expr.slice(i + 1).trim() };
      }
      nest--;
    }
  }
  return null;
}

function splitTopLevel(text, separator) {
  const parts = [];
  let depth = 0;
  let quote = null;
  let start = 0;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (quote) {
      if (c === "\\") i++;
      else if (c === quote) quote = null;
    } else if (c === '"' || c === "'" || c === "`") quote = c;
    else if ("([{".includes(c)) depth++;
    else if (")]}".includes(c)) depth--;
    else if (depth === 0 && c === separator) {
      parts.push(text.slice(start, i));
      start = i + 1;
    }
  }
  parts.push(text.slice(start));
  return parts;
}

const joinCond = (a, b) => (!a ? b : !b ? a : b === "otherwise" ? `${a} (otherwise)` : `${a} && ${b}`);

function rowsOf(expr, constants) {
  const ternary = splitTernary(expr);
  if (ternary) {
    const cons = rowsOf(ternary.cons, constants);
    const alt = rowsOf(ternary.alt, constants);
    if (!cons || !alt) return null;
    return [
      ...cons.map((r) => ({ cond: joinCond(ternary.cond, r.cond), text: r.text })),
      ...alt.map((r) => ({ cond: r.cond ?? "otherwise", text: r.text })),
    ];
  }
  const str = expr.match(/^(['"])([^'"\n]*)\1$/);
  if (str) return [{ cond: null, text: str[2] }];
  if (/^\w+$/.test(expr)) return expr in constants ? [{ cond: null, text: constants[expr] }] : null;
  const obj = expr.match(/^\{([\s\S]*)\}$/);
  if (!obj) return null;
  const props = {};
  for (const part of splitTopLevel(obj[1], ",")) {
    const m = part.match(/^\s*(\w+)\s*:\s*([\s\S]*?)\s*$/);
    if (m) props[m[1]] = m[2];
  }
  if (!props.model || !props.effort) return null;
  const models = rowsOf(props.model, constants);
  const efforts = rowsOf(props.effort, constants);
  if (!models || !efforts) return null;
  return models.flatMap((m) => efforts.map((e) => ({ cond: joinCond(m.cond, e.cond), text: `${m.text}/${e.text}` })));
}

function extractLevels(text) {
  const constants = {};
  for (const m of text.matchAll(/^const\s+(\w+)\s*=\s*(['"])([^'"\n]*)\2\s*$/gm)) constants[m[1]] = m[3];
  const lines = text.split("\n");
  const levels = [];
  for (let i = 0; i < lines.length; i++) {
    const m = lines[i].match(/^const\s+(\w+)\s*=(.*)$/);
    if (!m) continue;
    let expr = m[2];
    for (let j = i + 1; j < lines.length && /^\s+\S/.test(lines[j]); j++) expr += `\n${lines[j]}`;
    expr = stripLineComments(expr).trim().replace(/;$/, "").trim();
    let rows = null;
    try {
      rows = rowsOf(expr, constants);
    } catch {
      rows = null;
    }
    if (!rows || !(splitTernary(expr) || expr.startsWith("{"))) continue;
    if (!rows.every((r) => EFFORTS.has(r.text) || /^[\w.-]+\/[a-z]+$/.test(r.text))) continue;
    levels.push({ name: m[1], rows: rows.map((r) => `${r.cond ?? "always"} → ${r.text}`) });
  }
  return levels;
}

function extractWorkflows(root) {
  const nodes = [];
  const base = path.join(root, ".claude", "workflows");
  for (const file of listDir(base, (e) => e.isFile() && e.name.endsWith(".js"))) {
    const rel = `.claude/workflows/${file}`;
    const text = read(path.join(root, rel));
    const start = text.search(/export\s+const\s+meta\s*=\s*\{/);
    if (start < 0) fail(rel, "no `export const meta = {…}` literal found");
    const open = text.indexOf("{", start);
    const close = matchBrace(text, open);
    if (close < 0) fail(rel, "the `meta` literal is not closed");
    let meta;
    try {
      meta = new Function(`"use strict"; return (${text.slice(open, close + 1)});`)();
    } catch (error) {
      fail(rel, `the \`meta\` literal is not parsable: ${error.message}`);
    }
    if (!meta || typeof meta.name !== "string" || !meta.name) fail(rel, "the `meta` literal has no name");
    nodes.push({
      id: `wf:${meta.name}`,
      kind: "workflow",
      name: meta.name,
      file: rel,
      text,
      facts: {
        description: meta.description,
        whenToUse: meta.whenToUse,
        phases: (meta.phases ?? []).map((p) => ({ title: p.title, detail: p.detail, model: p.model })),
        levels: extractLevels(text),
      },
    });
  }
  return nodes;
}

function scriptLimits(text) {
  const limits = [];
  for (const line of text.split("\n")) {
    const m = line.match(/^(?:export\s+)?const\s+([A-Z][A-Z0-9_]*)\s*=\s*([^;,]+?)\s*;?\s*(?:\/\/.*)?$/);
    if (!m || !LIMIT_NAME.test(m[1]) || !/^[\d\s_*+\-/().]+$/.test(m[2])) continue;
    let value;
    try {
      value = new Function(`return (${m[2]});`)();
    } catch {
      continue;
    }
    if (typeof value !== "number" || !Number.isFinite(value)) continue;
    const unit = /_MS(?:_|$)/.test(m[1]) ? "ms" : /_SEC(?:_|$)/.test(m[1]) ? "s" : /_MIN(?:_|$)/.test(m[1]) ? "min" : "";
    limits.push({ name: m[1], value, unit });
  }
  return limits;
}

function extractScripts(root) {
  const nodes = [];
  const base = path.join(root, "scripts");
  for (const file of listDir(base, (e) => e.isFile() && e.name.endsWith(".mjs") && !e.name.endsWith(".test.mjs"))) {
    const rel = `scripts/${file}`;
    const text = read(path.join(root, rel));
    const lines = text.split("\n");
    const at = lines.slice(0, 40).findIndex((l) => /^\/\/\s*Usage:/.test(l));
    if (at < 0) fail(rel, "no `// Usage:` header line in the first 40 lines");
    const usage = lines[at].replace(/^\/\/\s*Usage:\s*/, "");
    const flags = [];
    for (let k = at + 1; k < lines.length && lines[k].startsWith("//"); k++) {
      const content = lines[k].replace(/^\/\/ ?/, "");
      if (!content.trim()) continue;
      const m = content.match(/^\s*(\S.*?)\s{2,}(\S.*)$/);
      if (m) flags.push({ flag: m[1], text: m[2] });
      else if (flags.length) flags.at(-1).text = `${flags.at(-1).text} ${content.trim()}`.trim();
    }
    if (!flags.length) {
      const head = usage.split(/\s{3,}/)[0];
      for (const flag of new Set(head.match(/--[\w-]+/g) ?? [])) flags.push({ flag, text: "" });
    }
    const name = file.replace(/\.mjs$/, "");
    nodes.push({
      id: `script:${name}`,
      kind: "script",
      name,
      file: rel,
      text,
      facts: { usage, flags, limits: scriptLimits(text) },
    });
  }
  return nodes;
}

const codeOf = (text) =>
  text
    .split("\n")
    .filter((l) => !/^\s*\/\//.test(l))
    .join("\n");

const verifiesAll = (text, pattern = VERIFY_ALL) => text.split("\n").some((l) => !l.includes("--cache-check") && pattern.test(l));

function directLabels(node) {
  const labels = new Set();
  if (node.kind === "script") {
    const code = codeOf(node.text);
    if (GIT_MUTATION.test(code) || GH_DEVELOP_CHECKOUT.test(code)) labels.add("zmienia git");
    if (GH_MERGE.test(code)) labels.add("merguje");
    if (GH_WRITE.test(code)) labels.add("pisze do GitHuba");
    if (node.name !== "verify" && verifiesAll(code, VERIFY_ALL_ARGV)) labels.add("pełny verify");
    if (node.name === "compose" || node.name === "stop-stack") labels.add("dotyka lokalnego stacku");
    if (node.name === "e2e") labels.add("dotyka izolowanego stacku e2e");
  } else if (verifiesAll(node.text)) labels.add("pełny verify");
  return labels;
}

function holderName(node) {
  if (node.kind === "script") return `${node.name}.mjs`;
  if (node.kind === "command") return `/${node.name}`;
  return node.name;
}

function computeMarkers(nodes, edges) {
  const direct = new Map(nodes.map((n) => [n.id, directLabels(n)]));
  const byId = new Map(nodes.map((n) => [n.id, n]));
  const next = new Map(nodes.map((n) => [n.id, []]));
  for (const e of edges) next.get(e.from).push(e.to);
  for (const node of nodes) {
    const markers = LABELS.filter((l) => direct.get(node.id).has(l)).map((label) => ({ label, direct: true }));
    const seen = new Set([node.id]);
    const queue = [[node.id]];
    const inherited = new Map();
    while (queue.length) {
      const trail = queue.shift();
      for (const to of next.get(trail.at(-1))) {
        if (seen.has(to)) continue;
        seen.add(to);
        const pathIds = [...trail, to];
        for (const label of direct.get(to)) {
          if (!direct.get(node.id).has(label) && !inherited.has(label)) {
            inherited.set(label, { label, direct: false, via: holderName(byId.get(to)), path: pathIds });
          }
        }
        queue.push(pathIds);
      }
    }
    for (const label of LABELS) if (inherited.has(label)) markers.push(inherited.get(label));
    node.markers = markers;
  }
}

function computeEdges(nodes) {
  const scripts = new Set(nodes.filter((n) => n.kind === "script").map((n) => n.name));
  const commands = new Set(nodes.filter((n) => n.kind === "command").map((n) => n.name));
  const agents = new Set(nodes.filter((n) => n.kind === "agent").map((n) => n.name));
  const workflows = new Set(nodes.filter((n) => n.kind === "workflow").map((n) => n.name));
  const edges = new Map();
  const add = (from, to) => {
    if (from !== to) edges.set(`${from}\0${to}`, { from, to });
  };
  for (const node of nodes) {
    const text = node.kind === "script" ? codeOf(node.text) : node.text;
    if (node.kind !== "script") {
      for (const m of text.matchAll(/\bnode\s+scripts\/([A-Za-z0-9_-]+)\.mjs/g)) if (scripts.has(m[1])) add(node.id, `script:${m[1]}`);
    } else {
      for (const m of text.matchAll(/["'`](?:\.\/|scripts\/)?([A-Za-z0-9_-]+)\.mjs["'`]/g)) {
        if (scripts.has(m[1])) add(node.id, `script:${m[1]}`);
      }
      if (/["'`]claude(?:\.cmd|\.exe)?["'`]|\bclaude\s+-p\b/.test(text)) {
        for (const m of text.matchAll(/(?<=["'`\s(])\/([a-z][\w-]*)(?=[\s"'`:#.,)]|$)/g)) {
          if (commands.has(m[1])) add(node.id, `cmd:${m[1]}`);
        }
      }
    }
    if (node.kind === "command") {
      for (const m of text.matchAll(/`([^`\n]+)`/g)) {
        if (workflows.has(m[1])) add(node.id, `wf:${m[1]}`);
        if (agents.has(m[1])) add(node.id, `agent:${m[1]}`);
      }
      if (node.frontAgent && agents.has(node.frontAgent)) add(node.id, `agent:${node.frontAgent}`);
    }
    if (node.kind === "workflow") {
      for (const m of text.matchAll(/\bstep\(\s*['"]([\w-]+)['"]|\bagentType\s*[:=]\s*['"]([\w-]+)['"]/g)) {
        const name = m[1] ?? m[2];
        if (agents.has(name)) add(node.id, `agent:${name}`);
      }
    }
  }
  return [...edges.values()].sort((a, b) => (a.from + a.to).localeCompare(b.from + b.to));
}

function hashFiles(root, files) {
  const hash = createHash("sha256");
  for (const file of files) {
    hash.update(file);
    hash.update("\0");
    hash.update(read(path.join(root, file)));
    hash.update("\0");
  }
  return hash.digest("hex").slice(0, 16);
}

function filesUnder(root, dir) {
  const out = [];
  const walk = (rel) => {
    const abs = path.join(root, rel);
    if (!existsSync(abs)) return;
    for (const entry of readdirSync(abs, { withFileTypes: true })) {
      const child = `${rel}/${entry.name}`;
      if (entry.isDirectory()) {
        if (entry.name !== "fixtures" && entry.name !== "node_modules") walk(child);
      } else if (!entry.name.endsWith(".test.mjs")) out.push(child);
    }
  };
  walk(dir);
  return out;
}

function ownFiles(root, node) {
  if (node.kind === "command") return filesUnder(root, path.posix.dirname(node.file));
  if (node.kind === "script") return [node.file, ...filesUnder(root, `scripts/${node.name}`)];
  return [node.file];
}

function sourcesOf(root, node, byId, edges) {
  const callees = (id, prefix) => edges.filter((e) => e.from === id && e.to.startsWith(prefix)).map((e) => e.to);
  const workflows = callees(node.id, "wf:");
  const agents = new Set([...callees(node.id, "agent:"), ...workflows.flatMap((id) => callees(id, "agent:"))]);
  const files = new Set(ownFiles(root, node));
  for (const id of [...workflows, ...agents]) for (const file of ownFiles(root, byId.get(id))) files.add(file);
  return [...files].sort((a, b) => (a === node.file ? -1 : b === node.file ? 1 : a.localeCompare(b)));
}

export function collect(root) {
  const all = [...extractCommands(root), ...extractAgents(root), ...extractWorkflows(root), ...extractScripts(root)];
  const edges = computeEdges(all);
  computeMarkers(all, edges);
  const byId = new Map(all.map((n) => [n.id, n]));
  const nodes = {};
  for (const node of all.sort((a, b) => a.id.localeCompare(b.id))) {
    const sources = sourcesOf(root, node, byId, edges);
    nodes[node.id] = {
      kind: node.kind,
      name: node.name,
      file: node.file,
      facts: node.facts,
      sources,
      hash: hashFiles(root, sources),
      markers: node.markers,
    };
  }
  return { nodes, edges };
}
