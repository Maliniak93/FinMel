// Usage: imported by autobuild.mjs - the queue plan, the next piece of work, the start and window times; pure, injectable, runs nothing on import

import { shortOf } from "./priority.mjs";
import { itemTag, queueOf } from "./queue.mjs";

const partNumbers = (epic) => (epic.subIssues ?? []).map((s) => (typeof s === "object" ? s.number : s));
const TIME_RE = /^(\d{1,2}):(\d{2})$/;

export function planFrom(queue, { only, includeManual = false, items = Infinity } = {}, cards = queue) {
  if (only !== undefined && only !== null) return planOne(queue, Number(only), cards);
  return queue.filter((q) => includeManual || !q.manual).slice(0, items);
}

function planOne(queue, number, cards) {
  const queued = queue.find((q) => q.number === number);
  if (queued) return [queued];
  const card = cards.find((c) => c.number === number);
  const parent = cards.find((c) => c.epic && partNumbers(c).includes(number))?.number ?? card?.parent;
  if (parent != null) throw new Error(`#${number} is a part of epic #${parent}`);
  if (!card) throw new Error(`#${number} is not on the board`);
  return [];
}

export function nextWork(item, cards) {
  if (!item.epic) return item.number;
  const byNumber = new Map(cards.map((c) => [c.number, c]));
  const next = partNumbers(item)
    .map((n) => byNumber.get(n))
    .find((p) => p && p.status !== "Done");
  return next ? next.number : null;
}

export function planLines(plan, cards) {
  return plan.map((item, i) => {
    const next = nextWork(item, cards);
    const action = item.epic ? (next ? `next #${next}` : "no open part") : "standalone";
    return `${i + 1}. #${item.number} ${item.title}${item.manual ? " (ręczny)" : ""} — ${action} [${itemTag(item)}]`;
  });
}

export function pickNext(queue, { started = new Set(), items = Infinity, includeManual = false } = {}) {
  return (
    queue.find((q) => {
      if (q.manual && !includeManual) return false;
      if (started.has(q.number)) return !q.epic || q.next !== null;
      return started.size < items;
    }) ?? null
  );
}

export async function runQueue({ items = Infinity, includeManual = false, only = null, readCards, buildPart, isClosed = async () => false, finishEpic = async () => {}, log = () => {} }) {
  const started = new Set();
  const finished = new Set();
  const closedParts = new Set();
  let previous = null;

  for (;;) {
    // the project's "Item closed" workflow moves a merged part's card to Done asynchronously, so a closed issue is Done here already
    const cards = (await readCards()).map((c) => (closedParts.has(c.number) ? { ...c, status: "Done" } : c));
    const queue = queueOf(cards).filter((q) => !finished.has(q.number));

    const closable = queue.find((q) => q.epic && started.has(q.number) && q.next === null);
    if (closable) {
      await finishEpic(closable, cards);
      finished.add(closable.number);
      continue;
    }

    const item = only !== null && only !== undefined ? planOne(queue, Number(only), cards)[0] : pickNext(queue, { started, items, includeManual });
    if (!item) return;

    const continuation = queue.find((q) => q.number === previous && q.epic && q.next !== null);
    if (continuation && continuation.number !== item.number) log(`kolejka zmieniona: #${item.number} (${shortOf(item.priority)}) przed #${continuation.number}`);

    started.add(item.number);
    const part = nextWork(item, cards);
    if (part === null) {
      await finishEpic(item, cards);
      finished.add(item.number);
      continue;
    }

    const result = await buildPart(item, part, cards);
    if (!item.epic || result === false) finished.add(item.number);
    else if (await isClosed(part)) closedParts.add(part);
    previous = item.number;
  }
}

export function areaWarning(item, cards) {
  const number = nextWork(item, cards);
  if (number === null) return null;
  const card = cards.find((c) => c.number === number);
  return card && !card.area ? `⚠ #${number} bez obszaru (Area)` : null;
}

function nextOccurrence(hhmm, now) {
  const match = TIME_RE.exec(String(hhmm));
  if (!match || Number(match[1]) > 23 || Number(match[2]) > 59) throw new Error(`"${hhmm}" is not a time: use HH:MM`);
  const at = new Date(now);
  at.setHours(Number(match[1]), Number(match[2]), 0, 0);
  if (at.getTime() <= now.getTime()) at.setDate(at.getDate() + 1);
  return at;
}

export function startAt(spec, now) {
  if (spec === undefined || spec === "now") return new Date(now);
  if (/^\d+$/.test(String(spec))) return new Date(now.getTime() + Number(spec) * 60e3);
  return nextOccurrence(spec, now);
}

export const windowEnd = (hhmm, now) => nextOccurrence(hhmm, now);

export const mayStart = (until, now) => (until && now.getTime() >= until.getTime() ? "window" : null);

export async function startWork({ item, part, gh, build }) {
  for (const number of new Set([item, part])) await gh(["issue", "edit", String(number), "--add-label", "autobuild"]);
  return build();
}
