// Usage: imported by autobuild.mjs - the flag parser and the interactive start wizard (questions, summary, equivalent command); runs nothing on import
import { createInterface } from "node:readline/promises";
import { planFrom, startAt, windowEnd } from "./autobuild-queue.mjs";
import { areaText } from "./queue.mjs";

const BOOLEAN_FLAGS = new Set(["dry-run", "shutdown", "no-shutdown", "include-manual", "skip-manual", "stop"]);
const KNOWN_FLAGS = new Set([...BOOLEAN_FLAGS, "max-tasks", "items", "start", "until", "on-limit", "max-wait", "sleep-test", "hibernate-test"]);
const USAGE = "usage: autobuild.mjs [<n>] [--items N|all] [--include-manual|--skip-manual] [--start now|MIN|HH:MM] [--until HH:MM] [--max-tasks N] [--on-limit sleep|wait|exit] [--max-wait HOURS] [--shutdown|--no-shutdown] [--dry-run] | --stop | --sleep-test MIN | --hibernate-test MIN";
const COMMAND_ORDER = ["items", "include-manual", "skip-manual", "on-limit", "max-wait", "shutdown", "no-shutdown", "start", "until", "max-tasks", "dry-run"];
const LIMIT_MODES = { a: "sleep", b: "wait", c: "exit" };
const LIMIT_LABELS = { sleep: "sleep and wake after the reset", wait: "wait awake", exit: "end the run" };

export class UsageError extends Error {}

export class WizardCancelled extends Error {
  constructor() {
    super("wizard cancelled");
  }
}

export function parseFlags(argv) {
  const flags = {};
  for (let i = 0; i < argv.length; i++) {
    if (/^\d+$/.test(argv[i]) && flags.only === undefined) {
      flags.only = Number(argv[i]);
      continue;
    }
    const key = argv[i].slice(2);
    if (!argv[i].startsWith("--") || !KNOWN_FLAGS.has(key)) throw new UsageError(`unexpected argument: ${argv[i]}\n${USAGE}`);
    flags[key] = BOOLEAN_FLAGS.has(key) ? true : argv[++i];
    if (flags[key] === undefined) throw new UsageError(`--${key} needs a value\n${USAGE}`);
  }
  return flags;
}

const pad = (n) => String(n).padStart(2, "0");
const stamp = (date) => `${date.toLocaleDateString("en-CA")} ${pad(date.getHours())}:${pad(date.getMinutes())}`;

function yesNo(text) {
  const answer = text.trim().toLowerCase();
  if (["t", "tak", "y", "yes"].includes(answer)) return true;
  if (["n", "nie", "no"].includes(answer)) return false;
  return null;
}

const planLine = (item, i) => `${i + 1}. #${item.number} ${item.title} (${item.status}${item.manual ? ", ręczny" : ""}) [${areaText(item)}]`;

function confirmQuestion(id, prompt, whenYes, whenNo) {
  return {
    id,
    prompt: `${prompt} [T/n]: `,
    default: "t",
    parse(text) {
      const yes = yesNo(text);
      return yes === null ? { error: "answer t (tak) or n (nie)" } : { flags: yes ? whenYes : whenNo };
    },
  };
}

function itemsQuestion(ctx) {
  const queue = ctx.plan.length ? ctx.plan.map(planLine).join("\n") : "(the queue is empty)";
  return {
    id: "items",
    prompt: `Plan - the build queue, top to bottom:\n${queue}\nHow many items? all or a number [all]: `,
    default: "all",
    parse(text) {
      const value = text.toLowerCase();
      return /^(all|[1-9]\d*)$/.test(value) ? { flags: { items: value } } : { error: "use all or a positive number" };
    },
  };
}

function limitQuestion(power) {
  const works = power.works;
  const state = works ? "wake timers: on" : `wake timers: off - ${power.hint}`;
  const first = `a) sleep and wake after the reset (suspend under 1 h, hibernate from 1 h)${works ? "" : ` - unavailable: ${power.hint}`}`;
  return {
    id: "on-limit",
    prompt: `When the Claude usage limit hits (${state}):\n  ${first}\n  b) wait awake\n  c) end the run\nChoice [${works ? "a" : "b"}]: `,
    default: works ? "a" : "b",
    parse(text) {
      const mode = LIMIT_MODES[text.toLowerCase()];
      if (!mode) return { error: "choose a, b or c" };
      if (mode === "sleep" && !works) return { error: `a) is unavailable: ${power.hint}` };
      return { flags: { "on-limit": mode } };
    },
  };
}

const maxWaitQuestion = {
  id: "max-wait",
  prompt: "Longest limit wait in hours; a reset further away ends the run [12]: ",
  default: "12",
  when: (answers) => answers["on-limit"] !== "exit",
  parse: (text) => (Number.isFinite(Number(text)) && Number(text) > 0 ? { flags: { "max-wait": text } } : { error: "use a positive number of hours" }),
};

function timeQuestion(id, prompt, def, check) {
  return {
    id,
    prompt,
    default: def,
    parse(text) {
      if (id === "until" && (text === "" || text.toLowerCase() === "none")) return { flags: {} };
      try {
        check(text);
      } catch (err) {
        return { error: err.message };
      }
      return { flags: { [id]: text } };
    },
  };
}

export function questions(flags, ctx) {
  if (!ctx.tty) return [];
  const list = [];
  const planned = flags.only === undefined;
  if (planned && flags.items === undefined) list.push(itemsQuestion(ctx));
  if (planned && !flags["include-manual"] && !flags["skip-manual"] && ctx.plan.some((item) => item.manual)) {
    list.push(confirmQuestion("manual", "Skip manual items (In progress, not started by autobuild.mjs)?", { "skip-manual": true }, { "include-manual": true }));
  }
  if (flags["on-limit"] === undefined) list.push(limitQuestion(ctx.power));
  if (flags["max-wait"] === undefined && flags["on-limit"] !== "exit") list.push(maxWaitQuestion);
  if (flags.shutdown === undefined && flags["no-shutdown"] === undefined) {
    list.push(confirmQuestion("shutdown", "Shut the PC down after the run?", { shutdown: true }, { "no-shutdown": true }));
  }
  if (flags.start === undefined) {
    list.push(timeQuestion("start", "Start: now, in N minutes or at HH:MM [now]: ", "now", (text) => startAt(text, ctx.now)));
  }
  if (flags.until === undefined) {
    list.push(timeQuestion("until", "No new build after HH:MM, or none [none]: ", "", (text) => windowEnd(text, ctx.now)));
  }
  return list;
}

async function askWith(rl, list, output, cancelled) {
  const answers = {};
  for (const question of list) {
    if (question.when && !question.when(answers)) continue;
    for (;;) {
      const pending = rl.question(question.prompt);
      pending.catch(() => {});
      const typed = (await Promise.race([pending, cancelled])).trim();
      const text = typed === "" ? question.default : typed;
      const result = question.parse(text);
      if (!result.error) {
        Object.assign(answers, result.flags);
        break;
      }
      output.write(`"${text}": ${result.error}\n`);
    }
  }
  return answers;
}

function openSession({ input, output }) {
  const rl = createInterface({ input, output });
  let finished = false;
  const cancelled = new Promise((_, reject) => {
    rl.on("SIGINT", () => reject(new WizardCancelled()));
    rl.on("close", () => {
      if (!finished) reject(new WizardCancelled());
    });
  });
  cancelled.catch(() => {});
  return {
    rl,
    cancelled,
    close() {
      finished = true;
      rl.close();
    },
  };
}

export async function ask(list, io) {
  const session = openSession(io);
  try {
    return await askWith(session.rl, list, io.output, session.cancelled);
  } finally {
    session.close();
  }
}

export function equivalentCommand(flags) {
  const parts = ["node", "scripts/autobuild.mjs"];
  if (flags.only !== undefined) parts.push(String(flags.only));
  for (const key of COMMAND_ORDER) {
    if (flags[key] === undefined) continue;
    parts.push(`--${key}`);
    if (!BOOLEAN_FLAGS.has(key)) parts.push(String(flags[key]));
  }
  return parts.join(" ");
}

export function summary(flags, ctx) {
  const lines = ["Summary"];
  const now = ctx.now ?? new Date();
  if (flags.only !== undefined) lines.push(`  plan: #${flags.only} only`);
  else {
    const includeManual = Boolean(flags["include-manual"]);
    const count = flags.items === undefined || flags.items === "all" ? Infinity : Number(flags.items);
    const chosen = planFrom(ctx.plan, { includeManual, items: count });
    lines.push(`  plan: ${flags.items ?? "all"} item(s), manual items ${includeManual ? "included" : "skipped"}`);
    chosen.forEach((item, i) => lines.push(`    ${planLine(item, i)}`));
  }
  const mode = flags["on-limit"] ?? "sleep";
  const wait = mode === "exit" ? "" : `, longest wait ${flags["max-wait"] ?? "12"} h`;
  lines.push(`  Claude limit: ${LIMIT_LABELS[mode]}${wait}`);
  lines.push(`  wake timers: ${ctx.power?.works ? "on" : `off - ${ctx.power?.hint ?? "not read"}`}`);
  lines.push(`  shutdown after the run: ${flags["no-shutdown"] ? "no" : "yes"}`);
  const start = startAt(flags.start, now);
  lines.push(`  start: ${stamp(start)}${flags.start === undefined || flags.start === "now" ? " (now)" : ""}`);
  lines.push(`  window end: ${flags.until === undefined ? "none" : stamp(windowEnd(flags.until, start))}`);
  return lines.join("\n");
}

export async function runWizard(flags, ctx, io) {
  const list = questions(flags, ctx);
  if (!list.length) return { run: true, flags };
  const session = openSession(io);
  try {
    const merged = { ...flags, ...(await askWith(session.rl, list, io.output, session.cancelled)) };
    io.output.write(`\n${summary(merged, ctx)}\n\nEquivalent command:\n  ${equivalentCommand(merged)}\n\n`);
    for (;;) {
      const pending = session.rl.question("Start? [T/n]: ");
      pending.catch(() => {});
      const typed = (await Promise.race([pending, session.cancelled])).trim();
      const yes = typed === "" ? true : yesNo(typed);
      if (yes !== null) return { run: yes, flags: merged };
      io.output.write(`"${typed}": answer t (tak) or n (nie)\n`);
    }
  } finally {
    session.close();
  }
}
